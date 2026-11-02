module CollectPreflight

open System
open System.Globalization
open System.IO
open Shared
open CollectPlanning
open TsebtConfig

/// Builds one missing-file compatibility descriptor.
let private missingFile (fileKind: string) (path: string) : MissingCollectFile = { FileKind = fileKind; Path = path }

/// Returns true when text contains one token, ignoring case.
let private containsIgnoreCase (text: string) (token: string) : bool =
    text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0

/// Returns true when one string parses as a floating-point value in invariant culture.
let private tryParseInvariantFloat (value: string) : bool =
    let mutable parsedValue = 0.0

    Double.TryParse(
        value.Trim(),
        NumberStyles.Float ||| NumberStyles.AllowThousands,
        CultureInfo.InvariantCulture,
        &parsedValue
    )

/// Returns true when one csv row contains numeric TOPAS scorer data.
let private isNumericTopasCsvRow (line: string) : bool =
    let columns = line.Split(',')

    if columns.Length < 4 then
        false
    else
        let xOk = tryParseInvariantFloat columns[0]
        let yOk = tryParseInvariantFloat columns[1]
        let zOk = tryParseInvariantFloat columns[2]
        let fourthOk = tryParseInvariantFloat columns[3]
        let lastOk = tryParseInvariantFloat columns[columns.Length - 1]
        xOk && yOk && zOk && (fourthOk || lastOk)

/// Returns true when the csv file contains at least one numeric TOPAS data row.
let private hasNumericTopasCsvRows (path: string) : Result<bool, string> =
    try
        use reader = new StreamReader(path)
        let mutable found = false

        while not reader.EndOfStream && not found do
            let line = reader.ReadLine()
            let trimmedLine = if isNull line then "" else line.Trim()

            if not (String.IsNullOrWhiteSpace(trimmedLine)) && not (trimmedLine.StartsWith("#", StringComparison.Ordinal)) then
                found <- isNumericTopasCsvRow trimmedLine

        Ok found
    with ex ->
        Error ex.Message

/// Returns true when the TOPAS log contains the completion timing footer markers.
let hasSuccessfulTopasTimingFooter (logText: string) : bool =
    [
        "Elapsed times:"
        "Parameter Reading"
        "Initialization:"
        "Execution:"
        "Finalization:"
        "Total:"
    ]
    |> List.forall (containsIgnoreCase logText)

/// Returns one preferred TOPAS failure message extracted from the log text.
let extractTopasFailureMessage (logText: string) : string option =
    let lines =
        logText.Replace("\r\n", "\n").Split('\n')
        |> Array.map (fun (line: string) -> line.Trim())
        |> Array.filter (String.IsNullOrWhiteSpace >> not)

    let tryFindContaining (token: string) =
        lines |> Array.tryFind (fun line -> containsIgnoreCase line token)

    match tryFindContaining "TOPAS is quitting due to a serious error", tryFindContaining "does not support particle ID" with
    | Some seriousErrorLine, Some particleIdLine when not (String.Equals(seriousErrorLine, particleIdLine, StringComparison.Ordinal)) ->
        Some $"{seriousErrorLine} | {particleIdLine}"
    | Some seriousErrorLine, _ -> Some seriousErrorLine
    | None, Some particleIdLine -> Some particleIdLine
    | None, None -> None

/// Classifies TOPAS log health using completion timing footer detection.
let classifyLogHealth (logText: string) : Result<unit, string> =
    if hasSuccessfulTopasTimingFooter logText then
        Ok()
    else
        Error(
            defaultArg
                (extractTopasFailureMessage logText)
                "TOPAS log does not contain successful completion timing footer."
        )

/// Reads one text file fully.
let private readAllText (path: string) : Result<string, string> =
    try
        Ok(File.ReadAllText(path))
    with ex ->
        Error ex.Message

/// Builds one collect file issue row.
let private collectFileIssue
    (row: CollectRunRow)
    (fileKind: string)
    (path: string)
    (problem: string)
    (message: string option)
    : CollectFileIssue =
    {
        RunId = row.RunId
        PhaseSpaceIndex = row.PhaseSpaceIndex
        NodeDigit = row.NodeDigit
        FileKind = fileKind
        Path = path
        Problem = problem
        Message = message
    }

/// Collects file issues for one generated run row.
let private collectRowFileIssues (scorers: CollectScorer list) (row: CollectRunRow) : CollectFileIssue list =
    let _, logPath = expectedCsvAndLogPaths row
    let csvIssues =
        scorers
        |> List.collect (fun scorer ->
            let csvPath = row.OutputFilePath + scorer.Suffix + ".csv"
            if not (File.Exists csvPath) then
                [ collectFileIssue row "Csv" csvPath "MissingCsv" None ]
            else
                let info = FileInfo(csvPath)

                if info.Length <= 0L then
                    [ collectFileIssue row "Csv" csvPath "EmptyCsv" (Some "CSV file is zero bytes.") ]
                else
                    match hasNumericTopasCsvRows csvPath with
                    | Error message -> [ collectFileIssue row "Csv" csvPath "CsvReadError" (Some message) ]
                    | Ok true -> []
                    | Ok false ->
                        [
                            collectFileIssue
                                row
                                "Csv"
                                csvPath
                                "NoNumericCsvRows"
                                (Some "CSV exists but contains no numeric TOPAS scorer rows.")
                        ])

    let logIssues =
        if not (File.Exists logPath) then
            [ collectFileIssue row "Log" logPath "MissingLog" None ]
        else
            match readAllText logPath with
            | Error message -> [ collectFileIssue row "Log" logPath "LogReadError" (Some message) ]
            | Ok logText ->
                match classifyLogHealth logText with
                | Ok() -> []
                | Error message -> [ collectFileIssue row "Log" logPath "IncompleteTopasLog" (Some message) ]

    csvIssues @ logIssues

/// Returns true when one issue belongs to a csv file.
let private isCsvIssue (issue: CollectFileIssue) : bool =
    String.Equals(issue.FileKind, "Csv", StringComparison.OrdinalIgnoreCase)

/// Returns true when one issue belongs to a log file.
let private isLogIssue (issue: CollectFileIssue) : bool =
    String.Equals(issue.FileKind, "Log", StringComparison.OrdinalIgnoreCase)

/// Applies requested phase-space and node exclusions to collect rows.
let applyCollectExclusions
    (rows: CollectRunRow list)
    (excludedPhaseSpaceIndexes: string list)
    (excludedNodeDigits: string list)
    : CollectRunRow list =
    let excludedPhspSet = excludedPhaseSpaceIndexes |> Set.ofList
    let excludedNodeSet = excludedNodeDigits |> Set.ofList

    rows
    |> List.filter (fun row ->
        not (excludedPhspSet.Contains row.PhaseSpaceIndex)
        && not (excludedNodeSet.Contains row.NodeDigit))

/// Builds one failed preflight check.
let private failedCheck (name: string) (message: string) : CollectPreflightCheck =
    {
        Name = name
        Ok = false
        Message = Some message
    }

/// Builds one passed preflight check.
let private passedCheck (name: string) : CollectPreflightCheck =
    {
        Name = name
        Ok = true
        Message = None
    }

/// Verifies that remaining collect rows have a balanced node set per phase-space index.
let private validateBalancedRows (rows: CollectRunRow list) : Result<unit, string> =
    if rows.IsEmpty then
        Error "No rows remain after exclusions."
    else
        let grouped =
            rows
            |> List.groupBy _.PhaseSpaceIndex
            |> List.map (fun (phaseSpaceIndex, phaseRows) ->
                phaseSpaceIndex, phaseRows |> List.map _.NodeDigit |> Set.ofList)

        let _, firstNodeSet = grouped.Head

        if grouped |> List.exists (fun (_, nodeSet) -> nodeSet <> firstNodeSet) then
            Error "Remaining collect set is unbalanced. Exclude a full phase-space or full node."
        else
            Ok()

/// Builds structured collect preflight result from generated rows, exclusions, and file checks.
let buildCollectPreflightResult
    (settings: TsebtSettings)
    (seedBase: string)
    (rows: CollectRunRow list)
    (excludedPhaseSpaceIndexes: string list)
    (excludedNodeDigits: string list)
    : CollectPreflightResult =
    let runFolder = runFolderPath settings seedBase
    let runFolderExists = Directory.Exists runFolder
    let hasGeneratedRuns = not rows.IsEmpty
    let effectiveRows = applyCollectExclusions rows excludedPhaseSpaceIndexes excludedNodeDigits
    let discovery = discoverCollectScorers rows effectiveRows
    let scorers = discovery |> Result.defaultValue []
    let fileIssues = rows |> List.collect (collectRowFileIssues scorers)
    let effectiveRunCount = effectiveRows.Length
    let effectivePhaseSpaceCount = effectiveRows |> List.map _.PhaseSpaceIndex |> List.distinct |> List.length
    let effectiveNodeCount = effectiveRows |> List.map _.NodeDigit |> List.distinct |> List.length

    let effectiveRowIds = effectiveRows |> List.map _.RunId |> Set.ofList

    let effectiveFileIssues =
        fileIssues |> List.filter (fun issue -> effectiveRowIds.Contains issue.RunId)

    let foundCsvCount = effectiveRunCount * scorers.Length - (effectiveFileIssues |> List.filter isCsvIssue |> List.length)
    let missingCsvCount = effectiveFileIssues |> List.filter isCsvIssue |> List.length
    let foundLogCount = effectiveRunCount - (effectiveFileIssues |> List.filter isLogIssue |> List.length)
    let missingLogCount = effectiveFileIssues |> List.filter isLogIssue |> List.length

    let balanceResult = validateBalancedRows effectiveRows
    let balanceOk = Result.isOk balanceResult

    let checks =
        [
            if hasGeneratedRuns then
                passedCheck "Generated runs found"
            else
                failedCheck "Generated runs found" $"No generated runs were found for seed base: {seedBase}"

            if runFolderExists then
                passedCheck "Run folder exists"
            else
                failedCheck "Run folder exists" $"Run folder does not exist: {runFolder}"

            if effectiveRunCount > 0 then
                passedCheck "Rows remain after exclusions"
            else
                failedCheck "Rows remain after exclusions" "No rows remain after exclusions."

            match discovery with
            | Ok _ -> passedCheck "Scorer CSV families discovered"
            | Error message -> failedCheck "Scorer CSV families discovered" message

            if missingCsvCount = 0 then
                passedCheck "Input CSV files exist and contain numeric scorer rows"
            else
                failedCheck
                    "Input CSV files exist and contain numeric scorer rows"
                    $"Detected {missingCsvCount} csv file issues."

            if missingLogCount = 0 then
                passedCheck "Log files contain successful TOPAS completion footer"
            else
                failedCheck
                    "Log files contain successful TOPAS completion footer"
                    $"Detected {missingLogCount} log file issues."

            match balanceResult with
            | Ok() -> passedCheck "Remaining rows are balanced"
            | Error message -> failedCheck "Remaining rows are balanced" message
        ]

    let missingFiles =
        effectiveFileIssues
        |> List.map (fun issue -> missingFile issue.FileKind issue.Path)

    {
        SeedBase = seedBase
        Scorers = scorers |> List.map _.Name
        CanCollect =
            Result.isOk discovery
            && hasGeneratedRuns
            && runFolderExists
            && effectiveRunCount > 0
            && missingCsvCount = 0
            && missingLogCount = 0
            && balanceOk
        ExpectedRunCount = rows.Length
        EffectiveRunCount = effectiveRunCount
        EffectivePhaseSpaceCount = effectivePhaseSpaceCount
        EffectiveNodeCount = effectiveNodeCount
        FoundCsvCount = foundCsvCount
        MissingCsvCount = missingCsvCount
        FoundLogCount = foundLogCount
        MissingLogCount = missingLogCount
        ExcludedPhaseSpaceIndexes = excludedPhaseSpaceIndexes
        ExcludedNodeDigits = excludedNodeDigits
        Checks = checks
        MissingFiles = missingFiles
        FileIssues = fileIssues
    }

