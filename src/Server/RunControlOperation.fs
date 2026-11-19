module RunControlOperation

open System
open System.IO
open System.Collections.Generic
open System.Collections.Concurrent
open System.Text
open System.Text.RegularExpressions
open System.Threading
open Shared
open TsebtConfig
open RunControlDomain
open RunControlStore
open RunControlProbe
open FsToolkit.ErrorHandling

/// Reads only a bounded tail while allowing TOPAS to continue writing its log.
let readTail path =
    try
        use stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite ||| FileShare.Delete)
        let count=int(min stream.Length 65536L)
        stream.Seek(-int64 count,SeekOrigin.End) |> ignore
        let bytes=Array.zeroCreate<byte> count
        let mutable read=0
        let mutable ended=false
        while read<count && not ended do
            let n=stream.Read(bytes,read,count-read)
            if n=0 then ended <- true else read <- read+n
        Ok(Encoding.UTF8.GetString(bytes,0,read))
    with ex -> Error $"Cannot read log: {ex.Message}"

/// Represents cached log evidence keyed by file length and modification timestamp.
let private logs=ConcurrentDictionary<string,DateTime * int64 * LogEvidence>()

/// Reads completion evidence without rescanning unchanged completed logs every refresh.
let private readEvidence path =
    if not(File.Exists path) then {Complete=false;Failure=None;WallSeconds=None},false
    else
        try
            let info=FileInfo path
            match logs.TryGetValue path with
            | true,(modified,length,evidence) when modified=info.LastWriteTimeUtc && length=info.Length -> evidence,true
            | _ ->
                let evidence=readTail path |> Result.map logEvidence |> Result.defaultValue {Complete=false;Failure=None;WallSeconds=None}
                if logs.Count>4096 then logs.Clear()
                logs[path] <- info.LastWriteTimeUtc,info.Length,evidence
                evidence,true
        with _ -> {Complete=false;Failure=None;WallSeconds=None},false

/// Lists monitorable batches including arrays accepted before a partial submission failure.
let listBatches settings = result {
    let! batches=RunOperation.getRunBatches settings
    let! tracked=batchJobIds settings
    return batches |> List.map(fun batch ->
        match batch.SlurmJobId,Map.tryFind batch.SeedBase tracked with
        | None,Some ids -> {batch with SlurmJobId=Some ids;RunStatus="Partially submitted"}
        | _ -> batch)
}

/// Selects valid numeric job IDs without accepting scheduler flags from stored metadata.
let private jobIds values =
    values |> List.collect(fun (value:string) -> value.Split(',') |> Array.toList)
    |> List.filter(fun value -> Regex.IsMatch(value,@"^\d+$")) |> List.distinct

/// Builds an explicit squeue request scoped to this batch's known per-node job names.
let queueCommand seedBase nodes =
    let names=nodes |> List.map(fun node -> $"tsebt-{seedBase}-{node}") |> String.concat ","
    {Executable="squeue";Arguments=["--array";"--me";"--noheader";"--format=%F|%K|%j|%T|%M|%S|%R|%N";"--name="+names]}

/// Builds a bounded accounting query using exact submitted array IDs and expanded tasks.
let accountingCommand ids =
    {Executable="sacct";Arguments=["--noheader";"--parsable2";"--array";"--jobs="+String.concat "," ids;
                                  "--starttime=1970-01-01";"--format=JobID%64,JobName%200,State%32,ExitCode,ElapsedRaw,Start,End,NodeList%100"]}

/// Parses individual scheduler rows without treating a malformed response as an empty queue.
let private parseRows parser (text:string) =
    let lines=text.Split('\n') |> Array.filter(String.IsNullOrWhiteSpace >> not)
    let parsed=lines |> Array.choose parser |> Array.toList
    if lines.Length>0 && parsed.IsEmpty then Error "Scheduler returned no recognizable array task records."
    else Ok parsed

/// Formats an optional UTC task start for the browser contract.
let private iso (value:DateTimeOffset option) = value |> Option.map(fun at -> at.ToString("O"))

/// Resolves one manifest task against current queue, accounting and bounded log evidence.
let private resolveTask now seedBase batchIds manifests submissions scheduler (planned:PlannedTask) : RunControlTask =
    let identity=Map.tryFind planned.RunId manifests
    let node=identity |> Option.map fst |> Option.defaultValue planned.NodeName
    let taskId=identity |> Option.map snd
    let arrays=submissions |> List.filter(fun submission -> submission.NodeName=node) |> List.map _.JobId
    let observed=taskId |> Option.bind(fun task ->
        scheduler |> List.filter(fun record ->
            let matches=
                if not arrays.IsEmpty then List.contains record.ArrayId arrays
                else String.Equals(record.JobName,$"tsebt-{seedBase}-{node}",StringComparison.OrdinalIgnoreCase) && (List.isEmpty batchIds || List.contains record.ArrayId batchIds)
            record.TaskId=task && matches)
        |> List.sortByDescending(fun record -> record.ArrayId.Length,record.ArrayId) |> List.tryHead)
    let log,available=readEvidence planned.LogPath
    let state,reason=taskState observed log (not(List.isEmpty batchIds) || not arrays.IsEmpty || observed.IsSome)
    let finished=observed |> Option.bind _.FinishedAt |> Option.orElseWith(fun () ->
        if available && (state=RunTaskState.Completed || state=RunTaskState.Failed) then Some(DateTimeOffset(File.GetLastWriteTimeUtc planned.LogPath)) else None)
    let started=observed |> Option.bind _.StartedAt |> Option.orElseWith(fun () ->
        match finished,log.WallSeconds with Some at,Some duration when state=RunTaskState.Completed -> Some(at.AddSeconds(-duration)) | _ -> None)
    let seconds=
        if state=RunTaskState.Completed then (observed |> Option.bind _.ElapsedSeconds |> Option.filter(fun s -> s>0.)) |> Option.orElse log.WallSeconds
        else observed |> Option.bind(fun record ->
            record.ElapsedSeconds |> Option.orElseWith(fun () -> record.StartedAt |> Option.map(fun at -> max 0. (now-at).TotalSeconds)))
    {RunId=planned.RunId;PhaseSpaceIndex=planned.PhaseSpaceIndex;NodeName=node;TaskId=taskId
     SlurmTaskId=observed |> Option.map(fun record -> $"{record.ArrayId}_{record.TaskId}")
     State=state;Reason=reason;StartedAt=iso started;FinishedAt=iso finished;ElapsedSeconds=seconds;LogAvailable=available}

/// Collects a read-only snapshot using an injectable command runner for deterministic tests.
let buildSnapshotWith runner telemetryEnabled (now:DateTimeOffset) settings seedBase = async {
    let foundation=result {
        let! details=RunOperation.getRunBatchDetails settings seedBase
        let! planned=plannedTasks settings seedBase
        let! tracked=submissions settings seedBase
        return details,planned,tracked
    }
    match foundation with
    | Error error -> return Error error
    | Ok(details,planned,tracked) ->
        let warnings=ResizeArray<string>()
        let folder=Path.Combine(settings.AppRoot,settings.Paths.Runs,seedBase)
        let manifestResult=manifestTasks folder
        let manifests=match manifestResult with Ok rows -> rows | Error error -> warnings.Add error;Map.empty
        let names=(planned |> List.map(fun task -> Map.tryFind task.RunId manifests |> Option.map fst |> Option.defaultValue task.NodeName)) @ (settings.Nodes |> List.map _.Name) |> List.distinct
        let! telemetry=names |> List.map(fun name -> async {
            if not telemetryEnabled then return name,unavailable "Node telemetry disabled."
            else
                let! reading=readNode runner now (settings.AppRoot+"|"+name) name
                return name,reading }) |> Async.Parallel |> Async.StartChild
        let! queued=if planned.IsEmpty then async.Return(Ok "") else runner(queueCommand seedBase names)
        let queueResult=queued |> Result.bind(parseRows parseQueue)
        let queue=match queueResult with Ok values -> values | Error error -> warnings.Add("Live queue unavailable: "+error);[]
        let ids=jobIds ((details.SlurmJobId |> Option.toList) @ (tracked |> List.map _.JobId) @ (queue |> List.map _.ArrayId))
        let! accounted=if ids.IsEmpty then async.Return(Ok "") else runner(accountingCommand ids)
        let accountResult=accounted |> Result.bind(parseRows parseAccounting)
        let accounting=match accountResult with Ok values -> values | Error error -> warnings.Add("Accounting unavailable: "+error);[]
        let scheduler=(queue @ accounting) |> List.distinctBy(fun record -> record.ArrayId,record.TaskId)
        let submitted=details.SlurmJobId.IsSome || not tracked.IsEmpty || not queue.IsEmpty
        let missingIdentities=planned |> List.filter(fun task -> not(Map.containsKey task.RunId manifests)) |> List.length
        if submitted && missingIdentities>0 then warnings.Add $"Node manifests are missing identities for {missingIdentities} task(s); task identities are not guessed from queue order."
        let batchIds=jobIds (details.SlurmJobId |> Option.toList)
        let tasks=planned |> List.map(resolveTask now seedBase batchIds manifests tracked scheduler)
        let! readings=telemetry
        let measurements=Map.ofArray readings
        let nodes=names |> List.map(fun name ->
            let items=tasks |> List.filter(fun task -> task.NodeName=name)
            let reading=measurements[name]
            let completed=items |> List.filter(fun task -> task.State=RunTaskState.Completed) |> List.length
            let running=items |> List.filter(fun task -> task.State=RunTaskState.Running) |> List.length
            let failed=items |> List.filter(fun task -> task.State=RunTaskState.Failed) |> List.length
            let remaining,note=if items.IsEmpty then None,"Not part of this batch" else estimate items
            let starts=items |> List.choose _.StartedAt |> List.choose timestamp
            let ends=items |> List.choose _.FinishedAt |> List.choose timestamp
            let ending=if (items |> List.forall(fun task -> task.State=RunTaskState.Completed || task.State=RunTaskState.Failed)) && not ends.IsEmpty then List.max ends else now
            let elapsed=if starts.IsEmpty then None else Some(max 0. (ending-List.min starts).TotalSeconds)
            {NodeName=name;Tasks=items;CpuPercent=reading.Cpu;MemoryPercent=reading.Memory;MemoryUsedGb=reading.UsedGb;MemoryTotalGb=reading.TotalGb
             TelemetryAt=reading.CapturedAt;TelemetryError=reading.Error;CompletedCount=completed;RunningCount=running;FailedCount=failed
             ElapsedSeconds=elapsed;RemainingSeconds=remaining;EstimateNote=note})
        let participating=nodes |> List.filter(fun node -> not node.Tasks.IsEmpty)
        let estimates=participating |> List.choose _.RemainingSeconds
        let remaining=if participating.IsEmpty || estimates.Length<>participating.Length then None else Some(List.max estimates)
        return Ok {SeedBase=seedBase;CapturedAt=now.ToString("O");Nodes=nodes;TotalCount=tasks.Length
                   CompletedCount=nodes |> List.sumBy _.CompletedCount;RunningCount=nodes |> List.sumBy _.RunningCount
                   FailedCount=nodes |> List.sumBy _.FailedCount;RemainingSeconds=remaining
                   EstimatedFinishAt=remaining |> Option.map(fun seconds -> now.AddSeconds(seconds).ToString("O"))
                   Warnings=List.ofSeq warnings}
}

/// Serializes cache refreshes so browsers share a single ten-second snapshot per batch.
let private gate=new SemaphoreSlim(1,1)
let private snapshots=Dictionary<string,DateTimeOffset * RunControlSnapshot>()

/// Returns a shared snapshot without creating one Slurm query per browser or per node.
let getSnapshot settings telemetryEnabled seedBase = async {
    do! gate.WaitAsync() |> Async.AwaitTask
    try
        let now=DateTimeOffset.UtcNow
        let key=settings.AppRoot+"|"+seedBase+"|"+string telemetryEnabled
        match snapshots.TryGetValue key with
        | true,(at,snapshot) when (now-at).TotalSeconds<10. -> return Ok snapshot
        | _ ->
            let! result=buildSnapshotWith run telemetryEnabled now settings seedBase
            match result with
            | Ok snapshot ->
                if snapshots.Count>64 then snapshots.Clear()
                snapshots[key] <- DateTimeOffset.UtcNow,snapshot
            | Error _ -> ()
            return result
    finally gate.Release() |> ignore
}

/// Resolves a requested log through generated metadata rather than accepting arbitrary file paths.
let getLog settings (request:RunControlLogRequest) = result {
    let! planned=plannedTasks settings request.SeedBase
    let! task=planned |> List.tryFind(fun task -> task.RunId=request.RunId) |> Result.requireSome "Generated run not found in this batch."
    return! readTail task.LogPath
}
