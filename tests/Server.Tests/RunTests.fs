module Server.Tests.RunTests

open System
open System.IO
open Microsoft.Data.Sqlite
open Xunit
open Shared
open Server.Tests.TestHelpers
open RunOperation
open SqliteInit
open Server

/// Asserts that an error Result contains expected text.
let private assertErrorContains (expected: string) result =
    let message: string = assertError result
    Assert.Contains(expected, message)

[<Fact>]
let ``Run parsing and manifest formatting are correct`` () =
    Assert.Equal(Ok "12345", parseSlurmJobId "Submitted batch job 12345")
    Assert.True(Result.isError (parseSlurmJobId "bad output"))
    let row = {
        TaskId = 1
        NodeName = "monte-carlo-01"
        RunId = "seed10011_phsp01"
        InputFilePath = "/app/inputs/1001/seed10011_phsp01.txt"
        LogFilePath = "/app/runs/1001/seed10011_phsp01.log"
    }
    Assert.Equal("1\tmonte-carlo-01\tseed10011_phsp01\t/app/inputs/1001/seed10011_phsp01.txt\t/app/runs/1001/seed10011_phsp01.log", formatManifestRow row)

[<Fact>]
let ``Run script includes account nodelist chdir and topas wrapper without srun`` () =
    let settings = { buildSettings @"C:\app-root" with Topas = { Executable = "/home/fysiker/shellScripts/topas" } }
    let manifestPath = @"C:\app-root\runs\1001\run_manifest_monte-carlo-02.tsv"
    let script = buildSlurmScriptText settings "1004" "monte-carlo-02" manifestPath 4
    Assert.Contains("#SBATCH --account=fysiker", script)
    Assert.Contains("#SBATCH --nodelist=monte-carlo-02", script)
    let expectedRunFolder = Path.Combine(settings.AppRoot, settings.Paths.Runs, "1004")
    Assert.Contains($"#SBATCH --chdir={expectedRunFolder}", script)
    Assert.Contains("TOPAS=\"/home/fysiker/shellScripts/topas\"", script)
    Assert.Contains("--array=1-4", script)
    Assert.Contains("\"$TOPAS\" \"$INPUT_FILE\" > \"$LOG_FILE\" 2>&1", script)
    Assert.DoesNotContain("srun", script)

[<Fact>]
let ``Manifest preview rows preserve configured node names`` () =
    let settings = buildSettings @"C:\app-root"
    let row = {
        RunId = "seed10011_phsp01"
        InputFilePath = @"C:\app-root\inputs\1001\seed10011_phsp01.txt"
        OutputFilePath = @"C:\app-root\runs\1001\seed10011_phsp01"
        RunFolder = @"C:\app-root\runs\1001"
        Seed = "10011"
        NodeDigit = "1"
        PhaseSpaceIndex = "01"
    }

    let preview = toManifestPreviewRow settings "1001" 1 row
    Assert.Equal("monte-carlo-01", preview.NodeName)

[<Fact>]
let ``Per-node manifests are grouped by node with task ids starting at 1`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-run-plan-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let inputFolder = Path.Combine(appRoot, "inputs", "1001")
        let runFolder = Path.Combine(appRoot, "runs", "1001")
        Directory.CreateDirectory(inputFolder) |> ignore
        Directory.CreateDirectory(runFolder) |> ignore

        let mkRun runId nodeDigit =
            let inputPath = Path.Combine(inputFolder, $"{runId}.txt")
            let outputBase = Path.Combine(runFolder, runId)
            File.WriteAllText(inputPath, "input")
            (runId, "01", nodeDigit, inputPath, outputBase)

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" [
            mkRun "seed10011_phsp01" "1"
            mkRun "seed10012_phsp01" "1"
            mkRun "seed10021_phsp01" "2"
        ]

        let preview = assertOk (previewRun settings "1001")
        Assert.Equal(2, preview.NodeScriptPreviews.Length)
        let node1 = preview.NodeScriptPreviews |> List.find (fun p -> p.NodeName = "monte-carlo-01")
        let node2 = preview.NodeScriptPreviews |> List.find (fun p -> p.NodeName = "monte-carlo-02")
        Assert.Equal(2, node1.TaskCount)
        Assert.Equal(1, node2.TaskCount)
        Assert.Equal<int list>([ 1; 2 ], node1.ManifestRowsPreview |> List.map _.TaskId)
        Assert.Equal<int list>([ 1 ], node2.ManifestRowsPreview |> List.map _.TaskId)
        Assert.True(node1.ManifestRowsPreview |> List.forall (fun row -> row.NodeName = "monte-carlo-01"))
        Assert.True(node2.ManifestRowsPreview |> List.forall (fun row -> row.NodeName = "monte-carlo-02"))
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Run preflight blocks existing csv and log outputs`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-run-preflight-{Guid.NewGuid():N}")
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
        File.WriteAllText(outputBase + ".csv", "csv")
        File.WriteAllText(outputBase + ".log", "log")

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" [ ("seed10011_phsp01", "01", "1", inputPath, outputBase) ]

        let preflight = assertOk (preflightRun settings "1001")
        Assert.False(preflight.CanSubmit)
        Assert.True(preflight.Checks |> List.exists (fun c -> c.Name = "No output CSV collisions" && not c.Ok))
        Assert.True(preflight.Checks |> List.exists (fun c -> c.Name = "No log collisions" && not c.Ok))
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Run submit blocks previously submitted batch`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-run-submit-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        use insert = conn.CreateCommand()
        insert.CommandText <- "INSERT INTO generated_batches(seed_base, created_at, run_status, slurm_job_id) VALUES('1001', '2026-01-01T00:00:00Z', 'Submitted', '12345');"
        insert.ExecuteNonQuery() |> ignore

        assertErrorContains "already been submitted to Slurm as job 12345" (submitRun settings { SeedBase = "1001" })
    finally
        cleanupTestDirectory appRoot

