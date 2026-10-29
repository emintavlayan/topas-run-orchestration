module CollectPlanning

open System
open System.IO
open FsToolkit.ErrorHandling
open Shared
open TsebtConfig

/// Represents one generated run row needed for collect preflight and planning.
type CollectRunRow = {
    RunId: string
    PhaseSpaceIndex: string
    NodeDigit: string
    OutputFilePath: string
}

/// Resolves the absolute run folder path for one seed base.
let runFolderPath (settings: TsebtSettings) (seedBase: string) : string =
    Path.Combine(settings.AppRoot, settings.Paths.Runs, seedBase)

/// Resolves the absolute collect output folder path for one seed base.
let outputFolderPath (settings: TsebtSettings) (seedBase: string) : string =
    Path.Combine(settings.AppRoot, settings.Paths.Outputs, seedBase)

/// Resolves one collect attempt output folder path under the seed-base output root.
let collectionOutputFolderPath (baseOutputFolder: string) (collectionFolderName: string) : string =
    Path.Combine(baseOutputFolder, collectionFolderName)

/// Resolves the absolute node-merged output folder path inside one collect attempt folder.
let mergedOutputFolderPathInCollection (collectionOutputFolder: string) : string =
    Path.Combine(collectionOutputFolder, "merged-over-nodes")

/// Resolves the absolute phase-space-merged output folder path inside one collect attempt folder.
let mergedOverPhaseSpaceOutputFolderPathInCollection (collectionOutputFolder: string) : string =
    Path.Combine(collectionOutputFolder, "merged-over-phsp")

/// Resolves the latest-collection marker file path for one seed base.
let latestCollectionMarkerPath (settings: TsebtSettings) (seedBase: string) : string =
    Path.Combine(outputFolderPath settings seedBase, "latest_collection.txt")

/// Builds expected csv and log file paths from one generated run row.
let expectedCsvAndLogPaths (row: CollectRunRow) : string * string =
    row.OutputFilePath + ".csv", row.OutputFilePath + ".log"

/// Represents one discovered scorer suffix and its output folder name.
type CollectScorer = { Suffix: string; Name: string }

/// Maps an arbitrary scorer suffix to a safe folder name without silently conflating suffixes.
let private scorerFromSuffix (suffix: string) : Result<CollectScorer, string> =
    let name =
        if suffix = "" then "default"
        elif suffix.StartsWith("_", StringComparison.Ordinal) then suffix.Substring(1)
        else suffix

    if String.IsNullOrWhiteSpace name || name = "." || name = ".."
       || name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal)
       || (name |> Seq.exists (fun c -> Char.IsControl c || "<>:\"/\\|?*".Contains c)) then
        Error $"Scorer suffix cannot be used as an output folder: '{suffix}'"
    else
        Ok { Suffix = suffix; Name = name }

/// Discovers scorer families using the longest matching generated stem to avoid neighbouring run IDs.
let discoverCollectScorers (allRows: CollectRunRow list) (effectiveRows: CollectRunRow list) : Result<CollectScorer list, string> =
    try
        let effectivePaths = effectiveRows |> List.map _.OutputFilePath |> Set.ofList
        let suffixes =
            allRows
            |> List.groupBy (fun row -> Path.GetDirectoryName(Path.GetFullPath row.OutputFilePath))
            |> List.collect (fun (folder, folderRows) ->
                let owners = folderRows |> List.sortByDescending (fun row -> Path.GetFileName(row.OutputFilePath).Length)
                if not (Directory.Exists folder) then []
                else
                    Directory.EnumerateFiles(folder)
                    |> Seq.filter (fun path -> String.Equals(Path.GetExtension path, ".csv", StringComparison.Ordinal))
                    |> Seq.choose (fun path ->
                        let stem = Path.GetFileNameWithoutExtension path
                        owners
                        |> List.tryFind (fun row -> stem.StartsWith(Path.GetFileName row.OutputFilePath, StringComparison.Ordinal))
                        |> Option.filter (fun row -> effectivePaths.Contains row.OutputFilePath)
                        |> Option.map (fun row -> stem.Substring(Path.GetFileName(row.OutputFilePath).Length)))
                    |> Seq.toList)
            |> List.distinct
            |> List.sort

        result {
            // Retain missing-file diagnostics when no CSV has been produced yet.
            let! scorers = (if suffixes.IsEmpty then [ "" ] else suffixes) |> List.traverseResultM scorerFromSuffix
            let collisions = scorers |> List.groupBy (fun scorer -> scorer.Name.ToUpperInvariant()) |> List.filter (fun (_, items) -> items.Length > 1)
            if not collisions.IsEmpty then
                return! Error "Different scorer suffixes resolve to the same output folder. Rename the conflicting suffixes before collection."
            return scorers
        }
    with ex ->
        Error $"Failed discovering scorer CSV files: {ex.Message}"

/// Appends one scorer suffix while retaining the original generated run identity.
let scorerRunRows (scorer: CollectScorer) (rows: CollectRunRow list) : CollectRunRow list =
    rows |> List.map (fun row -> { row with OutputFilePath = row.OutputFilePath + scorer.Suffix })

/// Resolves one scorer output folder within a collection attempt.
let scorerOutputFolderPath (collectionFolder: string) (scorer: CollectScorer) : string =
    Path.Combine(collectionFolder, scorer.Name)

/// Returns distinct phase-space indexes in stable ascending order.
let collectPhaseSpaceIndexes (rows: CollectRunRow list) : string list =
    rows
    |> List.map _.PhaseSpaceIndex
    |> List.distinct
    |> List.sort

/// Builds planned merged csv output paths within one collect attempt output folder.
let plannedMergedFilesInOutputFolder (collectionOutputFolder: string) (rows: CollectRunRow list) : string list =
    let outputFolder = mergedOutputFolderPathInCollection collectionOutputFolder

    rows
    |> collectPhaseSpaceIndexes
    |> List.map (fun phaseSpaceIndex -> Path.Combine(outputFolder, $"phsp{phaseSpaceIndex}_merged.csv"))

/// Builds planned merged csv output paths for one preview placeholder folder.
let plannedMergedFiles (settings: TsebtSettings) (seedBase: string) (rows: CollectRunRow list) : string list =
    plannedMergedFilesInOutputFolder (collectionOutputFolderPath (outputFolderPath settings seedBase) "<timestamp>") rows

/// Builds planned final merged dose output path within one collect attempt output folder.
let plannedSummaryPathInOutputFolder (collectionOutputFolder: string) : string =
    Path.Combine(mergedOverPhaseSpaceOutputFolderPathInCollection collectionOutputFolder, "dose_merged.csv")

/// Builds planned final merged dose output path for one preview placeholder folder.
let plannedSummaryPath (settings: TsebtSettings) (seedBase: string) : string =
    plannedSummaryPathInOutputFolder (collectionOutputFolderPath (outputFolderPath settings seedBase) "<timestamp>")

/// Builds planned dose uncertainty output path within one collect attempt output folder.
let plannedUncertaintyPathInOutputFolder (collectionOutputFolder: string) : string =
    Path.Combine(collectionOutputFolder, "dose_with_uncertainty.csv")

/// Builds planned dose uncertainty output path for one preview placeholder folder.
let plannedUncertaintyPath (settings: TsebtSettings) (seedBase: string) : string =
    plannedUncertaintyPathInOutputFolder (collectionOutputFolderPath (outputFolderPath settings seedBase) "<timestamp>")

/// Builds planned collect manifest output path within one collect attempt output folder.
let plannedManifestPathInOutputFolder (collectionOutputFolder: string) : string =
    Path.Combine(collectionOutputFolder, "collect_manifest.tsv")

/// Builds planned collect manifest output path for one preview placeholder folder.
let plannedManifestPath (settings: TsebtSettings) (seedBase: string) : string =
    plannedManifestPathInOutputFolder (collectionOutputFolderPath (outputFolderPath settings seedBase) "<timestamp>")

/// Builds a failed check with a message.
let failedCheck (name: string) (message: string) : CollectPreflightCheck =
    {
        Name = name
        Ok = false
        Message = Some message
    }

/// Builds a successful check with no message.
let passedCheck (name: string) : CollectPreflightCheck = { Name = name; Ok = true; Message = None }

