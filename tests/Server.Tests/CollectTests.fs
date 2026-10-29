module Server.Tests.CollectTests

open System
open System.Globalization
open System.IO
open Microsoft.Data.Sqlite
open Xunit
open Server.Tests.TestHelpers
open CollectOperation
open CollectPlanning
open CollectPreflight
open CollectCsvMerge
open CollectStatistics
open SqliteInit
open Server

/// Returns a synthetic TOPAS timing footer used for successful log health tests.
let private successfulTopasFooter =
    String.concat
        "\n"
        [
            "Elapsed times:"
            "Parameter Reading : User=0.000000s Real=0.002115s Sys=0.000000s [Cpu=0.0%]"
            "    Initialization: User=0.030000s Real=0.040187s Sys=0.010000s [Cpu=99.5%]"
            "         Execution: User=4.130000s Real=1.277396s Sys=0.160000s [Cpu=335.8%]"
            "      Finalization: User=0.510000s Real=1.155505s Sys=0.620000s [Cpu=97.8%]"
            "             Total: User=4.67s Real=2.4752s Sys=0.79s"
        ]

/// Formats one floating-point test value using invariant culture.
let private formatTestFloat (value: float) : string =
    value.ToString("G17", CultureInfo.InvariantCulture)

/// Writes one raw TOPAS-style dose csv used by collect uncertainty tests.
let private writeRawDoseCsv (path: string) (rows: (string * string * string * float) list) : unit =
    let lines =
        [
            "# TOPAS Version..."
            "# DoseToMedium ( Gy ) : Sum"
        ]
        @
        (rows
         |> List.map (fun (xValue, yValue, zValue, doseValue) ->
             $"{xValue},{yValue},{zValue},{formatTestFloat doseValue}"))

    File.WriteAllLines(path, lines)

/// Writes one merged phase-space csv fixture with a dose_sum_Gy column.
let private writeMergedPhaseSpaceCsv (path: string) (rows: (string * string * string * float) list) : unit =
    let lines =
        [ "x,y,z,dose_sum_Gy" ]
        @
        (rows
         |> List.map (fun (xValue, yValue, zValue, doseValue) ->
             $"{xValue},{yValue},{zValue},{formatTestFloat doseValue}"))

    File.WriteAllLines(path, lines)

/// Converts one absolute path to an AppRoot-relative forward-slash display path for marker assertions.
let private toRelativeDisplayPath (appRoot: string) (absolutePath: string) : string =
    Path.GetRelativePath(appRoot, absolutePath).Replace('\\', '/')

[<Fact>]
let ``Collect preflight blocks missing csv and missing log`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-collect-preflight-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let inputFolder = Path.Combine(appRoot, "inputs", "1001")
        let runFolder = Path.Combine(appRoot, "runs", "1001")
        Directory.CreateDirectory(inputFolder) |> ignore
        Directory.CreateDirectory(runFolder) |> ignore

        let inputPath = Path.Combine(inputFolder, "seed10011_phsp01.txt")
        let outputBase = Path.Combine(runFolder, "seed10011_phsp01")
        File.WriteAllText(inputPath, "input")

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" [ ("seed10011_phsp01", "01", "1", inputPath, outputBase) ]

        let missingCsv = assertOk (preflightCollect settings "1001")
        Assert.False(missingCsv.CanCollect)

        File.WriteAllText(outputBase + ".csv", "x,y,z,dose\n0,0,0,1")
        let missingLog = assertOk (preflightCollect settings "1001")
        Assert.False(missingLog.CanCollect)
        Assert.Equal(1, missingLog.MissingLogCount)
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Collect preflight passes when csv and log exist`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-collect-preflight-ok-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let inputFolder = Path.Combine(appRoot, "inputs", "1001")
        let runFolder = Path.Combine(appRoot, "runs", "1001")
        Directory.CreateDirectory(inputFolder) |> ignore
        Directory.CreateDirectory(runFolder) |> ignore

        let inputPath = Path.Combine(inputFolder, "seed10011_phsp01.txt")
        let outputBase = Path.Combine(runFolder, "seed10011_phsp01")
        File.WriteAllText(inputPath, "input")
        File.WriteAllText(outputBase + ".csv", "x,y,z,dose\n0,0,0,1")
        File.WriteAllText(outputBase + ".log", successfulTopasFooter)

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" [ ("seed10011_phsp01", "01", "1", inputPath, outputBase) ]

        let preflight = assertOk (preflightCollect settings "1001")
        Assert.True(preflight.CanCollect)
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Collect preflight detects empty csv and fatal log`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-collect-preflight-fatal-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let inputFolder = Path.Combine(appRoot, "inputs", "1001")
        let runFolder = Path.Combine(appRoot, "runs", "1001")
        Directory.CreateDirectory(inputFolder) |> ignore
        Directory.CreateDirectory(runFolder) |> ignore

        let inputPath = Path.Combine(inputFolder, "seed10011_phsp01.txt")
        let outputBase = Path.Combine(runFolder, "seed10011_phsp01")
        File.WriteAllText(inputPath, "input")
        File.WriteAllText(outputBase + ".csv", "")
        File.WriteAllText(outputBase + ".log", "TOPAS is quitting due to a serious error")

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" [ ("seed10011_phsp01", "01", "1", inputPath, outputBase) ]

        let preflight = assertOk (preflightCollect settings "1001")
        Assert.False(preflight.CanCollect)
        Assert.True(preflight.FileIssues |> List.exists (fun issue -> issue.Problem = "EmptyCsv"))
        Assert.True(preflight.FileIssues |> List.exists (fun issue -> issue.Problem = "IncompleteTopasLog"))
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Collect preview supports excluding failed phase-space or node`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-collect-preview-exclude-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let inputFolder = Path.Combine(appRoot, "inputs", "1001")
        let runFolder = Path.Combine(appRoot, "runs", "1001")
        Directory.CreateDirectory(inputFolder) |> ignore
        Directory.CreateDirectory(runFolder) |> ignore

        let rows = [
            ("seed10011_phsp01", "01", "1", Path.Combine(inputFolder, "seed10011_phsp01.txt"), Path.Combine(runFolder, "seed10011_phsp01"))
            ("seed10012_phsp01", "01", "2", Path.Combine(inputFolder, "seed10012_phsp01.txt"), Path.Combine(runFolder, "seed10012_phsp01"))
            ("seed10013_phsp20", "20", "1", Path.Combine(inputFolder, "seed10013_phsp20.txt"), Path.Combine(runFolder, "seed10013_phsp20"))
            ("seed10014_phsp20", "20", "2", Path.Combine(inputFolder, "seed10014_phsp20.txt"), Path.Combine(runFolder, "seed10014_phsp20"))
        ]

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" rows

        for (_, _, _, inputPath, _) in rows do
            File.WriteAllText(inputPath, "input")

        File.WriteAllText(Path.Combine(runFolder, "seed10011_phsp01.csv"), "x,y,z,dose\n0,0,0,1")
        File.WriteAllText(Path.Combine(runFolder, "seed10011_phsp01.log"), successfulTopasFooter)
        File.WriteAllText(Path.Combine(runFolder, "seed10012_phsp01.csv"), "x,y,z,dose\n0,0,0,2")
        File.WriteAllText(Path.Combine(runFolder, "seed10012_phsp01.log"), successfulTopasFooter)
        File.WriteAllText(Path.Combine(runFolder, "seed10013_phsp20.csv"), "")
        File.WriteAllText(Path.Combine(runFolder, "seed10013_phsp20.log"), "does not support particle ID")
        File.WriteAllText(Path.Combine(runFolder, "seed10014_phsp20.csv"), "x,y,z,dose\n0,0,0,4")
        File.WriteAllText(Path.Combine(runFolder, "seed10014_phsp20.log"), successfulTopasFooter)

        let strictPreview =
            assertOk (
                previewCollect
                    settings
                    {
                        SeedBase = "1001"
                        ExcludedPhaseSpaceIndexes = []
                        ExcludedNodeDigits = []
                    }
            )
        Assert.False(strictPreview.Preflight.CanCollect)
        Assert.True(strictPreview.Preflight.FileIssues |> List.exists (fun issue -> issue.PhaseSpaceIndex = "20"))

        let excludedPhspPreview =
            assertOk (
                previewCollect
                    settings
                    {
                        SeedBase = "1001"
                        ExcludedPhaseSpaceIndexes = [ "20" ]
                        ExcludedNodeDigits = []
                    }
            )
        Assert.True(excludedPhspPreview.Preflight.CanCollect)
        Assert.Equal(2, excludedPhspPreview.Preflight.EffectiveRunCount)
        Assert.Equal(1, excludedPhspPreview.Preflight.EffectivePhaseSpaceCount)
        Assert.Equal(2, excludedPhspPreview.Preflight.EffectiveNodeCount)

        let excludedNodePreview =
            assertOk (
                previewCollect
                    settings
                    {
                        SeedBase = "1001"
                        ExcludedPhaseSpaceIndexes = []
                        ExcludedNodeDigits = [ "1" ]
                    }
            )
        Assert.True(excludedNodePreview.Preflight.CanCollect)
        Assert.Equal(2, excludedNodePreview.Preflight.EffectiveRunCount)
        Assert.Equal(2, excludedNodePreview.Preflight.EffectivePhaseSpaceCount)
        Assert.Equal(1, excludedNodePreview.Preflight.EffectiveNodeCount)
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Collect merge sums final numeric dose column`` () =
    let folder = Path.Combine(Path.GetTempPath(), $"xunit-merge-{Guid.NewGuid():N}")
    Directory.CreateDirectory(folder) |> ignore

    try
        let a = Path.Combine(folder, "node1.csv")
        let b = Path.Combine(folder, "node2.csv")
        let output = Path.Combine(folder, "phsp01_merged.csv")
        File.WriteAllText(a, "# TOPAS Version...\n# DoseToMedium ( Gy ) : Sum\n0,0,0,1\n0,1,0,2")
        File.WriteAllText(b, "# TOPAS Version...\n# DoseToMedium ( Gy ) : Sum\n0,0,0,3\n0,1,0,4")
        assertOk (mergeNodeCsvFilesForPhaseSpace [ a; b ] output) |> ignore
        let lines = File.ReadAllLines(output)
        Assert.Equal("x,y,z,dose_sum_Gy,dose_mean_node_Gy,dose_sd_node_Gy,dose_sem_node_Gy,dose_rel_sem_node_percent,node_count", lines[0])

        let row = lines[1].Split(',')
        Assert.Equal("0", row[0])
        Assert.Equal("0", row[1])
        Assert.Equal("0", row[2])
        Assert.Equal(4.0, Double.Parse(row[3], CultureInfo.InvariantCulture))
        Assert.Equal(2.0, Double.Parse(row[4], CultureInfo.InvariantCulture))
        Assert.Equal(1.4142135623730951, Double.Parse(row[5], CultureInfo.InvariantCulture), 12)
        Assert.Equal(1.0, Double.Parse(row[6], CultureInfo.InvariantCulture), 12)
        Assert.Equal(50.0, Double.Parse(row[7], CultureInfo.InvariantCulture), 12)
        Assert.Equal("2", row[8])
    finally
        cleanupTestDirectory folder

[<Fact>]
let ``Collect merge fails on mismatched shape`` () =
    let folder = Path.Combine(Path.GetTempPath(), $"xunit-merge-mismatch-{Guid.NewGuid():N}")
    Directory.CreateDirectory(folder) |> ignore

    try
        let a = Path.Combine(folder, "a.csv")
        let b = Path.Combine(folder, "b.csv")
        let output = Path.Combine(folder, "merged.csv")
        File.WriteAllText(a, "x,y,dose\n0,0,1\n0,1,2")
        File.WriteAllText(b, "x,y,dose\n0,0,3")
        Assert.True(Result.isError (mergeNodeCsvFilesForPhaseSpace [ a; b ] output))

        File.WriteAllText(b, "x,y,z,dose\n0,0,0,3\n0,1,0,4")
        Assert.True(Result.isError (mergeNodeCsvFilesForPhaseSpace [ a; b ] output))
    finally
        cleanupTestDirectory folder

[<Fact>]
let ``Collect final merge writes summed dose_merged csv`` () =
    let folder = Path.Combine(Path.GetTempPath(), $"xunit-final-merge-{Guid.NewGuid():N}")
    Directory.CreateDirectory(folder) |> ignore

    try
        let a = Path.Combine(folder, "phsp01_merged.csv")
        let b = Path.Combine(folder, "phsp02_merged.csv")
        let c = Path.Combine(folder, "phsp03_merged.csv")
        let summary = Path.Combine(folder, "dose_merged.csv")
        writeMergedPhaseSpaceCsv a [ ("0", "0", "0", 1.0); ("0", "1", "0", 2.0) ]
        writeMergedPhaseSpaceCsv b [ ("0", "0", "0", 3.0); ("0", "1", "0", 4.0) ]
        writeMergedPhaseSpaceCsv c [ ("0", "0", "0", 5.0); ("0", "1", "0", 6.0) ]
        assertOk (mergePhaseSpaceDoseCsvFiles [ a; b; c ] summary) |> ignore
        let lines = File.ReadAllLines(summary)
        Assert.Equal("x,y,z,dose_to_medium_Gy", lines[0])

        let row = lines[1].Split(',')
        Assert.Equal("0", row[0])
        Assert.Equal("0", row[1])
        Assert.Equal("0", row[2])
        Assert.Equal(9.0, Double.Parse(row[3], CultureInfo.InvariantCulture), 12)
    finally
        cleanupTestDirectory folder

[<Fact>]
let ``Collect uncertainty writes expected values for three raw batches`` () =
    let folder = Path.Combine(Path.GetTempPath(), $"xunit-uncertainty-{Guid.NewGuid():N}")
    Directory.CreateDirectory(folder) |> ignore

    try
        let a = Path.Combine(folder, "node1.csv")
        let b = Path.Combine(folder, "node2.csv")
        let c = Path.Combine(folder, "node3.csv")
        let output = Path.Combine(folder, "dose_with_uncertainty.csv")
        writeRawDoseCsv a [ ("0", "0", "0", 1.0) ]
        writeRawDoseCsv b [ ("0", "0", "0", 2.0) ]
        writeRawDoseCsv c [ ("0", "0", "0", 3.0) ]
        assertOk (computeDoseWithUncertaintyFromRawBatchCsvFiles [ a; b; c ] output) |> ignore
        let lines = File.ReadAllLines(output)
        Assert.Equal(
            "x,y,z,dose_to_medium_Gy,batch_count,mean_batch_dose_Gy,batch_standard_deviation_Gy,standard_uncertainty_Gy,relative_uncertainty_percent",
            lines[0]
        )

        let row = lines[1].Split(',')
        Assert.Equal("0", row[0])
        Assert.Equal("0", row[1])
        Assert.Equal("0", row[2])
        Assert.Equal(6.0, Double.Parse(row[3], CultureInfo.InvariantCulture), 12)
        Assert.Equal("3", row[4])
        Assert.Equal(2.0, Double.Parse(row[5], CultureInfo.InvariantCulture), 12)
        Assert.Equal(1.0, Double.Parse(row[6], CultureInfo.InvariantCulture), 12)
        Assert.Equal(1.7320508075688772, Double.Parse(row[7], CultureInfo.InvariantCulture), 12)
        Assert.Equal(28.86751345948129, Double.Parse(row[8], CultureInfo.InvariantCulture), 12)
    finally
        cleanupTestDirectory folder

[<Fact>]
let ``Collect uncertainty writes zero relative uncertainty for identical raw batches`` () =
    let folder = Path.Combine(Path.GetTempPath(), $"xunit-uncertainty-identical-{Guid.NewGuid():N}")
    Directory.CreateDirectory(folder) |> ignore

    try
        let a = Path.Combine(folder, "node1.csv")
        let b = Path.Combine(folder, "node2.csv")
        let c = Path.Combine(folder, "node3.csv")
        let output = Path.Combine(folder, "dose_with_uncertainty.csv")
        writeRawDoseCsv a [ ("0", "0", "0", 2.0) ]
        writeRawDoseCsv b [ ("0", "0", "0", 2.0) ]
        writeRawDoseCsv c [ ("0", "0", "0", 2.0) ]
        assertOk (computeDoseWithUncertaintyFromRawBatchCsvFiles [ a; b; c ] output) |> ignore
        let outputLines = File.ReadAllLines(output)
        let row = outputLines[1].Split(',')
        Assert.Equal(6.0, Double.Parse(row[3], CultureInfo.InvariantCulture), 12)
        Assert.Equal(0.0, Double.Parse(row[6], CultureInfo.InvariantCulture), 12)
        Assert.Equal(0.0, Double.Parse(row[7], CultureInfo.InvariantCulture), 12)
        Assert.Equal(0.0, Double.Parse(row[8], CultureInfo.InvariantCulture), 12)
    finally
        cleanupTestDirectory folder

/// Verifies summed-dose uncertainty fails clearly when fewer than two batches are available.
[<Fact>]
let ``Collect uncertainty metrics fail when fewer than two batches are provided`` () =
    let result = computeVoxelUncertaintyMetrics 2.0 4.0 1

    match result with
    | Ok _ -> Assert.True(false, "Expected uncertainty calculation to fail for m < 2.")
    | Error message -> Assert.Contains("At least two batch values", message)

/// Verifies tiny negative variance numerator is clamped to zero for floating-point roundoff.
[<Fact>]
let ``Collect uncertainty metrics clamp tiny negative variance numerator to zero`` () =
    let result = assertOk (computeVoxelUncertaintyMetrics 3.0 2.9999999999999996 3)

    Assert.Equal(3.0, result.DoseSum, 12)
    Assert.Equal(1.0, result.MeanBatchDose, 12)
    Assert.Equal(0.0, result.BatchStandardDeviation, 12)
    Assert.Equal(0.0, result.StandardUncertaintyOfSummedDose, 12)
    Assert.Equal(Some 0.0, result.RelativeUncertaintyPercent)

/// Verifies clearly invalid negative variance numerator fails instead of being silently clamped.
[<Fact>]
let ``Collect uncertainty metrics fail on negative variance beyond roundoff tolerance`` () =
    let result = computeVoxelUncertaintyMetrics 10.0 1.0 3

    match result with
    | Ok _ -> Assert.True(false, "Expected uncertainty calculation to fail for invalid negative variance.")
    | Error message -> Assert.Contains("negative beyond floating-point roundoff tolerance", message)

[<Fact>]
let ``Collect final merge fails on mismatched row counts`` () =
    let folder = Path.Combine(Path.GetTempPath(), $"xunit-final-merge-mismatch-{Guid.NewGuid():N}")
    Directory.CreateDirectory(folder) |> ignore

    try
        let a = Path.Combine(folder, "phsp01.csv")
        let b = Path.Combine(folder, "phsp02.csv")
        let summary = Path.Combine(folder, "dose_merged.csv")
        writeMergedPhaseSpaceCsv a [ ("0", "0", "0", 1.0); ("0", "1", "0", 2.0) ]
        writeMergedPhaseSpaceCsv b [ ("0", "0", "0", 3.0) ]
        Assert.True(Result.isError (mergePhaseSpaceDoseCsvFiles [ a; b ] summary))
    finally
        cleanupTestDirectory folder

[<Fact>]
let ``Collect uncertainty fails on raw csv row or coordinate mismatch`` () =
    let folder = Path.Combine(Path.GetTempPath(), $"xunit-uncertainty-mismatch-{Guid.NewGuid():N}")
    Directory.CreateDirectory(folder) |> ignore

    try
        let a = Path.Combine(folder, "node1.csv")
        let b = Path.Combine(folder, "node2.csv")
        let output = Path.Combine(folder, "dose_with_uncertainty.csv")
        writeRawDoseCsv a [ ("0", "0", "0", 1.0); ("0", "1", "0", 2.0) ]
        writeRawDoseCsv b [ ("0", "0", "0", 3.0) ]
        Assert.True(Result.isError (computeDoseWithUncertaintyFromRawBatchCsvFiles [ a; b ] output))

        writeRawDoseCsv b [ ("0", "0", "1", 3.0); ("0", "1", "0", 4.0) ]

        let mismatch =
            computeDoseWithUncertaintyFromRawBatchCsvFiles [ a; b ] output

        Assert.True(Result.isError mismatch)

        match mismatch with
        | Error message -> Assert.Contains("Coordinate mismatch", message)
        | Ok() -> Assert.True(false, "Expected coordinate mismatch error.")
    finally
        cleanupTestDirectory folder

[<Fact>]
let ``Collect operation writes outputs and updates status`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-collect-op-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let inputFolder = Path.Combine(appRoot, "inputs", "1001")
        let runFolder = Path.Combine(appRoot, "runs", "1001")
        Directory.CreateDirectory(inputFolder) |> ignore
        Directory.CreateDirectory(runFolder) |> ignore

        let rows = [
            ("seed10011_phsp01", "01", "1", Path.Combine(inputFolder, "seed10011_phsp01.txt"), Path.Combine(runFolder, "seed10011_phsp01"))
            ("seed10012_phsp01", "01", "2", Path.Combine(inputFolder, "seed10012_phsp01.txt"), Path.Combine(runFolder, "seed10012_phsp01"))
        ]

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" rows

        File.WriteAllText(Path.Combine(inputFolder, "seed10011_phsp01.txt"), "input")
        File.WriteAllText(Path.Combine(inputFolder, "seed10012_phsp01.txt"), "input")
        File.WriteAllText(Path.Combine(runFolder, "seed10011_phsp01.csv"), "# TOPAS Version...\n# DoseToMedium ( Gy ) : Sum\n0,0,0,1")
        File.WriteAllText(Path.Combine(runFolder, "seed10012_phsp01.csv"), "# TOPAS Version...\n# DoseToMedium ( Gy ) : Sum\n0,0,0,2")
        File.WriteAllText(Path.Combine(runFolder, "seed10011_phsp01.log"), successfulTopasFooter)
        File.WriteAllText(Path.Combine(runFolder, "seed10012_phsp01.log"), successfulTopasFooter)

        let result =
            assertOk (
                collectBatch
                    settings
                    {
                        SeedBase = "1001"
                        ExcludedPhaseSpaceIndexes = []
                        ExcludedNodeDigits = []
                    }
            )
        Assert.Equal("Collected", result.Status)
        let outputRootFolder = Path.Combine(appRoot, "outputs", "1001")
        let latestMarkerPath = Path.Combine(outputRootFolder, "latest_collection.txt")
        Assert.StartsWith(outputRootFolder, result.OutputFolder, StringComparison.OrdinalIgnoreCase)
        let mergedOverNodesFolder = Path.Combine(result.OutputFolder, "default", "merged-over-nodes")
        let mergedOverPhspFolder = Path.Combine(result.OutputFolder, "default", "merged-over-phsp")
        let mergedDosePath = Path.Combine(mergedOverPhspFolder, "dose_merged.csv")
        let uncertaintyPath = Path.Combine(result.OutputFolder, "default", "dose_with_uncertainty.csv")
        Assert.True(File.Exists(Path.Combine(result.OutputFolder, "collect_manifest.tsv")))
        Assert.True(File.Exists(Path.Combine(mergedOverNodesFolder, "phsp01_merged.csv")))
        Assert.True(File.Exists(mergedDosePath))
        Assert.True(File.Exists(uncertaintyPath))
        Assert.True(File.Exists(latestMarkerPath))
        Assert.Equal(toRelativeDisplayPath appRoot result.OutputFolder, File.ReadAllText(latestMarkerPath))
        Assert.Equal(mergedDosePath, result.SummaryPath)
        let mergedHeader = File.ReadLines(Path.Combine(mergedOverNodesFolder, "phsp01_merged.csv")) |> Seq.head
        let summaryHeader = File.ReadLines(mergedDosePath) |> Seq.head
        let uncertaintyLines = File.ReadAllLines(uncertaintyPath)
        let mergedDoseLines = File.ReadAllLines(mergedDosePath)
        let uncertaintyRow = uncertaintyLines[1].Split(',')
        let mergedDoseRow = mergedDoseLines[1].Split(',')
        Assert.Equal("x,y,z,dose_sum_Gy,dose_mean_node_Gy,dose_sd_node_Gy,dose_sem_node_Gy,dose_rel_sem_node_percent,node_count", mergedHeader)
        Assert.Equal("x,y,z,dose_to_medium_Gy", summaryHeader)
        Assert.Equal(
            Double.Parse(mergedDoseRow[3], CultureInfo.InvariantCulture),
            Double.Parse(uncertaintyRow[3], CultureInfo.InvariantCulture),
            12
        )

        use statusCommand = conn.CreateCommand()
        statusCommand.CommandText <- "SELECT collect_status FROM generated_batches WHERE seed_base = '1001';"
        let status = string (statusCommand.ExecuteScalar())
        Assert.Equal("Collected", status)
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Collect operation allows recollection and preserves prior timestamped outputs`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-collect-recollect-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let inputFolder = Path.Combine(appRoot, "inputs", "1043")
        let runFolder = Path.Combine(appRoot, "runs", "1043")
        Directory.CreateDirectory(inputFolder) |> ignore
        Directory.CreateDirectory(runFolder) |> ignore

        let rows = [
            ("seed10431_phsp01", "01", "1", Path.Combine(inputFolder, "seed10431_phsp01.txt"), Path.Combine(runFolder, "seed10431_phsp01"))
            ("seed10432_phsp01", "01", "2", Path.Combine(inputFolder, "seed10432_phsp01.txt"), Path.Combine(runFolder, "seed10432_phsp01"))
        ]

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1043" rows

        File.WriteAllText(Path.Combine(inputFolder, "seed10431_phsp01.txt"), "input")
        File.WriteAllText(Path.Combine(inputFolder, "seed10432_phsp01.txt"), "input")
        File.WriteAllText(Path.Combine(runFolder, "seed10431_phsp01.csv"), "# TOPAS Version...\n# DoseToMedium ( Gy ) : Sum\n0,0,0,1")
        File.WriteAllText(Path.Combine(runFolder, "seed10432_phsp01.csv"), "# TOPAS Version...\n# DoseToMedium ( Gy ) : Sum\n0,0,0,2")
        File.WriteAllText(Path.Combine(runFolder, "seed10431_phsp01.log"), successfulTopasFooter)
        File.WriteAllText(Path.Combine(runFolder, "seed10432_phsp01.log"), successfulTopasFooter)

        let firstResult =
            assertOk (
                collectBatch
                    settings
                    {
                        SeedBase = "1043"
                        ExcludedPhaseSpaceIndexes = []
                        ExcludedNodeDigits = []
                    }
            )

        let secondResult =
            assertOk (
                collectBatch
                    settings
                    {
                        SeedBase = "1043"
                        ExcludedPhaseSpaceIndexes = []
                        ExcludedNodeDigits = []
                    }
            )

        Assert.NotEqual<string>(firstResult.OutputFolder, secondResult.OutputFolder)
        Assert.True(Directory.Exists(firstResult.OutputFolder))
        Assert.True(Directory.Exists(secondResult.OutputFolder))
        Assert.True(File.Exists(Path.Combine(firstResult.OutputFolder, "default", "merged-over-phsp", "dose_merged.csv")))
        Assert.True(File.Exists(Path.Combine(secondResult.OutputFolder, "default", "merged-over-phsp", "dose_merged.csv")))

        let latestMarkerPath = Path.Combine(appRoot, "outputs", "1043", "latest_collection.txt")
        Assert.True(File.Exists(latestMarkerPath))
        Assert.Equal(toRelativeDisplayPath appRoot secondResult.OutputFolder, File.ReadAllText(latestMarkerPath))

        let previewAfterRecollect =
            assertOk (
                previewCollect
                    settings
                    {
                        SeedBase = "1043"
                        ExcludedPhaseSpaceIndexes = []
                        ExcludedNodeDigits = []
                    }
            )

        Assert.True(previewAfterRecollect.HasCollectedBefore)
        Assert.Equal(Some secondResult.OutputFolder, previewAfterRecollect.LatestCollectionFolder)
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Collect operation applies phase-space exclusions to merge and manifest`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-collect-op-exclude-phsp-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let inputFolder = Path.Combine(appRoot, "inputs", "1001")
        let runFolder = Path.Combine(appRoot, "runs", "1001")
        Directory.CreateDirectory(inputFolder) |> ignore
        Directory.CreateDirectory(runFolder) |> ignore

        let rows = [
            ("seed10011_phsp01", "01", "1", Path.Combine(inputFolder, "seed10011_phsp01.txt"), Path.Combine(runFolder, "seed10011_phsp01"))
            ("seed10012_phsp01", "01", "2", Path.Combine(inputFolder, "seed10012_phsp01.txt"), Path.Combine(runFolder, "seed10012_phsp01"))
            ("seed10013_phsp20", "20", "1", Path.Combine(inputFolder, "seed10013_phsp20.txt"), Path.Combine(runFolder, "seed10013_phsp20"))
            ("seed10014_phsp20", "20", "2", Path.Combine(inputFolder, "seed10014_phsp20.txt"), Path.Combine(runFolder, "seed10014_phsp20"))
        ]

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" rows

        for (_, _, _, inputPath, _) in rows do
            File.WriteAllText(inputPath, "input")

        File.WriteAllText(Path.Combine(runFolder, "seed10011_phsp01.csv"), "x,y,z,dose\n0,0,0,1")
        File.WriteAllText(Path.Combine(runFolder, "seed10011_phsp01.log"), successfulTopasFooter)
        File.WriteAllText(Path.Combine(runFolder, "seed10012_phsp01.csv"), "x,y,z,dose\n0,0,0,2")
        File.WriteAllText(Path.Combine(runFolder, "seed10012_phsp01.log"), successfulTopasFooter)
        File.WriteAllText(Path.Combine(runFolder, "seed10013_phsp20.csv"), "")
        File.WriteAllText(Path.Combine(runFolder, "seed10013_phsp20.log"), "does not support particle ID")
        File.WriteAllText(Path.Combine(runFolder, "seed10014_phsp20.csv"), "x,y,z,dose\n0,0,0,4")
        File.WriteAllText(Path.Combine(runFolder, "seed10014_phsp20.log"), successfulTopasFooter)

        let result =
            assertOk (
                collectBatch
                    settings
                    {
                        SeedBase = "1001"
                        ExcludedPhaseSpaceIndexes = [ "20" ]
                        ExcludedNodeDigits = []
                    }
            )

        Assert.Equal("Collected", result.Status)
        Assert.Equal(2, result.ExpectedRunCount)
        Assert.Equal(1, result.MergedPhaseSpaceCount)

        let outputFolder = result.Scorers.Head.OutputFolder
        Assert.True(File.Exists(Path.Combine(outputFolder, "merged-over-nodes", "phsp01_merged.csv")))
        Assert.False(File.Exists(Path.Combine(outputFolder, "merged-over-nodes", "phsp20_merged.csv")))
        Assert.True(File.Exists(Path.Combine(outputFolder, "merged-over-phsp", "dose_merged.csv")))
        Assert.True(File.Exists(Path.Combine(outputFolder, "dose_with_uncertainty.csv")))

        let manifestLines = File.ReadAllLines(Path.Combine(outputFolder, "collect_manifest.tsv"))
        Assert.Equal(3, manifestLines.Length)
        Assert.DoesNotContain(manifestLines, (fun line -> line.Contains("phsp20")))
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Collect preflight accepts warning-heavy log when TOPAS footer exists`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-collect-preflight-warnings-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let inputFolder = Path.Combine(appRoot, "inputs", "1001")
        let runFolder = Path.Combine(appRoot, "runs", "1001")
        Directory.CreateDirectory(inputFolder) |> ignore
        Directory.CreateDirectory(runFolder) |> ignore

        let inputPath = Path.Combine(inputFolder, "seed10011_phsp01.txt")
        let outputBase = Path.Combine(runFolder, "seed10011_phsp01")
        File.WriteAllText(inputPath, "input")
        File.WriteAllText(outputBase + ".csv", "# TOPAS Version...\n# DoseToMedium...\n0,0,0,1.23E-08")

        let warningHeavyLog =
            String.concat
                "\n"
                [
                    "G4Exception : Stuck track"
                    "ERROR: particle had unusual state"
                    "Exception-like warning text from Geant4"
                    successfulTopasFooter
                ]

        File.WriteAllText(outputBase + ".log", warningHeavyLog)

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" [ ("seed10011_phsp01", "01", "1", inputPath, outputBase) ]

        let preflight = assertOk (preflightCollect settings "1001")
        Assert.True(preflight.CanCollect)
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Collect preflight reports particle-id failure when TOPAS footer is missing`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-collect-preflight-particleid-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let inputFolder = Path.Combine(appRoot, "inputs", "1001")
        let runFolder = Path.Combine(appRoot, "runs", "1001")
        Directory.CreateDirectory(inputFolder) |> ignore
        Directory.CreateDirectory(runFolder) |> ignore

        let inputPath = Path.Combine(inputFolder, "seed10011_phsp20.txt")
        let outputBase = Path.Combine(runFolder, "seed10011_phsp20")
        File.WriteAllText(inputPath, "input")
        File.WriteAllText(outputBase + ".csv", "")

        File.WriteAllText(
            outputBase + ".log",
            String.concat
                "\n"
                [
                    "TOPAS is quitting due to a serious error in specification of particle source: Beam"
                    "\"limited\" format phase space does not support particle ID: 36"
                ]
        )

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" [ ("seed10011_phsp20", "20", "1", inputPath, outputBase) ]

        let preflight = assertOk (preflightCollect settings "1001")
        Assert.False(preflight.CanCollect)

        let logIssue =
            preflight.FileIssues
            |> List.find (fun issue -> issue.FileKind = "Log" && issue.Problem = "IncompleteTopasLog")

        Assert.Contains("does not support particle ID: 36", defaultArg logIssue.Message "")
    finally
        cleanupTestDirectory appRoot


/// Runs an integration check against a balanced two-node, two-phase-space multi-scorer batch.
let private withScorerBatch (suffixes: string list) action =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-multi-scorer-{Guid.NewGuid():N}")
    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore
        let folder = Path.Combine(appRoot, "runs", "1001")
        Directory.CreateDirectory folder |> ignore
        let rows : CollectPlanning.CollectRunRow list =
            [ for phase in [ "01"; "02" ] do
                for node in [ "1"; "2" ] do
                    let runId = $"seed1001{node}_phsp{phase}"
                    { RunId = runId; PhaseSpaceIndex = phase; NodeDigit = node; OutputFilePath = Path.Combine(folder, runId) } ]
        let dbPath = Path.Combine(appRoot, "database", "app.db")
        use connection = new SqliteConnection($"Data Source={dbPath}")
        connection.Open()
        seedGeneratedBatch connection "1001"
            (rows |> List.map (fun row -> row.RunId, row.PhaseSpaceIndex, row.NodeDigit, row.OutputFilePath + ".txt", row.OutputFilePath))
        rows |> List.iteri (fun batchIndex row ->
            File.WriteAllText(row.OutputFilePath + ".log", successfulTopasFooter)
            suffixes |> List.iteri (fun scorerIndex suffix ->
                // Different scorer grids must never be combined with each other.
                let data = [ for voxel in 0 .. scorerIndex -> "0", string voxel, "0", float ((batchIndex + 1) * (scorerIndex + 1)) ]
                writeRawDoseCsv (row.OutputFilePath + suffix + ".csv") data))
        action settings rows connection
    finally
        cleanupTestDirectory appRoot

/// Checks scorer separation, arbitrary suffixes, independent uncertainty, shared logs and one history entry.
[<Fact>]
let ``Collect discovers arbitrary scorers and merges each grid independently`` () =
    withScorerBatch [ "_pdd"; "_x"; "_future-dose.v2"; "" ] (fun settings rows connection ->
        let preview = assertOk (previewCollect settings { SeedBase = "1001"; ExcludedPhaseSpaceIndexes = []; ExcludedNodeDigits = [] })
        Assert.True(preview.Preflight.CanCollect)
        Assert.Equal(16, preview.Preflight.FoundCsvCount)
        Assert.Equal(4, preview.Preflight.FoundLogCount)
        Assert.Equal(4, preview.Scorers.Length)
        Assert.Equal(8, preview.PlannedMergedFiles.Length)
        let collected = assertOk (collectBatch settings { SeedBase = "1001"; ExcludedPhaseSpaceIndexes = []; ExcludedNodeDigits = [] })
        Assert.Equal(16, collected.CsvReadCount)
        Assert.Equal(4, collected.LogFoundCount)
        Assert.Equal(2, collected.MergedPhaseSpaceCount)
        Assert.Equal(8, collected.MergedFiles.Length)
        for (name, scale) in [ "pdd", 1; "x", 2; "future-dose.v2", 3; "default", 4 ] do
            let paths = collected.Scorers |> List.find (fun paths -> paths.Scorer = name)
            Assert.Equal(Path.Combine(collected.OutputFolder, name), paths.OutputFolder)
            let summary = File.ReadAllLines paths.SummaryPath
            let uncertainty = File.ReadAllLines paths.UncertaintyPath
            Assert.Equal(scale + 1, summary.Length)
            Assert.Equal(scale + 1, uncertainty.Length)
            for index in 1 .. scale do
                let dose = Double.Parse(summary[index].Split(',')[3], CultureInfo.InvariantCulture)
                let stats = uncertainty[index].Split(',')
                Assert.Equal(10.0 * float scale, dose, 10)
                Assert.Equal(dose, Double.Parse(stats[3], CultureInfo.InvariantCulture), 10)
                Assert.Equal("4", stats[4])
                Assert.Equal(25.8198889747, Double.Parse(stats[8], CultureInfo.InvariantCulture), 7)
            let manifest = File.ReadAllLines paths.ManifestPath
            Assert.Equal(5, manifest.Length)
            Assert.Contains(rows.Head.OutputFilePath + ".log", String.concat "\n" manifest)
        Assert.Equal(17, File.ReadAllLines(collected.ManifestPath).Length)
        use count = connection.CreateCommand()
        count.CommandText <- "SELECT COUNT(*) FROM generated_batch_collections;"
        Assert.Equal(1L, count.ExecuteScalar() :?> int64)
        let original = File.ReadAllText(rows.Head.OutputFilePath + "_pdd.csv")
        let recollected = assertOk (collectBatch settings { SeedBase = "1001"; ExcludedPhaseSpaceIndexes = []; ExcludedNodeDigits = [] })
        Assert.NotEqual<string>(collected.OutputFolder, recollected.OutputFolder)
        Assert.Equal(original, File.ReadAllText(rows.Head.OutputFilePath + "_pdd.csv"))
        for scorer in collected.Scorers do Assert.True(File.Exists scorer.SummaryPath))

/// Checks that every discovered scorer must be healthy for every retained run.
[<Theory>]
[<InlineData("missing", "MissingCsv")>]
[<InlineData("empty", "EmptyCsv")>]
[<InlineData("header", "NoNumericCsvRows")>]
let ``Collect blocks unhealthy files within any scorer family`` (failure: string) (problem: string) =
    withScorerBatch [ "_pdd"; "_custom" ] (fun settings rows connection ->
        let path = rows.Head.OutputFilePath + "_custom.csv"
        match failure with
        | "missing" -> File.Delete path
        | "empty" -> File.WriteAllText(path, "")
        | _ -> File.WriteAllText(path, "# header only")
        let preview = assertOk (previewCollect settings { SeedBase = "1001"; ExcludedPhaseSpaceIndexes = []; ExcludedNodeDigits = [] })
        Assert.False(preview.Preflight.CanCollect)
        Assert.Equal(7, preview.Preflight.FoundCsvCount)
        Assert.Equal(1, preview.Preflight.MissingCsvCount)
        Assert.Contains(preview.Preflight.FileIssues, fun issue -> issue.Path = path && issue.Problem = problem)
        assertError (collectBatch settings { SeedBase = "1001"; ExcludedPhaseSpaceIndexes = []; ExcludedNodeDigits = [] }) |> ignore
        Assert.False(File.Exists(CollectPlanning.latestCollectionMarkerPath settings "1001"))
        use status = connection.CreateCommand()
        status.CommandText <- "SELECT collect_status FROM generated_batches;"
        Assert.Equal("NotCollected", string (status.ExecuteScalar())))

/// Checks exclusions before determining the retained scorer set and expected CSV count.
[<Theory>]
[<InlineData(true)>]
[<InlineData(false)>]
let ``Collect applies exclusions consistently to every scorer`` (excludeNode: bool) =
    withScorerBatch [ "_pdd"; "_x" ] (fun settings rows _ ->
        let excluded = rows |> List.filter (fun row -> if excludeNode then row.NodeDigit = "2" else row.PhaseSpaceIndex = "02")
        for row in excluded do
            File.Delete(row.OutputFilePath + "_x.csv")
            // Scorers found only in excluded runs must not leak into the effective collection.
            writeRawDoseCsv (row.OutputFilePath + "_excluded-only.csv") [ "0", "0", "0", 99.0 ]
        let nodeExclusions = if excludeNode then [ "2" ] else []
        let phaseExclusions = if excludeNode then [] else [ "02" ]
        let preview = assertOk (previewCollect settings { SeedBase = "1001"; ExcludedPhaseSpaceIndexes = phaseExclusions; ExcludedNodeDigits = nodeExclusions })
        Assert.True(preview.Preflight.CanCollect)
        Assert.Equal(4, preview.Preflight.FoundCsvCount)
        Assert.Equal(2, preview.Preflight.FoundLogCount)
        Assert.Equal<string list>([ "pdd"; "x" ], preview.Preflight.Scorers)
        let collected = assertOk (collectBatch settings { SeedBase = "1001"; ExcludedPhaseSpaceIndexes = phaseExclusions; ExcludedNodeDigits = nodeExclusions })
        for paths in collected.Scorers do
            let manifest = File.ReadAllText paths.ManifestPath
            for row in excluded do Assert.DoesNotContain(row.RunId, manifest))

/// Checks that a failed later scorer cannot replace the latest successful collection or metadata.
[<Fact>]
let ``Failed scorer preserves latest successful collection`` () =
    withScorerBatch [ "_a"; "_z" ] (fun settings rows connection ->
        let request : Shared.CollectRequest = { SeedBase = "1001"; ExcludedPhaseSpaceIndexes = []; ExcludedNodeDigits = [] }
        let first = assertOk (collectBatch settings request)
        let marker = File.ReadAllText(CollectPlanning.latestCollectionMarkerPath settings "1001")
        writeRawDoseCsv (rows.Head.OutputFilePath + "_z.csv") [ "9", "0", "0", 1.0 ]
        let failure = assertError (collectBatch settings request)
        Assert.Contains("Scorer 'z'", failure)
        Assert.Equal(marker, File.ReadAllText(CollectPlanning.latestCollectionMarkerPath settings "1001"))
        let details = assertOk (getCollectBatchDetails settings "1001")
        Assert.Equal(Some first.OutputFolder, details.LatestCollectionFolder)
        Assert.Equal("Collected", details.CollectStatus)
        use history = connection.CreateCommand()
        history.CommandText <- "SELECT COUNT(*) FROM generated_batch_collections WHERE status = 'Failed';"
        Assert.Equal(1L, history.ExecuteScalar() :?> int64))

/// Checks longest-stem ownership and literal matching when run names overlap or contain wildcard characters.
[<Fact>]
let ``Scorer discovery assigns overlapping run stems to their longest owner`` () =
    withScorerBatch [] (fun settings rows _ ->
        let first = rows.Head
        let longer = { first with RunId = first.RunId + "0"; OutputFilePath = first.OutputFilePath + "0" }
        let special = { first with RunId = "run[1]"; OutputFilePath = Path.Combine(Path.GetDirectoryName first.OutputFilePath, "run[1]") }
        File.WriteAllText(longer.OutputFilePath + "_long.csv", "data")
        File.WriteAllText(special.OutputFilePath + "-anything.csv", "data")
        File.WriteAllText(Path.Combine(Path.GetDirectoryName first.OutputFilePath, "unrelated.csv"), "data")
        let allRows = special :: longer :: rows
        let selected = assertOk (CollectPlanning.discoverCollectScorers allRows [ first; special ])
        Assert.Equal<string list>([ "-anything" ], selected |> List.map _.Name)
        let all = assertOk (CollectPlanning.discoverCollectScorers allRows allRows)
        Assert.Equal(2, all.Length)
        Assert.Contains(all, fun scorer -> scorer.Suffix = "_long"))

/// Checks explicit rejection of unsafe or ambiguous output folder names before any collection writes.
[<Theory>]
[<InlineData("_..", "_x")>]
[<InlineData("_", "_x")>]
[<InlineData("", "_default")>]
[<InlineData("_x", "x")>]
[<InlineData("_X", "_x")>]
let ``Scorer discovery blocks folder traversal and naming collisions`` (first: string) (second: string) =
    withScorerBatch [ first; second ] (fun settings _ _ ->
        let preview = assertOk (previewCollect settings { SeedBase = "1001"; ExcludedPhaseSpaceIndexes = []; ExcludedNodeDigits = [] })
        Assert.False(preview.Preflight.CanCollect)
        Assert.Contains(preview.Preflight.Checks, fun check -> check.Name = "Scorer CSV families discovered" && not check.Ok)
        assertError (collectBatch settings { SeedBase = "1001"; ExcludedPhaseSpaceIndexes = []; ExcludedNodeDigits = [] }) |> ignore
        Assert.False(Directory.Exists(CollectPlanning.outputFolderPath settings "1001")))

/// Checks the current production CSV names without requiring any predefined scorer list.
[<Fact>]
let ``Scorer discovery recognizes seed11137 phsp21 production files`` () =
    withScorerBatch [] (fun _ rows _ ->
        let row =
            { rows.Head with
                RunId = "seed11137_phsp21"
                PhaseSpaceIndex = "21"
                NodeDigit = "7"
                OutputFilePath = Path.Combine(Path.GetDirectoryName rows.Head.OutputFilePath, "seed11137_phsp21") }
        for suffix in [ "_pdd"; "_xprofile"; "_yprofile" ] do
            writeRawDoseCsv (row.OutputFilePath + suffix + ".csv") [ "0", "0", "0", 1.0 ]
        let scorers = assertOk (discoverCollectScorers [ row ] [ row ])
        Assert.Equal<string list>([ "pdd"; "xprofile"; "yprofile" ], scorers |> List.map _.Name)
        for scorer in scorers do
            let csvPath = scorerRunRows scorer [ row ] |> List.head |> expectedCsvAndLogPaths |> fst
            Assert.Equal(row.OutputFilePath + "_" + scorer.Name + ".csv", csvPath))
