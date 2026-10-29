module CollectOperation

open System
open FsToolkit.ErrorHandling
open Microsoft.Data.Sqlite
open Shared
open TsebtConfig
open Bootstrap
open CollectPlanning
open CollectPreflight
open CollectCsvMerge
open CollectStatistics
open System.IO

/// Returns nullable database values as optional strings.
let private asOptionalString (value: obj) : string option =
    if isNull value || Convert.IsDBNull value then None else Some(string value)

/// Returns nullable database values as optional integers.
let private asOptionalInt (value: obj) : int option =
    if isNull value || Convert.IsDBNull value then None else Some(Convert.ToInt32 value)

/// Converts one optional value into a database parameter value.
let private toDbValue (value: 'a option) : obj =
    value |> Option.map box |> Option.defaultValue null

/// Builds sqlite connection string from configured database path.
let private toConnectionString (settings: TsebtSettings) : string =
    let databasePath = combineAppRoot settings.AppRoot settings.Paths.Database
    let builder = SqliteConnectionStringBuilder()
    builder.DataSource <- databasePath
    builder.ConnectionString

/// Returns one UTC timestamp string used by collect logs.
let private collectLogTimestampUtc () : string = DateTime.UtcNow.ToString("O")

/// Writes one collect operation stage log line to stdout.
let private logCollectStage (seedBase: string) (stage: string) (message: string) : unit =
    Console.WriteLine($"[{collectLogTimestampUtc()}] [Collect] [seedBase={seedBase}] [{stage}] {message}")

/// Formats one sortable UTC collection folder timestamp.
let private collectionFolderTimestampUtc () : string =
    DateTime.UtcNow.ToString("yyyyMMddTHHmmss")

/// Builds one collection folder name from a base timestamp and optional collision suffix.
let private buildCollectionFolderName (timestamp: string) (suffix: int) : string =
    if suffix <= 0 then
        timestamp
    else
        $"{timestamp}-{suffix:D3}"

/// Creates one unique timestamped collection output folder without overwriting prior outputs.
let private createCollectionOutputFolder (settings: TsebtSettings) (seedBase: string) : Result<string, string> =
    try
        let baseOutputFolder = outputFolderPath settings seedBase
        Directory.CreateDirectory(baseOutputFolder) |> ignore

        let timestamp = collectionFolderTimestampUtc ()

        let rec loop suffix =
            let folderName = buildCollectionFolderName timestamp suffix
            let candidate = collectionOutputFolderPath baseOutputFolder folderName

            if Directory.Exists candidate then
                loop (suffix + 1)
            else
                Directory.CreateDirectory(candidate) |> ignore
                candidate

        Ok(loop 0)
    with ex ->
        Error $"Failed creating timestamped collect output folder: {ex.Message}"

/// Converts one absolute path into an AppRoot-relative display path when possible.
let private toAppRootRelativePath (settings: TsebtSettings) (path: string) : string =
    try
        let appRootFullPath = Path.GetFullPath settings.AppRoot
        let candidateFullPath = Path.GetFullPath path
        Path.GetRelativePath(appRootFullPath, candidateFullPath).Replace('\\', '/')
    with _ ->
        path

/// Writes the latest-collection marker file for one seed base.
let private writeLatestCollectionMarker
    (settings: TsebtSettings)
    (seedBase: string)
    (collectionOutputFolder: string)
    : Result<unit, string> =
    try
        let markerPath = latestCollectionMarkerPath settings seedBase
        let markerDirectory = Path.GetDirectoryName markerPath

        if not (String.IsNullOrWhiteSpace markerDirectory) then
            Directory.CreateDirectory(markerDirectory) |> ignore

        File.WriteAllText(markerPath, toAppRootRelativePath settings collectionOutputFolder)
        Ok()
    with ex ->
        Error $"Failed writing latest collection marker: {ex.Message}"

/// Inserts one collection-attempt history row and returns its database identifier.
let private createCollectionAttempt (connection: SqliteConnection) (seedBase: string) : Result<int64, string> =
    try
        let startedAt = DateTime.UtcNow.ToString("O")
        use command = connection.CreateCommand()
        command.CommandText <-
            """INSERT INTO generated_batch_collections (
  seed_base,
  status,
  started_at,
  completed_at,
  output_folder,
  summary_path,
  csv_found_count,
  csv_missing_count,
  log_found_count,
  log_missing_count,
  error_message
) VALUES (
  $seedBase,
  'InProgress',
  $startedAt,
  NULL,
  NULL,
  NULL,
  NULL,
  NULL,
  NULL,
  NULL,
  NULL
);"""

        command.Parameters.AddWithValue("$seedBase", seedBase) |> ignore
        command.Parameters.AddWithValue("$startedAt", startedAt) |> ignore
        command.ExecuteNonQuery() |> ignore

        use idCommand = connection.CreateCommand()
        idCommand.CommandText <- "SELECT last_insert_rowid();"
        Ok(Convert.ToInt64(idCommand.ExecuteScalar()))
    with ex ->
        Error $"Failed creating collect attempt history: {ex.Message}"

/// Marks one collection-attempt history row as successfully completed.
let private markCollectionAttemptSucceeded
    (connection: SqliteConnection)
    (attemptId: int64)
    (completedAt: string)
    (outputFolder: string)
    (summaryPath: string)
    (preflight: CollectPreflightResult)
    : Result<unit, string> =
    try
        use command = connection.CreateCommand()
        command.CommandText <-
            """UPDATE generated_batch_collections
SET status = 'Collected',
    completed_at = $completedAt,
    output_folder = $outputFolder,
    summary_path = $summaryPath,
    csv_found_count = $collectCsvFoundCount,
    csv_missing_count = $collectCsvMissingCount,
    log_found_count = $collectLogFoundCount,
    log_missing_count = $collectLogMissingCount,
    error_message = NULL
WHERE id = $attemptId;"""

        command.Parameters.AddWithValue("$completedAt", completedAt) |> ignore
        command.Parameters.AddWithValue("$outputFolder", outputFolder) |> ignore
        command.Parameters.AddWithValue("$summaryPath", summaryPath) |> ignore
        command.Parameters.AddWithValue("$collectCsvFoundCount", preflight.FoundCsvCount) |> ignore
        command.Parameters.AddWithValue("$collectCsvMissingCount", preflight.MissingCsvCount) |> ignore
        command.Parameters.AddWithValue("$collectLogFoundCount", preflight.FoundLogCount) |> ignore
        command.Parameters.AddWithValue("$collectLogMissingCount", preflight.MissingLogCount) |> ignore
        command.Parameters.AddWithValue("$attemptId", attemptId) |> ignore
        command.ExecuteNonQuery() |> ignore
        Ok()
    with ex ->
        Error $"Failed marking collect attempt success: {ex.Message}"

/// Marks one collection-attempt history row as failed.
let private markCollectionAttemptFailed
    (connection: SqliteConnection)
    (attemptId: int64)
    (outputFolder: string option)
    (summaryPath: string option)
    (preflight: CollectPreflightResult option)
    (errorMessage: string)
    : Result<unit, string> =
    try
        use command = connection.CreateCommand()
        command.CommandText <-
            """UPDATE generated_batch_collections
SET status = 'Failed',
    completed_at = $completedAt,
    output_folder = $outputFolder,
    summary_path = $summaryPath,
    csv_found_count = $collectCsvFoundCount,
    csv_missing_count = $collectCsvMissingCount,
    log_found_count = $collectLogFoundCount,
    log_missing_count = $collectLogMissingCount,
    error_message = $errorMessage
WHERE id = $attemptId;"""

        let completedAt = DateTime.UtcNow.ToString("O")
        command.Parameters.AddWithValue("$completedAt", completedAt) |> ignore
        command.Parameters.AddWithValue("$outputFolder", toDbValue outputFolder) |> ignore
        command.Parameters.AddWithValue("$summaryPath", toDbValue summaryPath) |> ignore
        command.Parameters.AddWithValue("$collectCsvFoundCount", toDbValue (preflight |> Option.map _.FoundCsvCount)) |> ignore
        command.Parameters.AddWithValue("$collectCsvMissingCount", toDbValue (preflight |> Option.map _.MissingCsvCount)) |> ignore
        command.Parameters.AddWithValue("$collectLogFoundCount", toDbValue (preflight |> Option.map _.FoundLogCount)) |> ignore
        command.Parameters.AddWithValue("$collectLogMissingCount", toDbValue (preflight |> Option.map _.MissingLogCount)) |> ignore
        command.Parameters.AddWithValue("$errorMessage", errorMessage) |> ignore
        command.Parameters.AddWithValue("$attemptId", attemptId) |> ignore
        command.ExecuteNonQuery() |> ignore
        Ok()
    with ex ->
        Error $"Failed marking collect attempt failure: {ex.Message}"

/// Records one failed collection-attempt history update without masking the main error.
let private recordCollectionAttemptFailureBestEffort
    (seedBase: string)
    (connection: SqliteConnection)
    (attemptId: int64 option)
    (outputFolder: string option)
    (summaryPath: string option)
    (preflight: CollectPreflightResult option)
    (errorMessage: string)
    : unit =
    match attemptId with
    | None -> ()
    | Some value ->
        match markCollectionAttemptFailed connection value outputFolder summaryPath preflight errorMessage with
        | Ok() -> ()
        | Error historyError ->
            logCollectStage seedBase "AttemptFailureUpdateError" $"{historyError}; originalError={errorMessage}"

/// Reads collect batch summaries from generated batch and run metadata.
let private readCollectBatchSummaries (connection: SqliteConnection) : Result<CollectBatchSummary list, string> =
    try
        use command = connection.CreateCommand()

        command.CommandText <-
            """SELECT
  b.seed_base,
  b.created_at,
  COUNT(r.id) AS generated_run_count,
  COUNT(DISTINCT r.node_digit) AS node_count,
  COUNT(DISTINCT r.phase_space_index) AS phase_space_count,
  b.run_status,
  COALESCE(b.collect_status, 'NotCollected') AS collect_status,
  b.collect_summary_path,
  b.collect_output_folder
FROM generated_batches b
LEFT JOIN generated_runs r ON r.batch_id = b.id
GROUP BY b.id, b.seed_base, b.created_at, b.run_status, b.collect_status, b.collect_summary_path, b.collect_output_folder
ORDER BY b.created_at DESC;"""

        use reader = command.ExecuteReader()
        let rows = ResizeArray<CollectBatchSummary>()

        while reader.Read() do
            let latestCollectionFolder = asOptionalString (reader.GetValue(8))

            rows.Add(
                {
                    SeedBase = reader.GetString(0)
                    CreatedAt = reader.GetString(1)
                    GeneratedRunCount = reader.GetInt32(2)
                    NodeCount = reader.GetInt32(3)
                    PhaseSpaceCount = reader.GetInt32(4)
                    RunStatus = asOptionalString (reader.GetValue(5))
                    CollectStatus = reader.GetString(6)
                    CollectSummaryPath = asOptionalString (reader.GetValue(7))
                    HasCollectedBefore = latestCollectionFolder.IsSome
                    LatestCollectionFolder = latestCollectionFolder
                }
            )

        Ok(List.ofSeq rows)
    with ex ->
        Error $"Failed reading collect batch summaries: {ex.Message}"

/// Returns collect batch summaries for all generated batches.
let getCollectBatches (settings: TsebtSettings) : Result<CollectBatchSummary list, string> =
    try
        use connection = new SqliteConnection(toConnectionString settings)
        connection.Open()
        readCollectBatchSummaries connection
    with ex ->
        Error $"Failed loading collect batches: {ex.Message}"

/// Reads one collect batch details row by seed base.
let private readCollectBatchDetailsRow
    (connection: SqliteConnection)
    (seedBase: string)
    : Result<CollectBatchDetails, string> =
    try
        use command = connection.CreateCommand()

        command.CommandText <-
            """SELECT
  b.seed_base,
  b.created_at,
  COUNT(r.id) AS generated_run_count,
  COUNT(DISTINCT r.node_digit) AS node_count,
  COUNT(DISTINCT r.phase_space_index) AS phase_space_count,
  b.run_status,
  COALESCE(b.collect_status, 'NotCollected') AS collect_status,
  b.collected_at,
  b.collect_output_folder,
  b.collect_summary_path,
  b.collect_csv_found_count,
  b.collect_csv_missing_count,
  b.collect_log_found_count,
  b.collect_log_missing_count
FROM generated_batches b
LEFT JOIN generated_runs r ON r.batch_id = b.id
WHERE b.seed_base = $seedBase
GROUP BY b.id, b.seed_base, b.created_at, b.run_status, b.collect_status, b.collected_at, b.collect_output_folder, b.collect_summary_path, b.collect_csv_found_count, b.collect_csv_missing_count, b.collect_log_found_count, b.collect_log_missing_count
LIMIT 1;"""

        command.Parameters.AddWithValue("$seedBase", seedBase) |> ignore
        use reader = command.ExecuteReader()

        if not (reader.Read()) then
            Error $"Collect batch not found for seed base: {seedBase}"
        else
            let latestCollectionFolder = asOptionalString (reader.GetValue(8))

            Ok
                {
                    SeedBase = reader.GetString(0)
                    CreatedAt = reader.GetString(1)
                    GeneratedRunCount = reader.GetInt32(2)
                    NodeCount = reader.GetInt32(3)
                    PhaseSpaceCount = reader.GetInt32(4)
                    RunStatus = asOptionalString (reader.GetValue(5))
                    CollectStatus = reader.GetString(6)
                    CollectedAt = asOptionalString (reader.GetValue(7))
                    CollectOutputFolder = asOptionalString (reader.GetValue(8))
                    CollectSummaryPath = asOptionalString (reader.GetValue(9))
                    CollectCsvFoundCount = asOptionalInt (reader.GetValue(10))
                    CollectCsvMissingCount = asOptionalInt (reader.GetValue(11))
                    CollectLogFoundCount = asOptionalInt (reader.GetValue(12))
                    CollectLogMissingCount = asOptionalInt (reader.GetValue(13))
                    HasCollectedBefore = latestCollectionFolder.IsSome
                    LatestCollectionFolder = latestCollectionFolder
                }
    with ex ->
        Error $"Failed reading collect batch details: {ex.Message}"

/// Returns collect batch details by seed base.
let getCollectBatchDetails (settings: TsebtSettings) (seedBase: string) : Result<CollectBatchDetails, string> =
    try
        use connection = new SqliteConnection(toConnectionString settings)
        connection.Open()
        readCollectBatchDetailsRow connection seedBase
    with ex ->
        Error $"Failed loading collect batch details: {ex.Message}"

/// Reads generated run rows needed for collect preflight and planning.
let private readCollectRunRows
    (connection: SqliteConnection)
    (seedBase: string)
    : Result<CollectRunRow list, string> =
    try
        use command = connection.CreateCommand()

        command.CommandText <-
            """SELECT
  r.run_id,
  r.phase_space_index,
  r.node_digit,
  r.output_file_path
FROM generated_runs r
INNER JOIN generated_batches b ON b.id = r.batch_id
WHERE b.seed_base = $seedBase
ORDER BY r.id;"""

        command.Parameters.AddWithValue("$seedBase", seedBase) |> ignore
        use reader = command.ExecuteReader()
        let rows = ResizeArray<CollectRunRow>()

        while reader.Read() do
            rows.Add(
                {
                    RunId = reader.GetString(0)
                    PhaseSpaceIndex = reader.GetString(1)
                    NodeDigit = reader.GetString(2)
                    OutputFilePath = reader.GetString(3)
                }
            )

        Ok(List.ofSeq rows)
    with ex ->
        Error $"Failed reading collect run rows: {ex.Message}"

/// Runs collect preflight checks for one seed base using generated run metadata.
let preflightCollect (settings: TsebtSettings) (seedBase: string) : Result<CollectPreflightResult, string> =
    try
        use connection = new SqliteConnection(toConnectionString settings)
        connection.Open()

        result {
            let! rows = readCollectRunRows connection seedBase
            return buildCollectPreflightResult settings seedBase rows [] []
        }
    with ex ->
        Error $"Failed running collect preflight: {ex.Message}"

/// Builds all output paths for one scorer inside a collection attempt.
let private buildScorerPaths (collectionFolder: string) (scorer: CollectScorer) : CollectScorerPaths =
    let folder = scorerOutputFolderPath collectionFolder scorer
    {
        Scorer = scorer.Name
        OutputFolder = folder
        SummaryPath = plannedSummaryPathInOutputFolder folder
        UncertaintyPath = plannedUncertaintyPathInOutputFolder folder
        ManifestPath = plannedManifestPathInOutputFolder folder
    }

/// Builds collect preview including planned output paths and preflight details.
let previewCollect (settings: TsebtSettings) (request: CollectPreviewRequest) : Result<CollectPreviewResult, string> =
    try
        use connection = new SqliteConnection(toConnectionString settings)
        connection.Open()

        result {
            let! details = readCollectBatchDetailsRow connection request.SeedBase
            let! rows = readCollectRunRows connection request.SeedBase
            let effectiveRows =
                applyCollectExclusions rows request.ExcludedPhaseSpaceIndexes request.ExcludedNodeDigits

            let preflight =
                buildCollectPreflightResult
                    settings
                    request.SeedBase
                    rows
                    request.ExcludedPhaseSpaceIndexes
                    request.ExcludedNodeDigits
            let outputFolder =
                collectionOutputFolderPath (outputFolderPath settings request.SeedBase) "<timestamp>"

            let scorers = discoverCollectScorers rows effectiveRows |> Result.defaultValue []
            let scorerPaths = scorers |> List.map (buildScorerPaths outputFolder)

            return
                {
                    SeedBase = request.SeedBase
                    Scorers = scorerPaths
                    ExpectedRunCount = preflight.ExpectedRunCount
                    PhaseSpaceCount = effectiveRows |> List.map _.PhaseSpaceIndex |> List.distinct |> List.length
                    NodeCount = effectiveRows |> List.map _.NodeDigit |> List.distinct |> List.length
                    OutputFolder = outputFolder
                    PlannedMergedFiles =
                        scorerPaths |> List.collect (fun paths -> plannedMergedFilesInOutputFolder paths.OutputFolder effectiveRows)
                    FinalSummaryPath = scorerPaths |> List.tryHead |> Option.map _.SummaryPath |> Option.defaultValue ""
                    ManifestPath = plannedManifestPath settings request.SeedBase
                    Preflight = preflight
                    HasCollectedBefore = details.HasCollectedBefore
                    LatestCollectionFolder = details.LatestCollectionFolder
                }
        }
    with ex ->
        Error $"Failed building collect preview: {ex.Message}"

/// Builds collect manifest lines from generated run rows.
let private buildCollectManifestLines (scorers: CollectScorer list) (rows: CollectRunRow list) : string list =
    let header = "scorer\trunId\tphaseSpaceIndex\tnodeDigit\tcsvPath\tlogPath"
    let lines =
        scorers
        |> List.collect (fun scorer ->
            rows |> List.map (fun row ->
                let csvPath = row.OutputFilePath + scorer.Suffix + ".csv"
                let logPath = row.OutputFilePath + ".log"
                $"{scorer.Name}\t{row.RunId}\t{row.PhaseSpaceIndex}\t{row.NodeDigit}\t{csvPath}\t{logPath}"))
    header :: lines

/// Validates output collision rules before collect writes any files.
let private validateCollectOutputCollisions
    (plannedMergedFilesList: string list)
    (summaryPath: string)
    (uncertaintyPath: string)
    : Result<unit, string> =
    if plannedMergedFilesList |> List.exists File.Exists then
        Error "One or more planned merged csv files already exist."
    elif File.Exists summaryPath then
        Error $"Final merged dose output file already exists: {summaryPath}"
    elif File.Exists uncertaintyPath then
        Error $"Dose uncertainty output file already exists: {uncertaintyPath}"
    else
        Ok()

/// Writes collect manifest file for one seed base.
let private writeCollectManifest (manifestPath: string) (manifestLines: string list) : Result<unit, string> =
    try
        let parent = Path.GetDirectoryName manifestPath

        if not (String.IsNullOrWhiteSpace parent) then
            Directory.CreateDirectory(parent) |> ignore

        File.WriteAllLines(manifestPath, manifestLines)
        Ok()
    with ex ->
        Error $"Failed writing collect manifest: {ex.Message}"

/// Merges node csv files grouped by phase-space index.
let private mergePhaseSpaceCsvFiles
    (collectionOutputFolder: string)
    (seedBase: string)
    (rows: CollectRunRow list)
    : Result<CollectedPhaseSpaceResult list, string> =
    rows
    |> List.groupBy _.PhaseSpaceIndex
    |> List.sortBy fst
    |> List.map (fun (phaseSpaceIndex, phaseRows) ->
        let csvInputPaths =
            phaseRows
            |> List.map expectedCsvAndLogPaths
            |> List.map fst
            |> List.distinct

        let outputPath =
            Path.Combine(mergedOutputFolderPathInCollection collectionOutputFolder, $"phsp{phaseSpaceIndex}_merged.csv")

        result {
            logCollectStage
                seedBase
                "MergePhaseSpaceStart"
                $"phaseSpaceIndex={phaseSpaceIndex}; sourceCsvCount={csvInputPaths.Length}"

            do! mergeNodeCsvFilesForPhaseSpace csvInputPaths outputPath

            logCollectStage
                seedBase
                "MergePhaseSpaceEnd"
                $"phaseSpaceIndex={phaseSpaceIndex}; sourceCsvCount={csvInputPaths.Length}; output={outputPath}"

            return
                {
                    PhaseSpaceIndex = phaseSpaceIndex
                    MergedFilePath = outputPath
                    SourceCsvCount = csvInputPaths.Length
                }
        })
    |> List.sequenceResultM

/// Collects one scorer independently using the existing merge and raw-batch uncertainty calculations.
let private collectScorer
    (seedBase: string)
    (collectionFolder: string)
    (rows: CollectRunRow list)
    (scorer: CollectScorer)
    : Result<CollectedPhaseSpaceResult list, string> =
    let paths = buildScorerPaths collectionFolder scorer
    let scorerRows = scorerRunRows scorer rows
    let rawPaths = scorerRows |> List.map (expectedCsvAndLogPaths >> fst) |> List.distinct
    result {
        logCollectStage seedBase "ScorerStart" $"scorer={scorer.Name}; sourceCsvCount={rawPaths.Length}"
        do! validateCollectOutputCollisions
                (plannedMergedFilesInOutputFolder paths.OutputFolder scorerRows)
                paths.SummaryPath paths.UncertaintyPath
        let! merged = mergePhaseSpaceCsvFiles paths.OutputFolder seedBase scorerRows
        do! mergePhaseSpaceDoseCsvFiles (merged |> List.map _.MergedFilePath) paths.SummaryPath
        do! computeDoseWithUncertaintyFromRawBatchCsvFiles rawPaths paths.UncertaintyPath
        do! validateDoseMergedMatchesUncertainty paths.SummaryPath paths.UncertaintyPath
        do! writeCollectManifest paths.ManifestPath (buildCollectManifestLines [ scorer ] rows)
        logCollectStage seedBase "ScorerEnd" $"scorer={scorer.Name}; summary={paths.SummaryPath}"
        return merged
    }
    |> Result.mapError (fun message -> $"Scorer '{scorer.Name}': {message}")

/// Updates collect metadata columns in generated_batches after successful collect.
let private updateCollectedBatchMetadata
    (connection: SqliteConnection)
    (seedBase: string)
    (outputFolder: string)
    (summaryPath: string)
    (preflight: CollectPreflightResult)
    : Result<string, string> =
    try
        let collectedAt = DateTime.UtcNow.ToString("O")
        use command = connection.CreateCommand()

        command.CommandText <-
            "UPDATE generated_batches SET collect_status = $collectStatus, collected_at = $collectedAt, collect_output_folder = $collectOutputFolder, collect_summary_path = $collectSummaryPath, collect_csv_found_count = $collectCsvFoundCount, collect_csv_missing_count = $collectCsvMissingCount, collect_log_found_count = $collectLogFoundCount, collect_log_missing_count = $collectLogMissingCount WHERE seed_base = $seedBase;"

        command.Parameters.AddWithValue("$collectStatus", "Collected") |> ignore
        command.Parameters.AddWithValue("$collectedAt", collectedAt) |> ignore
        command.Parameters.AddWithValue("$collectOutputFolder", outputFolder) |> ignore
        command.Parameters.AddWithValue("$collectSummaryPath", summaryPath) |> ignore
        command.Parameters.AddWithValue("$collectCsvFoundCount", preflight.FoundCsvCount) |> ignore
        command.Parameters.AddWithValue("$collectCsvMissingCount", preflight.MissingCsvCount) |> ignore
        command.Parameters.AddWithValue("$collectLogFoundCount", preflight.FoundLogCount) |> ignore
        command.Parameters.AddWithValue("$collectLogMissingCount", preflight.MissingLogCount) |> ignore
        command.Parameters.AddWithValue("$seedBase", seedBase) |> ignore

        let affected = command.ExecuteNonQuery()

        if affected = 0 then
            Error $"Collect batch not found for seed base: {seedBase}"
        else
            Ok collectedAt
    with ex ->
        Error $"Failed updating collect metadata: {ex.Message}"

/// Executes full collect operation for one seed base.
let collectBatch (settings: TsebtSettings) (request: CollectRequest) : Result<CollectResult, string> =
    let mutable attemptId: int64 option = None
    let mutable attemptedOutputFolder: string option = None
    let mutable attemptedSummaryPath: string option = None
    let mutable attemptedPreflight: CollectPreflightResult option = None

    try
        logCollectStage request.SeedBase "Start" "Collect batch request received."
        use connection = new SqliteConnection(toConnectionString settings)
        connection.Open()

        let operationResult =
            result {
            let! _details = readCollectBatchDetailsRow connection request.SeedBase
            let! createdAttemptId = createCollectionAttempt connection request.SeedBase
            attemptId <- Some createdAttemptId
            let! rows = readCollectRunRows connection request.SeedBase
            let effectiveRows =
                applyCollectExclusions rows request.ExcludedPhaseSpaceIndexes request.ExcludedNodeDigits

            let preflight =
                buildCollectPreflightResult
                    settings
                    request.SeedBase
                    rows
                    request.ExcludedPhaseSpaceIndexes
                    request.ExcludedNodeDigits
            attemptedPreflight <- Some preflight
            logCollectStage
                request.SeedBase
                "Preflight"
                $"expectedRuns={preflight.ExpectedRunCount}; effectiveRuns={preflight.EffectiveRunCount}; effectivePhaseSpaces={preflight.EffectivePhaseSpaceCount}; effectiveNodes={preflight.EffectiveNodeCount}; csvFound={preflight.FoundCsvCount}; csvMissing={preflight.MissingCsvCount}; logFound={preflight.FoundLogCount}; logMissing={preflight.MissingLogCount}; canCollect={preflight.CanCollect}"

            if not preflight.CanCollect then
                return! Error $"Collect preflight failed for seed base: {request.SeedBase}"

            let! outputFolder = createCollectionOutputFolder settings request.SeedBase
            attemptedOutputFolder <- Some outputFolder
            let! scorers = discoverCollectScorers rows effectiveRows
            let scorerPaths = scorers |> List.map (buildScorerPaths outputFolder)
            // Keep the scalar summary field compatible; full per-scorer paths are returned separately.
            let summaryPath = scorerPaths.Head.SummaryPath
            attemptedSummaryPath <- Some summaryPath
            let manifestPath = plannedManifestPathInOutputFolder outputFolder
            let! scorerMergedFiles = scorers |> List.traverseResultM (collectScorer request.SeedBase outputFolder effectiveRows)
            let mergedFiles = scorerMergedFiles |> List.concat
            let manifestLines = buildCollectManifestLines scorers effectiveRows
            logCollectStage request.SeedBase "ManifestWriteStart" $"Writing collect manifest: {manifestPath}"
            do! writeCollectManifest manifestPath manifestLines
            logCollectStage request.SeedBase "ManifestWriteEnd" $"Collect manifest written: {manifestPath}"

            logCollectStage request.SeedBase "LatestMarkerWriteStart" "Writing latest collection marker."
            do! writeLatestCollectionMarker settings request.SeedBase outputFolder
            logCollectStage request.SeedBase "LatestMarkerWriteEnd" "Latest collection marker written."

            logCollectStage request.SeedBase "DatabaseUpdateStart" "Updating generated batch collect metadata."
            let! collectedAt =
                updateCollectedBatchMetadata connection request.SeedBase outputFolder summaryPath preflight
            logCollectStage request.SeedBase "DatabaseUpdateEnd" "Generated batch collect metadata updated."

            logCollectStage request.SeedBase "AttemptHistoryUpdateStart" "Marking collection attempt as completed."
            do! markCollectionAttemptSucceeded connection createdAttemptId collectedAt outputFolder summaryPath preflight
            logCollectStage request.SeedBase "AttemptHistoryUpdateEnd" "Collection attempt history updated."

            logCollectStage request.SeedBase "Success" "Collect completed successfully."

            return
                {
                    SeedBase = request.SeedBase
                    Scorers = scorerPaths
                    ExpectedRunCount = preflight.EffectiveRunCount
                    CsvReadCount = preflight.FoundCsvCount
                    LogFoundCount = preflight.FoundLogCount
                    MergedPhaseSpaceCount = preflight.EffectivePhaseSpaceCount
                    OutputFolder = outputFolder
                    SummaryPath = summaryPath
                    MergedFiles = mergedFiles
                    ManifestPath = manifestPath
                    Status = "Collected"
                }
        }

        match operationResult with
        | Ok collected -> Ok collected
        | Error errorMessage ->
            recordCollectionAttemptFailureBestEffort
                request.SeedBase
                connection
                attemptId
                attemptedOutputFolder
                attemptedSummaryPath
                attemptedPreflight
                errorMessage

            Error errorMessage
    with ex ->
        let errorMessage = $"Failed executing collect operation: {ex.Message}"
        logCollectStage request.SeedBase "Exception" $"Collect failed with exception: {ex.Message}"

        try
            use connection = new SqliteConnection(toConnectionString settings)
            connection.Open()
            recordCollectionAttemptFailureBestEffort
                request.SeedBase
                connection
                attemptId
                attemptedOutputFolder
                attemptedSummaryPath
                attemptedPreflight
                errorMessage
        with _ ->
            ()

        Error errorMessage

