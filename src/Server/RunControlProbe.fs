module RunControlProbe

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Net
open System.Text.RegularExpressions
open System.Threading
open RunControlDomain

/// Represents a bounded read-only operating-system command.
type Command = { Executable: string; Arguments: string list }

/// Represents the latest whole-node measurements without substituting allocation for usage.
type NodeReading = {
    Cpu: float option; Memory: float option; UsedGb: float option; TotalGb: float option
    CapturedAt: string option; Error: string option
}

/// Runs a read-only probe with redirected output, no shell expansion and a five-second timeout.
let run command : Async<Result<string,string>> = async {
    try
        let info=ProcessStartInfo(command.Executable)
        info.UseShellExecute <- false
        info.RedirectStandardOutput <- true
        info.RedirectStandardError <- true
        info.CreateNoWindow <- true
        info.Environment["LC_ALL"] <- "C"
        info.Environment["TZ"] <- "UTC"
        info.Environment["SLURM_TIME_FORMAT"] <- "standard"
        for argument in command.Arguments do info.ArgumentList.Add argument
        use probeProcess=new Process(StartInfo=info)
        probeProcess.Start() |> ignore
        let stdout=probeProcess.StandardOutput.ReadToEndAsync()
        let stderr=probeProcess.StandardError.ReadToEndAsync()
        use timeout=new CancellationTokenSource(TimeSpan.FromSeconds 5.)
        try
            do! probeProcess.WaitForExitAsync(timeout.Token) |> Async.AwaitTask
            let! output=stdout |> Async.AwaitTask
            let! error=stderr |> Async.AwaitTask
            if probeProcess.ExitCode=0 then return Ok output
            else return Error $"{command.Executable}: {error.Trim() |> Seq.truncate 1000 |> String.Concat}"
        with :? OperationCanceledException ->
            try probeProcess.Kill(true) with _ -> ()
            return Error $"{command.Executable}: probe timed out after 5 seconds."
    with ex -> return Error $"{command.Executable}: {ex.Message}"
}

/// Builds the node-wide procfs command, using SSH only for configured remote host names.
let nodeCommand (nodeName:string) =
    if not(Regex.IsMatch(nodeName,@"^[A-Za-z0-9][A-Za-z0-9.-]*$")) then Error "Node name is not a valid probe host."
    elif String.Equals(nodeName.Split('.')[0],Dns.GetHostName().Split('.')[0],StringComparison.OrdinalIgnoreCase) then
        Ok {Executable="cat";Arguments=["/proc/stat";"/proc/meminfo"]}
    else Ok {Executable="ssh";Arguments=["-T";"-oBatchMode=yes";"-oStrictHostKeyChecking=yes";"-oConnectTimeout=3";nodeName;"cat /proc/stat /proc/meminfo"]}

/// Represents utilization history shared by batch snapshots.
let private samples=ConcurrentDictionary<string,DateTimeOffset * NodeSample * NodeReading>()

/// Returns an unavailable reading with a precise diagnostic.
let unavailable reason = {Cpu=None;Memory=None;UsedGb=None;TotalGb=None;CapturedAt=None;Error=Some reason}

/// Samples one node at most once per ten seconds and calculates CPU from counter differences.
let readNode runner (now:DateTimeOffset) cacheKey nodeName = async {
    match samples.TryGetValue cacheKey with
    | true,(at,_,reading) when (now-at).TotalSeconds<10. -> return reading
    | previous ->
        match nodeCommand nodeName with
        | Error reason -> return unavailable reason
        | Ok command ->
            let! result=runner command
            match result |> Result.bind nodeSample with
            | Error reason -> return unavailable reason
            | Ok sample ->
                let old=match previous with true,(_,value,_) -> Some value | _ -> None
                let reading={Cpu=cpuPercent old sample;Memory=Some(100.*(sample.MemoryTotal-sample.MemoryAvailable)/sample.MemoryTotal)
                             UsedGb=Some((sample.MemoryTotal-sample.MemoryAvailable)/1048576.);TotalGb=Some(sample.MemoryTotal/1048576.)
                             CapturedAt=Some(now.ToString("O"));Error=None}
                samples[cacheKey] <- now,sample,reading
                return reading
}
