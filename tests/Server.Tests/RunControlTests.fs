module Server.Tests.RunControlTests

open System
open System.IO
open Microsoft.Data.Sqlite
open Xunit
open Shared
open RunControlDomain
open RunControlOperation
open Server.Tests.TestHelpers

/// Creates a task fixture with the minimum monitoring identity.
let private task state seconds : RunControlTask =
    {RunId="run";PhaseSpaceIndex="01";NodeName="node01";TaskId=Some 1;SlurmTaskId=Some "90_1";State=state
     Reason=None;StartedAt=None;FinishedAt=None;ElapsedSeconds=seconds;LogAvailable=false}

/// Represents the real TOPAS timing footer, including CPU and wall-clock totals.
let private footer = "Elapsed times:\nParameter Reading : User=0s Real=1s Sys=0s\nInitialization: User=0s Real=1s Sys=0s\nExecution: User=400s Real=98s Sys=0s\nFinalization: User=0s Real=0s Sys=0s\nTotal: User=400s Real=100s Sys=2s"

[<Fact>]
let ``Queue and accounting preserve array identities and ignore steps`` () =
    let row=parseQueue "92|17|tsebt-1001-node01|RUNNING|1-02:03:04|2026-11-01T10:00:00|node01|node01" |> Option.get
    Assert.Equal("92",row.ArrayId)
    Assert.Equal(17,row.TaskId)
    Assert.Equal(Some 93784.,row.ElapsedSeconds)
    let waiting=parseQueue "92|3|tsebt-1001-node01|PENDING|0:00|N/A|(Resources)|" |> Option.get
    Assert.Equal(Some "Resources",waiting.Reason)
    Assert.Equal(None,waiting.StartedAt)
    let doneTask=parseAccounting "92_3|tsebt-1001-node01|CANCELLED by 10|0:15|12|2026-11-01T10:00:00|2026-11-01T10:00:12|node01" |> Option.get
    Assert.Equal("CANCELLED",doneTask.State)
    Assert.True(doneTask.FinishedAt.IsSome)
    for row in ["92.batch|x|COMPLETED|0:0|1|N/A|N/A|node01";"92|x|COMPLETED|0:0|1|N/A|N/A|node01";"92_1|x||0:0|1|N/A|N/A|node01";"bad"] do
        Assert.Equal(None,parseAccounting row)

[<Fact>]
let ``Completion requires healthy log and scheduler success and running beats stale footer`` () =
    let evidence=logEvidence footer
    Assert.True evidence.Complete
    Assert.Equal(Some 100.,evidence.WallSeconds)
    let running=parseQueue "90|1|x|RUNNING|0:10|N/A||node01"
    Assert.Equal(RunTaskState.Running,taskState running evidence true |> fst)
    let completed=parseAccounting "90_1|x|COMPLETED|0:0|100|N/A|N/A|node01"
    Assert.Equal(RunTaskState.Completed,taskState completed evidence true |> fst)
    Assert.Equal(RunTaskState.Failed,taskState completed (logEvidence "unfinished") true |> fst)
    let failed=parseAccounting "90_1|x|OUT_OF_MEMORY|0:9|100|N/A|N/A|node01"
    Assert.Equal(RunTaskState.Failed,taskState failed evidence true |> fst)
    Assert.Equal(RunTaskState.Unknown,taskState None (logEvidence "") true |> fst)
    Assert.Equal(RunTaskState.NotSubmitted,taskState None (logEvidence "") false |> fst)
    Assert.Equal(RunTaskState.Completed,taskState None evidence true |> fst)
    Assert.Equal(None,(logEvidence(footer.Replace("Real=100s", "CPU=100s"))).WallSeconds)
    Assert.Equal(RunTaskState.Failed,taskState completed (logEvidence(footer+"\nTOPAS is quitting due to a serious error")) true |> fst)

[<Fact>]
let ``ETA models concurrent slots and becomes unavailable for failures or overruns`` () =
    let doneTask=task RunTaskState.Completed (Some 100.)
    let active=task RunTaskState.Running (Some 20.)
    let waiting=task RunTaskState.Pending None
    Assert.Equal(Some 280.,estimate [doneTask;active;waiting;waiting] |> fst)
    Assert.Equal(Some 180.,estimate [doneTask;active;active;waiting;waiting] |> fst)
    Assert.Equal(Some 0.,estimate [doneTask] |> fst)
    for tasks in [[active;waiting];[doneTask;waiting];[doneTask;task RunTaskState.Running (Some 101.)];[doneTask;task RunTaskState.Failed None];[doneTask;task RunTaskState.Unknown None]] do
        Assert.Equal(None,estimate tasks |> fst)
    Assert.Equal(Some 3.,median [2.;4.;Double.NaN;-1.])

[<Fact>]
let ``CPU uses counter differences and excludes guest double counting`` () =
    let sample=nodeSample "cpu  100 0 30 500 20 0 0 10 90 0\nMemTotal: 1000 kB\nMemAvailable: 250 kB" |> assertOk
    Assert.Equal(660.,sample.Total)
    Assert.Equal(520.,sample.Idle)
    Assert.Equal(None,cpuPercent None sample)
    let next={sample with Total=sample.Total+100.;Idle=sample.Idle+20.}
    Assert.Equal(Some 80.,cpuPercent (Some sample) next)
    Assert.Equal(None,cpuPercent (Some next) sample)
    Assert.True(Result.isError(nodeSample "cpu 1 2\nMemTotal: 0 kB"))
    Assert.True(Result.isError(RunControlProbe.nodeCommand "-oProxyCommand=evil"))

/// Creates a database and real generated manifests for monitoring integration tests.
let private withBatch action =
    let root=Path.Combine(Path.GetTempPath(),"run-control-"+Guid.NewGuid().ToString("N"))
    let settings={buildSettings root with Nodes=[{Name="node01";Digit="1"};{Name="node02";Digit="2"}]}
    try
        Bootstrap.ensureRootFolders settings |> assertOk |> ignore
        SqliteInit.initialize settings |> assertOk |> ignore
        let folder=Path.Combine(root,"runs","1001")
        Directory.CreateDirectory folder |> ignore
        use connection=new SqliteConnection("Data Source="+Path.Combine(root,"database","app.db"))
        connection.Open()
        seedGeneratedBatch connection "1001" [for id,index,digit in ["a","01","1";"b","02","1";"c","03","1";"d","01","2"] -> id,index,digit,Path.Combine(root,id+".txt"),Path.Combine(folder,id)]
        File.WriteAllText(Path.Combine(folder,"run_manifest_node01.tsv"),"task_id\tnode_name\trun_id\tinput_file_path\tlog_file_path\n1\tnode01\ta\ta.txt\ta.log\n2\tnode01\tb\tb.txt\tb.log\n3\tnode01\tc\tc.txt\tc.log")
        File.WriteAllText(Path.Combine(folder,"run_manifest_node02.tsv"),"task_id\tnode_name\trun_id\tinput_file_path\tlog_file_path\n1\tnode02\td\td.txt\td.log")
        action settings folder
    finally cleanupTestDirectory root

[<Fact>]
let ``Partial submissions survive reload and exact task identity wins over scheduler row order`` () =
    withBatch(fun settings folder ->
        RunControlStore.recordSubmission settings "1001" "node01" "90" "manifest" |> assertOk |> ignore
        RunControlStore.recordSubmission settings "1001" "node02" "91" "manifest" |> assertOk |> ignore
        Assert.Equal("Partially submitted",(listBatches settings |> assertOk |> List.head).RunStatus)
        File.WriteAllText(Path.Combine(folder,"a.log"),footer)
        File.WriteAllText(Path.Combine(folder,"d.log"),footer)
        /// Supplies intentionally unordered and concurrent scheduler records.
        let runner (command:RunControlProbe.Command) = async {
            return Ok(if command.Executable="squeue" then "999|2|tsebt-1001-node01|RUNNING|0:01|N/A||node01\n90|3|tsebt-1001-node01|PENDING|0:00|N/A|Resources|\n90|2|tsebt-1001-node01|RUNNING|0:20|2026-11-01T10:01:40||node01" else "90_1|tsebt-1001-node01|COMPLETED|0:0|100|2026-11-01T10:00:00|2026-11-01T10:01:40|node01\n91_1|tsebt-1001-node02|COMPLETED|0:0|100|2026-11-01T10:00:00|2026-11-01T10:01:40|node02") }
        let snapshot=buildSnapshotWith runner false (DateTimeOffset.Parse "2026-11-01T10:02:00Z") settings "1001" |> Async.RunSynchronously |> assertOk
        Assert.Equal(2,snapshot.CompletedCount)
        Assert.Equal(Some 180.,snapshot.RemainingSeconds)
        let node=snapshot.Nodes |> List.find(fun node -> node.NodeName="node01")
        Assert.Equal<RunTaskState list>([RunTaskState.Completed;RunTaskState.Running;RunTaskState.Pending],node.Tasks |> List.map _.State)
        Assert.Equal(Some "90_2",node.Tasks[1].SlurmTaskId)
        let finished=snapshot.Nodes |> List.find(fun node -> node.NodeName="node02")
        Assert.Equal(Some 100.,finished.ElapsedSeconds)
        Assert.Equal(Some 0.,finished.RemainingSeconds)
        Assert.Equal(footer,getLog settings {SeedBase="1001";RunId="a"} |> assertOk)
        Assert.True(Result.isError(getLog settings {SeedBase="other";RunId="a"}))
        Assert.True(Result.isError(getLog settings {SeedBase="1001";RunId="../../etc/passwd"})))

[<Fact>]
let ``Scheduler outage preserves log recovery while incomplete submitted tasks stay unknown`` () =
    withBatch(fun settings folder ->
        RunControlStore.recordSubmission settings "1001" "node01" "90" "manifest" |> assertOk |> ignore
        File.WriteAllText(Path.Combine(folder,"a.log"),String('x',100000)+"\n"+footer)
        /// Simulates a host without usable Slurm commands.
        let runner (_:RunControlProbe.Command)=async.Return(Error "not installed")
        let snapshot=buildSnapshotWith runner false DateTimeOffset.UtcNow settings "1001" |> Async.RunSynchronously |> assertOk
        Assert.Equal(1,snapshot.CompletedCount)
        Assert.Equal(None,snapshot.RemainingSeconds)
        Assert.Equal(2,snapshot.Warnings.Length)
        let tasks=snapshot.Nodes |> List.collect _.Tasks
        Assert.Equal(RunTaskState.Unknown,(tasks |> List.find(fun task -> task.RunId="b")).State)
        let text=getLog settings {SeedBase="1001";RunId="a"} |> assertOk
        Assert.True(text.Length<=65536)
        Assert.EndsWith(footer,text))

[<Fact>]
let ``Partial acceptance does not invent submissions on other nodes and missing manifests do not guess task order`` () =
    withBatch(fun settings folder ->
        RunControlStore.recordSubmission settings "1001" "node01" "90" "manifest" |> assertOk |> ignore
        /// Supplies a known array with two concurrent tasks in reverse order.
        let runner (command:RunControlProbe.Command) = async.Return(Ok(if command.Executable="squeue" then "90|2|tsebt-1001-node01|RUNNING|0:20|N/A||node01\n90|1|tsebt-1001-node01|RUNNING|0:20|N/A||node01" else ""))
        let snapshot=buildSnapshotWith runner false DateTimeOffset.UtcNow settings "1001" |> Async.RunSynchronously |> assertOk
        Assert.Equal(2,snapshot.RunningCount)
        let other=snapshot.Nodes |> List.find(fun node -> node.NodeName="node02")
        Assert.Equal(RunTaskState.NotSubmitted,other.Tasks.Head.State)
        File.Delete(Path.Combine(folder,"run_manifest_node01.tsv"))
        let missing=buildSnapshotWith runner false DateTimeOffset.UtcNow settings "1001" |> Async.RunSynchronously |> assertOk
        Assert.NotEmpty(missing.Warnings)
        let affected=missing.Nodes |> List.find(fun node -> node.NodeName="node01")
        Assert.All(affected.Tasks,fun task -> Assert.Equal(None,task.TaskId);Assert.Equal(RunTaskState.Unknown,task.State)))

[<Fact>]
let ``Live overview groups current and upcoming runs by configured node with shared scheduler queries`` () =
    withBatch(fun settings folder ->
        RunControlStore.recordSubmission settings "1001" "node01" "90" "manifest" |> assertOk |> ignore
        let next=Path.Combine(settings.AppRoot,"runs","1002")
        Directory.CreateDirectory next |> ignore
        use connection=new SqliteConnection("Data Source="+Path.Combine(settings.AppRoot,"database","app.db"))
        connection.Open()
        seedGeneratedBatch connection "1002" ["next","01","1","input.txt",Path.Combine(next,"next")]
        RunControlStore.recordSubmission settings "1002" "node01" "91" "manifest" |> assertOk |> ignore
        File.WriteAllText(Path.Combine(next,"run_manifest_node01.tsv"),"task_id\tnode_name\trun_id\n1\tnode01\tnext")
        let calls=ResizeArray<string>()
        /// Returns current and upcoming arrays without requiring a run selector.
        let runner (command:RunControlProbe.Command) =
            calls.Add command.Executable
            async.Return(Ok(if command.Executable="squeue" then "90|2|tsebt-1001-node01|RUNNING|0:20|N/A||node01\n91|1|tsebt-1002-node01|PENDING|0:00|N/A|Resources|" else ""))
        let overview=buildOverviewWith runner false DateTimeOffset.UtcNow settings |> Async.RunSynchronously |> assertOk
        Assert.Equal(2,overview.Nodes.Length)
        Assert.Equal(2,overview.Runs.Length)
        Assert.Contains(overview.Activities,fun activity -> activity.SeedBase="1001" && activity.State=RunTaskState.Running)
        Assert.Contains(overview.Activities,fun activity -> activity.SeedBase="1002" && activity.State=RunTaskState.Pending)
        Assert.DoesNotContain(overview.Activities,fun activity -> activity.NodeName="node02")
        Assert.Equal<string list>(["squeue";"sacct"],List.ofSeq calls))

[<Fact>]
let ``Unrelated non-array jobs do not appear as application runs or trigger an outage`` () =
    withBatch(fun settings _ ->
        /// Supplies a normal unrelated interactive Slurm job.
        let runner (_:RunControlProbe.Command)=async.Return(Ok "55|N/A|interactive|RUNNING|1:00|N/A|node01|node01")
        let overview=buildOverviewWith runner false DateTimeOffset.UtcNow settings |> Async.RunSynchronously |> assertOk
        Assert.Empty(overview.Activities)
        Assert.Empty(overview.Runs)
        Assert.Empty(overview.Warnings))
