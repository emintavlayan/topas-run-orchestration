module Server.Tests.TestHelpers

open System
open System.IO
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Configuration
open Shared
open TsebtConfig

/// Asserts that a Result is Ok and returns the value.
let assertOk result =
    match result with
    | Ok value -> value
    | Error message -> failwith $"Expected Ok but got Error: {message}"

/// Asserts that a Result is Error and returns the message.
let assertError result =
    match result with
    | Ok _ -> failwith "Expected Error but got Ok"
    | Error message -> message

/// Deletes a temporary directory after clearing SQLite pools.
let cleanupTestDirectory (path: string) =
    if Directory.Exists path then
        SqliteConnection.ClearAllPools()
        GC.Collect()
        GC.WaitForPendingFinalizers()
        Directory.Delete(path, true)

/// Builds a minimal valid settings record for server tests.
let buildSettings (appRoot: string) : TsebtSettings =
    {
        AppRoot = appRoot
        Paths = {
            Templates = "templates"
            Inputs = "inputs"
            Runs = "runs"
            Outputs = "outputs"
            Database = Path.Combine("database", "app.db")
            Logs = "logs"
        }
        Placeholders = {
            PhaseSpaceFile = "__PHSP_FILE__"
            OutputFile = "__OUTPUT_FILE__"
            Seed = "__SEED__"
        }
        Seed = { CurrentBase = "1001" }
        Topas = { Executable = "topas" }
        Slurm = {
            Partition = "compute"
            CpusPerTask = 1
            Account = "fysiker"
        }
        Nodes = [
            { Name = "monte-carlo-01"; Digit = "1" }
            { Name = "monte-carlo-02"; Digit = "2" }
        ]
        PhaseSpaceFiles = [
            { Index = "01"; Value = "ps01.IAEAphsp" }
            { Index = "02"; Value = "ps02.IAEAphsp" }
        ]
    }

/// Builds a minimal valid IConfiguration for config loading tests.
let buildValidConfig () =
    let values =
        dict [
            "Tsebt:AppRoot", @"C:\app-root"
            "Tsebt:Paths:Templates", "templates"
            "Tsebt:Paths:Inputs", "inputs"
            "Tsebt:Paths:Runs", "runs"
            "Tsebt:Paths:Outputs", "outputs"
            "Tsebt:Paths:Database", "database\\app.db"
            "Tsebt:Paths:Logs", "logs"
            "Tsebt:Placeholders:PhaseSpaceFile", "__PHSP_FILE__"
            "Tsebt:Placeholders:OutputFile", "__OUTPUT_FILE__"
            "Tsebt:Placeholders:Seed", "__SEED__"
            "Tsebt:Seed:CurrentBase", "1001"
            "Tsebt:Nodes:0:Name", "node01"
            "Tsebt:Nodes:0:Digit", "1"
            "Tsebt:Nodes:1:Name", "node02"
            "Tsebt:Nodes:1:Digit", "2"
            "Tsebt:PhaseSpaceFiles:0:Index", "01"
            "Tsebt:PhaseSpaceFiles:0:Value", "ps01.IAEAphsp"
            "Tsebt:PhaseSpaceFiles:1:Index", "02"
            "Tsebt:PhaseSpaceFiles:1:Value", "ps02.IAEAphsp"
        ]

    ConfigurationBuilder().AddInMemoryCollection(values).Build() :> IConfiguration

/// Seeds a generated batch and generated runs in SQLite.
let seedGeneratedBatch
    (connection: SqliteConnection)
    (seedBase: string)
    (rows: (string * string * string * string * string) list)
    =
    use batchInsert = connection.CreateCommand()
    batchInsert.CommandText <- "INSERT INTO generated_batches(seed_base, created_at, run_status, collect_status) VALUES($seedBase, '2026-01-01T00:00:00Z', 'Generated', 'NotCollected');"
    batchInsert.Parameters.AddWithValue("$seedBase", seedBase) |> ignore
    batchInsert.ExecuteNonQuery() |> ignore

    rows
    |> List.iter (fun (runId, phaseIndex, nodeDigit, inputPath, outputBase) ->
        use runInsert = connection.CreateCommand()
        runInsert.CommandText <- "INSERT INTO generated_runs(batch_id, run_id, phase_space_index, phase_space_file, node_name, node_digit, seed, input_file_path, output_file_path, run_folder, status, created_at) VALUES((SELECT id FROM generated_batches WHERE seed_base = $seedBase), $runId, $phaseIndex, 'ps.IAEAphsp', $nodeName, $nodeDigit, $seed, $inputPath, $outputBase, $runFolder, 'Generated', '2026-01-01T00:00:00Z');"
        runInsert.Parameters.AddWithValue("$seedBase", seedBase) |> ignore
        runInsert.Parameters.AddWithValue("$runId", runId) |> ignore
        runInsert.Parameters.AddWithValue("$phaseIndex", phaseIndex) |> ignore
        runInsert.Parameters.AddWithValue("$nodeName", $"node{nodeDigit.PadLeft(2, '0')}") |> ignore
        runInsert.Parameters.AddWithValue("$nodeDigit", nodeDigit) |> ignore
        runInsert.Parameters.AddWithValue("$seed", $"1001{nodeDigit}") |> ignore
        runInsert.Parameters.AddWithValue("$inputPath", inputPath) |> ignore
        runInsert.Parameters.AddWithValue("$outputBase", outputBase) |> ignore
        runInsert.Parameters.AddWithValue("$runFolder", Path.GetDirectoryName(outputBase)) |> ignore
        runInsert.ExecuteNonQuery() |> ignore)

