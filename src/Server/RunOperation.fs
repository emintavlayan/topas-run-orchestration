module RunOperation

open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions
open FsToolkit.ErrorHandling
open Microsoft.Data.Sqlite
open Shared
open TsebtConfig
open Bootstrap

/// Returns nullable database values as option strings.
let private asOptionalString (value: obj) : string option =
    if isNull value || Convert.IsDBNull value then None else Some(string value)

/// Resolves the sqlite connection string from configured app root.
let private toConnectionString (settings: TsebtSettings) : string =
    let databasePath = combineAppRoot settings.AppRoot settings.Paths.Database
    let builder = SqliteConnectionStringBuilder()
    builder.DataSource <- databasePath
    builder.ConnectionString

/// Resolves the absolute run batch folder path for a seed base.
let private runBatchFolderPath (settings: TsebtSettings) (seedBase: string) : string =
    Path.Combine(settings.AppRoot, settings.Paths.Runs, seedBase)

/// Reads run batch summary rows from generated batch and run metadata.
let private readRunBatchSummaries (connection: SqliteConnection) : Result<RunBatchSummary list, string> =
    try
        use command = connection.CreateCommand()

        command.CommandText <-
            """SELECT
  b.seed_base,
  b.created_at,
  COALESCE(b.run_status, 'Generated') AS run_status,
  b.slurm_job_id,
  COUNT(r.id) AS generated_input_count,
  COUNT(DISTINCT r.node_digit) AS node_count,
  COUNT(DISTINCT r.phase_space_index) AS phase_space_count
FROM generated_batches b
LEFT JOIN generated_runs r ON r.batch_id = b.id
GROUP BY b.id, b.seed_base, b.created_at, b.run_status, b.slurm_job_id
ORDER BY b.created_at DESC;"""

        use reader = command.ExecuteReader()
        let results = ResizeArray<RunBatchSummary>()

        while reader.Read() do
            results.Add(
                {
                    SeedBase = reader.GetString(0)
                    CreatedAt = reader.GetString(1)
                    RunStatus = reader.GetString(2)
                    SlurmJobId = asOptionalString (reader.GetValue(3))
                    GeneratedInputCount = reader.GetInt32(4)
                    NodeCount = reader.GetInt32(5)
                    PhaseSpaceCount = reader.GetInt32(6)
                }
            )

        Ok(List.ofSeq results)
    with ex ->
        Error $"Failed reading run batch summaries: {ex.Message}"

/// Returns run batch summaries for all generated seed bases.
let getRunBatches (settings: TsebtSettings) : Result<RunBatchSummary list, string> =
    try
        use connection = new SqliteConnection(toConnectionString settings)
        connection.Open()
        readRunBatchSummaries connection
    with ex ->
        Error $"Failed loading run batches: {ex.Message}"

/// Reads one run batch details row by seed base.
let private readRunBatchDetailsHeader
    (connection: SqliteConnection)
    (seedBase: string)
    : Result<RunBatchDetails, string> =
    try
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT id, seed_base, created_at, COALESCE(run_status, 'Generated'), slurm_job_id, manifest_path, script_path, submitted_at FROM generated_batches WHERE seed_base = $seedBase LIMIT 1;"
        command.Parameters.AddWithValue("$seedBase", seedBase) |> ignore
        use reader = command.ExecuteReader()

        if not (reader.Read()) then
            Error $"Run batch not found for seed base: {seedBase}"
        else
            let batchId = reader.GetInt64(0)

            use countCommand = connection.CreateCommand()
            countCommand.CommandText <- "SELECT COUNT(id), COUNT(DISTINCT node_digit), COUNT(DISTINCT phase_space_index) FROM generated_runs WHERE batch_id = $batchId;"
            countCommand.Parameters.AddWithValue("$batchId", batchId) |> ignore
            use countReader = countCommand.ExecuteReader()
            countReader.Read() |> ignore

            Ok
                {
                    SeedBase = reader.GetString(1)
                    CreatedAt = reader.GetString(2)
                    RunStatus = reader.GetString(3)
                    SlurmJobId = asOptionalString (reader.GetValue(4))
                    ManifestPath = asOptionalString (reader.GetValue(5))
                    ScriptPath = asOptionalString (reader.GetValue(6))
                    SubmittedAt = asOptionalString (reader.GetValue(7))
                    GeneratedInputCount = countReader.GetInt32(0)
                    NodeCount = countReader.GetInt32(1)
                    PhaseSpaceCount = countReader.GetInt32(2)
                    Rows = []
                }
    with ex ->
        Error $"Failed reading run batch details: {ex.Message}"

/// Reads per-run manifest rows for one seed base.
let private readRunManifestRows
    (connection: SqliteConnection)
    (seedBase: string)
    : Result<RunManifestRow list, string> =
    try
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT r.run_id, r.input_file_path, r.output_file_path, r.run_folder, r.seed, r.node_digit, r.phase_space_index FROM generated_runs r INNER JOIN generated_batches b ON b.id = r.batch_id WHERE b.seed_base = $seedBase ORDER BY r.id;"
        command.Parameters.AddWithValue("$seedBase", seedBase) |> ignore
        use reader = command.ExecuteReader()
        let rows = ResizeArray<RunManifestRow>()

        while reader.Read() do
            rows.Add(
                {
                    RunId = reader.GetString(0)
                    InputFilePath = reader.GetString(1)
                    OutputFilePath = reader.GetString(2)
                    RunFolder = reader.GetString(3)
                    Seed = reader.GetString(4)
                    NodeDigit = reader.GetString(5)
                    PhaseSpaceIndex = reader.GetString(6)
                }
            )

        Ok(List.ofSeq rows)
    with ex ->
        Error $"Failed reading run manifest rows: {ex.Message}"

/// Returns detailed run batch data by seed base.
let getRunBatchDetails (settings: TsebtSettings) (seedBase: string) : Result<RunBatchDetails, string> =
    try
        use connection = new SqliteConnection(toConnectionString settings)
        connection.Open()

        result {
            let! header = readRunBatchDetailsHeader connection seedBase
            let! rows = readRunManifestRows connection seedBase
            return { header with Rows = rows }
        }
    with ex ->
        Error $"Failed loading run batch details: {ex.Message}"

/// Builds one preflight check result value.
let private runCheck (name: string) (ok: bool) (message: string option) : RunPreflightCheck =
    {
        Name = name
        Ok = ok
        Message = if ok then None else message
    }

/// Returns true when all generated input files exist on disk.
let private allInputFilesExist (rows: RunManifestRow list) : bool =
    rows |> List.forall (fun row -> File.Exists row.InputFilePath)

/// Returns true when no output base path exists yet.
let private noOutputPathCollisions (rows: RunManifestRow list) : bool =
    rows
    |> List.exists (fun row -> File.Exists row.OutputFilePath || Directory.Exists row.OutputFilePath)
    |> not

/// Returns true when no output csv path exists yet.
let private noOutputCsvCollisions (rows: RunManifestRow list) : bool =
    rows |> List.exists (fun row -> File.Exists(row.OutputFilePath + ".csv")) |> not

/// Returns true when no output log path exists yet.
let private noOutputLogCollisions (rows: RunManifestRow list) : bool =
    rows |> List.exists (fun row -> File.Exists(row.OutputFilePath + ".log")) |> not

let private sanitizeNodeFileSuffix (nodeName: string) : string =
    nodeName.Replace(" ", "-")

type private PlannedNodeBatch = {
    NodeName: string
    ManifestPath: string
    ScriptPath: string
    ManifestRows: RunManifestPreviewRow list
}

let private planNodeBatches (settings: TsebtSettings) (seedBase: string) (rows: RunManifestRow list) : PlannedNodeBatch list =
    let resolveNodeName (row: RunManifestRow) =
        settings.Nodes
        |> List.tryFind (fun configuredNode -> configuredNode.Digit = row.NodeDigit)
        |> Option.map _.Name
        |> Option.defaultValue $"node{row.NodeDigit.PadLeft(2, '0')}"

    let mappedRows =
        rows
        |> List.map (fun row -> {
            TaskId = 0
            NodeName = resolveNodeName row
            RunId = row.RunId
            InputFilePath = row.InputFilePath
            LogFilePath = Path.Combine(settings.AppRoot, settings.Paths.Runs, seedBase, $"{row.RunId}.log")
        })

    let rowsByNode =
        mappedRows
        |> List.groupBy _.NodeName
        |> Map.ofList

    settings.Nodes
    |> List.choose (fun node ->
        let nodeName = node.Name

        match rowsByNode |> Map.tryFind nodeName with
        | None -> None
        | Some nodeRows ->
            let fileSuffix = sanitizeNodeFileSuffix nodeName
            let manifestPath = Path.Combine(settings.AppRoot, settings.Paths.Runs, seedBase, $"run_manifest_{fileSuffix}.tsv")
            let scriptPath = Path.Combine(settings.AppRoot, settings.Paths.Runs, seedBase, $"run_batch_{fileSuffix}.slurm")
            let renumberedRows = nodeRows |> List.mapi (fun index row -> { row with TaskId = index + 1 })

            Some {
                NodeName = nodeName
                ManifestPath = manifestPath
                ScriptPath = scriptPath
                ManifestRows = renumberedRows
            })

/// Evaluates Run preflight checks from batch metadata and generated rows.
let private evaluatePreflight
    (settings: TsebtSettings)
    (details: RunBatchDetails)
    : RunPreflightResult =
    let hasRows = details.Rows |> List.isEmpty |> not
    let isGeneratedStatus = String.Equals(details.RunStatus, "Generated", StringComparison.OrdinalIgnoreCase)
    let noPreviousSlurmJob = details.SlurmJobId |> Option.isNone
    let inputFilesOk = hasRows && allInputFilesExist details.Rows
    let runFolderExists = Directory.Exists(runBatchFolderPath settings details.SeedBase)
    let noOutputBaseCollisions = noOutputPathCollisions details.Rows
    let noOutputCsv = noOutputCsvCollisions details.Rows
    let noOutputLog = noOutputLogCollisions details.Rows
    let plannedNodeBatches = if hasRows then planNodeBatches settings details.SeedBase details.Rows else []
    let noPlannedManifestCollisions =
        plannedNodeBatches
        |> List.exists (fun batch -> File.Exists batch.ManifestPath || Directory.Exists batch.ManifestPath)
        |> not
    let noPlannedScriptCollisions =
        plannedNodeBatches
        |> List.exists (fun batch -> File.Exists batch.ScriptPath || Directory.Exists batch.ScriptPath)
        |> not

    let checks =
        [
            runCheck
                "Generated runs found"
                hasRows
                (Some $"No generated runs were found for seed base: {details.SeedBase}")
            runCheck
                "Batch status is Generated"
                isGeneratedStatus
                (Some $"Run batch status must be Generated but was {details.RunStatus}.")
            runCheck
                "No previous Slurm job"
                noPreviousSlurmJob
                (Some "This run batch already has a Slurm job id.")
            runCheck
                "Input files exist"
                inputFilesOk
                (Some "One or more generated input files are missing.")
            runCheck
                "Run folder exists"
                runFolderExists
                (Some $"Run folder does not exist: {runBatchFolderPath settings details.SeedBase}")
            runCheck
                "No output collisions"
                noOutputBaseCollisions
                (Some "One or more output base paths already exist.")
            runCheck
                "No output CSV collisions"
                noOutputCsv
                (Some "One or more output csv files already exist.")
            runCheck
                "No log collisions"
                noOutputLog
                (Some "One or more output log files already exist.")
            runCheck
                "No run manifest collisions"
                noPlannedManifestCollisions
                (Some "One or more planned run manifest paths already exist.")
            runCheck
                "No run script collisions"
                noPlannedScriptCollisions
                (Some "One or more planned run script paths already exist.")
        ]

    {
        SeedBase = details.SeedBase
        CanSubmit = checks |> List.forall _.Ok
        Checks = checks
    }

/// Runs Run preflight checks by seed base.
let preflightRun (settings: TsebtSettings) (seedBase: string) : Result<RunPreflightResult, string> =
    result {
        let! details = getRunBatchDetails settings seedBase
        return evaluatePreflight settings details
    }

/// Builds one manifest preview row from one generated run row.
let toManifestPreviewRow (settings: TsebtSettings) (seedBase: string) (taskId: int) (row: RunManifestRow) : RunManifestPreviewRow =
    let nodeName =
        settings.Nodes
        |> List.tryFind (fun configuredNode -> configuredNode.Digit = row.NodeDigit)
        |> Option.map _.Name
        |> Option.defaultValue $"node{row.NodeDigit.PadLeft(2, '0')}"

    {
        TaskId = taskId
        NodeName = nodeName
        RunId = row.RunId
        InputFilePath = row.InputFilePath
        LogFilePath = Path.Combine(settings.AppRoot, settings.Paths.Runs, seedBase, $"{row.RunId}.log")
    }

/// Formats one manifest preview row as a TSV line.
let formatManifestRow (row: RunManifestPreviewRow) : string =
    $"{row.TaskId}\t{row.NodeName}\t{row.RunId}\t{row.InputFilePath}\t{row.LogFilePath}"

/// Parses Slurm job id from sbatch output.
let parseSlurmJobId (sbatchOutput: string) : Result<string, string> =
    let matchResult = Regex.Match(sbatchOutput, @"Submitted\s+batch\s+job\s+(\d+)", RegexOptions.IgnoreCase)

    if matchResult.Success then
        Ok matchResult.Groups[1].Value
    else
        Error $"Failed parsing Slurm job id from sbatch output: {sbatchOutput}"

/// Builds Slurm script text for a run batch manifest.
let buildSlurmScriptText
    (settings: TsebtSettings)
    (seedBase: string)
    (nodeName: string)
    (manifestPath: string)
    (rowCount: int)
    : string =
    let runsFolder = Path.Combine(settings.AppRoot, settings.Paths.Runs, seedBase)
    let slurmOutputPath = Path.Combine(runsFolder, "slurm-" + nodeName + "-%A_%a.out")
    let accountLine =
        if String.IsNullOrWhiteSpace settings.Slurm.Account then
            []
        else
            [ $"#SBATCH --account={settings.Slurm.Account}" ]

    [
        [ "#!/usr/bin/env bash"
          $"#SBATCH --job-name=tsebt-{seedBase}-{nodeName}"
          $"#SBATCH --partition={settings.Slurm.Partition}" ]
        accountLine
        [ $"#SBATCH --cpus-per-task={settings.Slurm.CpusPerTask}"
          $"#SBATCH --nodelist={nodeName}"
          $"#SBATCH --array=1-{rowCount}"
          $"#SBATCH --chdir={runsFolder}"
          $"#SBATCH --output={slurmOutputPath}"
          ""
          "set -euo pipefail"
          ""
          $"TOPAS=\"{settings.Topas.Executable}\""
          $"MANIFEST=\"{manifestPath}\""
          "ROW=$(awk -F '\\t' -v task_id=\"$SLURM_ARRAY_TASK_ID\" '$1 == task_id { print; exit }' \"$MANIFEST\")"
          "if [ -z \"$ROW\" ]; then"
          "  echo \"Manifest row not found for task id $SLURM_ARRAY_TASK_ID\" >&2"
          "  exit 1"
          "fi"
          "IFS=$'\\t' read -r TASK_ID NODE_NAME RUN_ID INPUT_FILE LOG_FILE <<< \"$ROW\""
          "\"$TOPAS\" \"$INPUT_FILE\" > \"$LOG_FILE\" 2>&1" ]
    ]
    |> List.concat
    |> String.concat "\n"

/// Executes sbatch for the given script path and returns combined process output.
let private runSbatchProcess (scriptPath: string) : Result<string, string> =
    try
        let startInfo = ProcessStartInfo()
        startInfo.FileName <- "sbatch"
        startInfo.Arguments <- scriptPath
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.UseShellExecute <- false
        startInfo.CreateNoWindow <- true

        use proc = new Process()
        proc.StartInfo <- startInfo
        proc.Start() |> ignore
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        let combined = $"{stdout}\n{stderr}".Trim()

        if proc.ExitCode = 0 then
            Ok combined
        else
            Error $"sbatch failed with exit code {proc.ExitCode}. Output: {combined}"
    with ex ->
        Error $"Failed executing sbatch: {ex.Message}"

/// Writes manifest and script files for one run batch.
let private writeRunBatchFiles (manifestPath: string) (manifestText: string) (scriptPath: string) (scriptText: string) : Result<unit, string> =
    try
        let runFolder = Path.GetDirectoryName(manifestPath)

        if String.IsNullOrWhiteSpace runFolder then
            Error $"Invalid run folder for manifest path: {manifestPath}"
        else
            Directory.CreateDirectory(runFolder) |> ignore
            File.WriteAllText(manifestPath, manifestText)
            File.WriteAllText(scriptPath, scriptText)
            Ok()
    with ex ->
        Error $"Failed writing run manifest/script files: {ex.Message}"

/// Builds a run script preview payload from existing generated run metadata.
let previewRun (settings: TsebtSettings) (seedBase: string) : Result<RunScriptPreview, string> =
    result {
        let! details = getRunBatchDetails settings seedBase
        let! preflight = preflightRun settings seedBase
        let nodeBatches = planNodeBatches settings seedBase details.Rows
        let firstNode = nodeBatches |> List.tryHead
        let firstManifestPath = firstNode |> Option.map _.ManifestPath |> Option.defaultValue ""
        let firstScriptPath = firstNode |> Option.map _.ScriptPath |> Option.defaultValue ""
        let firstScriptText =
            firstNode
            |> Option.map (fun batch -> buildSlurmScriptText settings seedBase batch.NodeName batch.ManifestPath batch.ManifestRows.Length)
            |> Option.defaultValue ""
        let firstRows = firstNode |> Option.map _.ManifestRows |> Option.defaultValue []
        let nodeScriptPreviews =
            nodeBatches
            |> List.map (fun batch -> {
                NodeName = batch.NodeName
                ManifestPath = batch.ManifestPath
                ScriptPath = batch.ScriptPath
                TaskCount = batch.ManifestRows.Length
                ManifestRowsPreview = batch.ManifestRows |> List.truncate 20
                ScriptText = buildSlurmScriptText settings seedBase batch.NodeName batch.ManifestPath batch.ManifestRows.Length
            })

        return
            {
                SeedBase = seedBase
                ManifestPath = firstManifestPath
                ScriptPath = firstScriptPath
                ScriptText = firstScriptText
                RunCount = details.GeneratedInputCount
                ManifestRowsPreview = firstRows |> List.truncate 20
                NodeScriptPreviews = nodeScriptPreviews
                Preflight = preflight
            }
    }

/// Updates generated batch submission metadata after successful sbatch.
let private updateRunSubmissionStatus
    (connection: SqliteConnection)
    (seedBase: string)
    (slurmJobIds: string list)
    (manifestPath: string)
    (scriptPath: string)
    (sbatchOutput: string)
    (submittedRunCount: int)
    : Result<SubmitRunResult, string> =
    try
        let submittedAt = DateTime.UtcNow.ToString("O")
        let slurmJobId = String.concat "," slurmJobIds
        use command = connection.CreateCommand()
        command.CommandText <- "UPDATE generated_batches SET run_status = $runStatus, slurm_job_id = $slurmJobId, submitted_at = $submittedAt, manifest_path = $manifestPath, script_path = $scriptPath WHERE seed_base = $seedBase;"
        command.Parameters.AddWithValue("$runStatus", "Submitted") |> ignore
        command.Parameters.AddWithValue("$slurmJobId", slurmJobId) |> ignore
        command.Parameters.AddWithValue("$submittedAt", submittedAt) |> ignore
        command.Parameters.AddWithValue("$manifestPath", manifestPath) |> ignore
        command.Parameters.AddWithValue("$scriptPath", scriptPath) |> ignore
        command.Parameters.AddWithValue("$seedBase", seedBase) |> ignore

        let affected = command.ExecuteNonQuery()

        if affected = 0 then
            Error $"Run batch not found for seed base: {seedBase}"
        else
            use runsCommand = connection.CreateCommand()
            runsCommand.CommandText <- "UPDATE generated_runs SET status = $status WHERE batch_id = (SELECT id FROM generated_batches WHERE seed_base = $seedBase LIMIT 1);"
            runsCommand.Parameters.AddWithValue("$status", "Submitted") |> ignore
            runsCommand.Parameters.AddWithValue("$seedBase", seedBase) |> ignore
            runsCommand.ExecuteNonQuery() |> ignore

            Ok
                {
                    SeedBase = seedBase
                    RunStatus = "Submitted"
                    SlurmJobId = slurmJobId
                    SlurmJobIds = slurmJobIds
                    SubmittedRunCount = submittedRunCount
                    ManifestPath = manifestPath
                    ScriptPath = scriptPath
                    SbatchOutput = sbatchOutput
                    SubmittedAt = submittedAt
                }
    with ex ->
        Error $"Failed updating run submission status: {ex.Message}"

/// Returns true when run status is a non-rerunnable submitted state.
let private isAlreadySubmittedStatus (status: string) : bool =
    [ "Submitted"; "Running"; "Completed"; "Failed" ]
    |> List.exists (fun value -> String.Equals(value, status, StringComparison.OrdinalIgnoreCase))

/// Submits one generated run batch to Slurm after successful preflight.
let submitRun (settings: TsebtSettings) (request: SubmitRunRequest) : Result<SubmitRunResult, string> =
    result {
        let! details = getRunBatchDetails settings request.SeedBase

        if isAlreadySubmittedStatus details.RunStatus && details.SlurmJobId.IsSome then
            return!
                Error $"Batch {request.SeedBase} has already been submitted to Slurm as job {details.SlurmJobId.Value}."

        let! preview = previewRun settings request.SeedBase

        if not preview.Preflight.CanSubmit then
            return! Error $"Run preflight failed for seed base: {request.SeedBase}"

        let nodeBatches = planNodeBatches settings request.SeedBase details.Rows
        let collisions =
            nodeBatches
            |> List.collect (fun batch -> [ batch.ManifestPath; batch.ScriptPath ])
            |> List.filter (fun path -> File.Exists path || Directory.Exists path)

        if not collisions.IsEmpty then
            let collisionText = String.concat ", " collisions
            return! Error $"Run preflight failed for seed base: {request.SeedBase}. Existing manifest/script paths: {collisionText}"

        let manifestHeader = "task_id\tnode_name\trun_id\tinput_file_path\tlog_file_path"
        let rec submitNodeBatches
            (pending: PlannedNodeBatch list)
            (submittedJobIds: string list)
            (submittedOutputs: string list)
            : Result<string list * string list, string> =
            result {
                match pending with
                | [] -> return submittedJobIds, submittedOutputs
                | batch :: rest ->
                    let manifestLines = batch.ManifestRows |> List.map formatManifestRow
                    let manifestText = String.concat "\n" (manifestHeader :: manifestLines)
                    let scriptText = buildSlurmScriptText settings request.SeedBase batch.NodeName batch.ManifestPath batch.ManifestRows.Length
                    do! writeRunBatchFiles batch.ManifestPath manifestText batch.ScriptPath scriptText

                    let! sbatchOutput =
                        runSbatchProcess batch.ScriptPath
                        |> Result.mapError (fun err ->
                            if submittedJobIds.IsEmpty then
                                err
                            else
                                let idsText = String.concat "," submittedJobIds
                                $"{err} Submitted job ids before failure: {idsText}")

                    let! slurmJobId =
                        parseSlurmJobId sbatchOutput
                        |> Result.mapError (fun err ->
                            if submittedJobIds.IsEmpty then
                                err
                            else
                                let idsText = String.concat "," submittedJobIds
                                $"{err} Submitted job ids before failure: {idsText}")

                    let earlierIds=String.concat "," submittedJobIds
                    do!
                        RunControlStore.recordSubmission settings request.SeedBase batch.NodeName slurmJobId batch.ManifestPath
                        |> Result.mapError (fun error -> $"{error}. Slurm accepted job {slurmJobId}; inspect Run Control before taking further action. Earlier accepted jobs: {earlierIds}")

                    return!
                        submitNodeBatches
                            rest
                            (submittedJobIds @ [ slurmJobId ])
                            (submittedOutputs @ [ $"[{batch.NodeName}] {sbatchOutput}" ])
            }

        let! submittedJobIds, submittedOutputs = submitNodeBatches nodeBatches [] []

        let allManifestPaths = nodeBatches |> List.map _.ManifestPath
        let allScriptPaths = nodeBatches |> List.map _.ScriptPath
        let sbatchOutput = String.concat "\n" submittedOutputs
        let primaryManifestPath = allManifestPaths |> List.tryHead |> Option.defaultValue ""
        let primaryScriptPath = allScriptPaths |> List.tryHead |> Option.defaultValue ""

        try
            use connection = new SqliteConnection(toConnectionString settings)
            connection.Open()
            return! updateRunSubmissionStatus connection request.SeedBase submittedJobIds primaryManifestPath primaryScriptPath sbatchOutput details.Rows.Length
        with ex ->
            return! Error $"Failed submitting run batch: {ex.Message}"
    }
