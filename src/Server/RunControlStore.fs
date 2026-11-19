module RunControlStore

open System
open System.IO
open Microsoft.Data.Sqlite
open TsebtConfig
open FsToolkit.ErrorHandling

/// Represents a generated task with its immutable identity and log path.
type PlannedTask = { RunId: string; NodeName: string; PhaseSpaceIndex: string; LogPath: string }

/// Represents an accepted per-node Slurm submission.
type NodeSubmission = { NodeName: string; JobId: string; ManifestPath: string }

/// Opens the configured orchestration database without creating another database silently.
let private connect settings =
    let path=Bootstrap.combineAppRoot settings.AppRoot settings.Paths.Database
    let builder=SqliteConnectionStringBuilder()
    builder.DataSource <- path
    builder.Mode <- SqliteOpenMode.ReadWrite
    let connection=new SqliteConnection(builder.ConnectionString)
    connection.Open()
    connection

/// Persists each accepted array immediately, including arrays accepted before a later submission fails.
let recordSubmission settings seedBase nodeName jobId manifestPath =
    try
        use connection=connect settings
        use command=connection.CreateCommand()
        command.CommandText <- "INSERT INTO run_node_submissions(seed_base,node_name,job_id,manifest_path,submitted_at) VALUES($batch,$node,$job,$manifest,$at);"
        for key,value in ["$batch",seedBase;"$node",nodeName;"$job",jobId;"$manifest",manifestPath;"$at",DateTimeOffset.UtcNow.ToString("O")] do
            command.Parameters.AddWithValue(key,value) |> ignore
        command.ExecuteNonQuery() |> ignore
        Ok ()
    with ex -> Error $"Cannot record accepted Slurm job {jobId}: {ex.Message}"

/// Reads accepted arrays for one batch without reconstructing node associations from list positions.
let submissions settings seedBase =
    try
        use connection=connect settings
        use command=connection.CreateCommand()
        command.CommandText <- "SELECT node_name,job_id,manifest_path FROM run_node_submissions WHERE seed_base=$batch ORDER BY id;"
        command.Parameters.AddWithValue("$batch",seedBase) |> ignore
        use reader=command.ExecuteReader()
        let rows=ResizeArray<NodeSubmission>()
        while reader.Read() do rows.Add {NodeName=reader.GetString(0);JobId=reader.GetString(1);ManifestPath=reader.GetString(2)}
        Ok(List.ofSeq rows)
    with ex -> Error $"Cannot read node submissions: {ex.Message}"

/// Groups accepted array IDs by batch, including partially submitted batches.
let batchJobIds settings =
    try
        use connection=connect settings
        use command=connection.CreateCommand()
        command.CommandText <- "SELECT seed_base,group_concat(job_id) FROM run_node_submissions GROUP BY seed_base;"
        use reader=command.ExecuteReader()
        let rows=ResizeArray<string * string>()
        while reader.Read() do rows.Add(reader.GetString(0),reader.GetString(1))
        Ok(Map.ofSeq rows)
    with ex -> Error $"Cannot read monitored submissions: {ex.Message}"

/// Reads generated task identities in the original manifest-planning order.
let plannedTasks settings seedBase =
    try
        use connection=connect settings
        use command=connection.CreateCommand()
        command.CommandText <- "SELECT r.run_id,r.node_name,r.phase_space_index,r.output_file_path FROM generated_runs r JOIN generated_batches b ON b.id=r.batch_id WHERE b.seed_base=$batch ORDER BY r.id;"
        command.Parameters.AddWithValue("$batch",seedBase) |> ignore
        use reader=command.ExecuteReader()
        let rows=ResizeArray<PlannedTask>()
        while reader.Read() do rows.Add {RunId=reader.GetString(0);NodeName=reader.GetString(1);PhaseSpaceIndex=reader.GetString(2);LogPath=reader.GetString(3)+".log"}
        Ok(List.ofSeq rows)
    with ex -> Error $"Cannot read monitored tasks: {ex.Message}"

/// Reads actual task numbers and node assignments from generated per-node manifests.
let manifestTasks (folder: string) =
    try
        let files=if Directory.Exists folder then Directory.GetFiles(folder,"run_manifest_*.tsv") else [||]
        let rows=files |> Array.collect(fun path ->
            let lines=File.ReadAllLines path
            if lines.Length=0 then [||]
            else
                let headers=lines[0].Split('\t')
                let task=Array.findIndex ((=) "task_id") headers
                let run=Array.findIndex ((=) "run_id") headers
                let node=Array.findIndex ((=) "node_name") headers
                lines |> Array.skip 1 |> Array.filter(String.IsNullOrWhiteSpace >> not) |> Array.map(fun line ->
                    let fields=line.Split('\t')
                    fields[run],(fields[node],Int32.Parse fields[task])))
        if rows |> Array.distinctBy fst |> Array.length <> rows.Length then Error "Duplicate run IDs in node manifests."
        else Ok(Map.ofArray rows)
    with ex -> Error $"Cannot read node manifests: {ex.Message}"
