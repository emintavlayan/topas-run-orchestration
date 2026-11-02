module RunControlDomain

open System
open System.Globalization
open System.Text.RegularExpressions
open Shared
open FsToolkit.ErrorHandling

/// Represents scheduler evidence for one array element.
type SchedulerTask = {
    ArrayId: string; TaskId: int; JobName: string; State: string; Reason: string option
    StartedAt: DateTimeOffset option; FinishedAt: DateTimeOffset option; ElapsedSeconds: float option; ExitCode: string option
}

/// Represents the available information in a bounded TOPAS log tail.
type LogEvidence = { Complete: bool; Failure: string option; WallSeconds: float option }

/// Represents counters used to measure node utilization between samples.
type NodeSample = { Total: float; Idle: float; MemoryTotal: float; MemoryAvailable: float }

/// Parses a finite non-negative invariant number.
let number (text: string) =
    match Double.TryParse(text.Trim(),NumberStyles.Float,CultureInfo.InvariantCulture) with
    | true,value when Double.IsFinite value && value>=0. -> Some value
    | _ -> None

/// Parses Slurm UTC timestamps while excluding pending and unknown sentinels.
let timestamp (text: string) =
    match DateTimeOffset.TryParse(text.Trim(),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal) with
    | true,value -> Some value
    | _ -> None

/// Parses Slurm wall-clock durations in seconds or day-hour notation.
let elapsed (text: string) =
    let m=Regex.Match(text.Trim(),@"^(?:(\d+)-)?(\d+):(\d+)(?::(\d+(?:\.\d+)?))?$")
    if not m.Success then number text
    else
        let days=if m.Groups[1].Success then Double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture) else 0.
        let a=Double.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture)
        let b=Double.Parse(m.Groups[3].Value,CultureInfo.InvariantCulture)
        if m.Groups[4].Success then Some(days*86400.+a*3600.+b*60.+Double.Parse(m.Groups[4].Value,CultureInfo.InvariantCulture))
        else Some(days*86400.+a*60.+b)

/// Parses one squeue array-element row with explicit pipe-delimited fields.
let parseQueue (line: string) =
    let fields=line.Split('|') |> Array.map _.Trim()
    if fields.Length<>8 || not(Regex.IsMatch(fields[0],@"^\d+$")) then None
    else
        match Int32.TryParse fields[1] with
        | true,taskId -> Some {ArrayId=fields[0];TaskId=taskId;JobName=fields[2];State=fields[3];
                              ElapsedSeconds=elapsed fields[4];StartedAt=timestamp fields[5];
                              Reason=(if fields[3]="PENDING" then Some(fields[6].Trim('(',')')) else None);ExitCode=None;FinishedAt=None}
        | _ -> None

/// Parses sacct array elements while excluding aggregate and job-step rows.
let parseAccounting (line: string) =
    let fields=line.Split('|') |> Array.map _.Trim()
    if fields.Length<>8 then None
    else
        let id=Regex.Match(fields[0],@"^(\d+)_(\d+)$")
        let states=fields[2].Split([|' ';'+'|],StringSplitOptions.RemoveEmptyEntries)
        match Int32.TryParse id.Groups[2].Value with
        | true,taskId when id.Success && states.Length>0 ->
            Some {ArrayId=id.Groups[1].Value;TaskId=taskId;JobName=fields[1];State=states[0]
                  Reason=None;ExitCode=Some fields[3];ElapsedSeconds=number fields[4];StartedAt=timestamp fields[5];FinishedAt=timestamp fields[6]}
        | _ -> None

/// Reads successful completion and real elapsed time without confusing CPU totals with wall time.
let logEvidence (text: string) =
    let complete=CollectPreflight.hasSuccessfulTopasTimingFooter text
    let failure=CollectPreflight.extractTopasFailureMessage text
    let total=Regex.Match(text,@"(?im)^\s*Total:\s*([^\r\n]+)")
    let real=Regex.Match(total.Value,@"(?i)\bReal\s*=\s*(\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)\s*s?")
    let wall=
        if not complete || not total.Success then None
        elif real.Success then number real.Groups[1].Value
        else
            let single=Regex.Match(total.Groups[1].Value,@"^\s*(\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)\s*s?\s*$")
            if single.Success then number single.Groups[1].Value else None

    {Complete=complete;Failure=failure;WallSeconds=wall |> Option.filter(fun value -> value>0.)}

/// Resolves task state with live scheduler records taking precedence over stale log content.
let taskState (scheduler: SchedulerTask option) (log: LogEvidence) submitted =
    match scheduler with
    | Some task ->
        match task.State with
        | "PENDING" | "REQUEUED" | "REQUEUE_HOLD" -> RunTaskState.Pending,task.Reason
        | "RUNNING" | "COMPLETING" | "CONFIGURING" | "STAGE_OUT" -> RunTaskState.Running,None
        | "SUSPENDED" | "STOPPED" -> RunTaskState.Suspended,Some task.State
        | "COMPLETED" when task.ExitCode=Some "0:0" && log.Complete && log.Failure.IsNone -> RunTaskState.Completed,None
        | "COMPLETED" -> RunTaskState.Failed,Some "Slurm completed without a successful TOPAS timing footer or zero exit status."
        | "FAILED" | "CANCELLED" | "TIMEOUT" | "OUT_OF_MEMORY" | "NODE_FAIL" | "PREEMPTED" | "BOOT_FAIL" | "DEADLINE" | "REVOKED" ->
            RunTaskState.Failed,Some(task.State+(task.ExitCode |> Option.map(fun code -> " · exit "+code) |> Option.defaultValue ""))
        | other -> RunTaskState.Unknown,Some other
    | None when log.Failure.IsSome -> RunTaskState.Failed,log.Failure
    | None when log.Complete -> RunTaskState.Completed,None
    | None when submitted -> RunTaskState.Unknown,Some "No current scheduler/accounting evidence or complete TOPAS log."
    | None -> RunTaskState.NotSubmitted,None

/// Parses whole-node Linux CPU and memory counters from procfs.
let nodeSample (text: string) =
    result {
        let lines=text.Split('\n')
        let! cpu=lines |> Array.tryFind(fun line -> line.StartsWith("cpu ")) |> Result.requireSome "CPU counters unavailable."
        let counters=cpu.Split([|' ';'\t'|],StringSplitOptions.RemoveEmptyEntries) |> Array.skip 1 |> Array.truncate 8 |> Array.choose number
        do! Result.requireTrue "CPU counters are incomplete." (counters.Length>=4)
        /// Reads one memory counter in the kernel's reported kB units.
        let memory key = lines |> Array.tryFind(fun line -> line.StartsWith(key+":")) |> Option.bind(fun line -> line.Split([|' ';'\t'|],StringSplitOptions.RemoveEmptyEntries) |> Array.tryItem 1 |> Option.bind number)
        let! total=memory "MemTotal" |> Result.requireSome "MemTotal unavailable."
        let! available=memory "MemAvailable" |> Result.requireSome "MemAvailable unavailable."
        do! Result.requireTrue "Memory counters are invalid." (total>0. && available<=total)
        return {Total=Array.sum counters;Idle=counters[3]+(if counters.Length>4 then counters[4] else 0.);MemoryTotal=total;MemoryAvailable=available}
    }

/// Calculates normalized whole-node CPU usage between two valid samples.
let cpuPercent previous current =
    previous |> Option.bind(fun old ->
        let total=current.Total-old.Total
        let idle=current.Idle-old.Idle
        if total<=0. || idle<0. then None else Some(max 0. (min 100. (100.*(total-idle)/total))))

/// Finds a robust duration estimate from successful tasks in this batch and node.
let median values =
    match values |> List.filter(fun value -> Double.IsFinite value && value>0.) |> List.sort with
    | [] -> None
    | sorted ->
        let n=sorted.Length
        Some(if n%2=1 then sorted[n/2] else (sorted[n/2-1]+sorted[n/2])/2.)

/// Estimates remaining work by scheduling queued tasks across the currently active task slots.
let estimate (tasks: RunControlTask list) =
    let unfinished=tasks |> List.filter(fun task -> task.State<>RunTaskState.Completed)
    let running=tasks |> List.filter(fun task -> task.State=RunTaskState.Running)
    let durations=tasks |> List.filter(fun task -> task.State=RunTaskState.Completed) |> List.choose _.ElapsedSeconds
    if unfinished.IsEmpty then Some 0.,"Finished"
    elif unfinished |> List.exists(fun task -> task.State=RunTaskState.Failed) then None,"Needs attention"
    elif unfinished |> List.exists(fun task -> task.State=RunTaskState.Unknown || task.State=RunTaskState.Suspended || task.State=RunTaskState.NotSubmitted) then None,"State unavailable or not running"
    elif running.IsEmpty then None,"Waiting for resources"
    else
        match median durations with
        | None -> None,"Estimating…"
        | Some typical when running |> List.exists(fun task -> task.ElapsedSeconds.IsNone) -> None,"Waiting for elapsed time"
        | Some typical when running |> List.exists(fun task -> task.ElapsedSeconds.Value>=typical) -> None,"Running task exceeds typical duration"
        | Some typical ->
            let slots=running |> List.map(fun task -> typical-task.ElapsedSeconds.Value) |> List.toArray
            let queued=unfinished.Length-running.Length
            for _ in 1..queued do
                let index=slots |> Array.mapi(fun i seconds -> i,seconds) |> Array.minBy snd |> fst
                slots[index] <- slots[index]+typical
            Some(Array.max slots),$"Based on {durations.Length} completed task(s); current concurrency assumed."
