module RunControlViews

open System
open Feliz
open Shared
open SAFE

/// Formats wall-clock durations without implying second-level estimate precision.
let private duration seconds =
    match seconds with
    | None -> "—"
    | Some value when value <= 0. -> "0 min"
    | Some value when value < 60. -> "<1 min"
    | Some value when value < 3600. -> sprintf "%.0f min" (ceil(value/60.))
    | Some value -> sprintf "%.1f h" (value/3600.)

/// Formats an optional whole-node utilization measurement.
let private percent value = value |> Option.map (sprintf "%.0f%%") |> Option.defaultValue "—"

/// Formats a server timestamp in the browser's local timezone.
let private localTime (value:string) =
    match DateTime.TryParse value with true,at -> at.ToLocalTime().ToString("yyyy-MM-dd HH:mm") | _ -> value

/// Provides readable task states consistently across summaries and tables.
let private stateLabel state =
    match state with
    | RunTaskState.NotSubmitted -> "Not submitted"
    | RunTaskState.Pending -> "Waiting"
    | RunTaskState.Running -> "Running"
    | RunTaskState.Suspended -> "Suspended"
    | RunTaskState.Completed -> "Completed"
    | RunTaskState.Failed -> "Failed"
    | RunTaskState.Unknown -> "Unknown"

/// Renders an outlined action with an explicit enabled state.
let private button (label:string) disabled action =
    Html.button [prop.className "analysis-button";prop.disabled disabled;prop.text label;prop.onClick(fun _ -> action())]

/// Renders task identities in planned order; scheduler order is never assumed.
let private taskTable (tasks:RunControlTask list) showLog =
    Html.div [
        prop.className "overflow-x-auto"
        prop.children [
            Html.table [
                prop.className "table table-sm run-control-table"
                prop.children [
                    Html.thead [Html.tr [for label in ["PHSP / Run";"Slurm task";"State";"Elapsed";"Details";"Log"] -> Html.th label]]
                    Html.tbody [
                        for task in tasks do
                            Html.tr [
                                prop.key task.RunId
                                prop.children [
                                    Html.td [Html.div ("PHSP "+task.PhaseSpaceIndex);Html.div [prop.className "text-xs opacity-60";prop.text task.RunId]]
                                    Html.td (task.SlurmTaskId |> Option.defaultValue "—")
                                    Html.td [prop.className (if task.State=RunTaskState.Failed then "text-error" else "");prop.text(stateLabel task.State)]
                                    Html.td (duration task.ElapsedSeconds)
                                    Html.td [prop.className "run-control-reason";prop.text(task.Reason |> Option.defaultValue "—")]
                                    Html.td [button "View log" (not task.LogAvailable) (fun () -> showLog task)]
                                ]
                            ]
                    ]
                ]
            ]
        ]
    ]

/// Renders one labelled task section, hiding empty groups.
let private taskSection label tasks showLog =
    if List.isEmpty tasks then Html.none
    else Html.section [prop.className "run-control-group";prop.children [Html.h3 ($"{label} · {List.length tasks}");taskTable tasks showLog]]

/// Renders a node accordion with utilization, completion and wall-clock estimates.
let private nodeView (node:RunControlNode) showLog =
    let total=node.Tasks.Length
    let running=node.Tasks |> List.filter(fun task -> task.State=RunTaskState.Running)
    let current=
        match running with
        | [] when total=0 -> "Idle"
        | [] -> node.EstimateNote
        | [task] -> "PHSP "+task.PhaseSpaceIndex+" · "+(task.SlurmTaskId |> Option.defaultValue task.RunId)
        | tasks -> ($"{tasks.Length} tasks running · ")+(tasks |> List.choose _.SlurmTaskId |> String.concat ", ")
    let telemetry=node.TelemetryError |> Option.defaultWith(fun () ->
        let memory=match node.MemoryUsedGb,node.MemoryTotalGb with Some used,Some all -> sprintf "RAM %.1f / %.1f GiB. " used all | _ -> ""
        memory+(node.TelemetryAt |> Option.map(fun at -> "Sampled "+localTime at) |> Option.defaultValue "CPU needs two samples."))
    /// Renders a truthful count segment rather than guessing fractional TOPAS progress.
    let segment (className:string) count =
        Html.span [prop.className className;prop.style [style.width (length.percent (if total=0 then 0. else 100.*float count/float total))]]
    Html.details [prop.key node.NodeName;prop.className "run-control-node";prop.children [
        Html.summary [prop.className "run-control-row";prop.children [
            Html.div [prop.className "run-control-name";prop.children [Html.strong node.NodeName;Html.span current]]
            Html.span [prop.title telemetry;prop.text(percent node.CpuPercent)]
            Html.span [prop.title telemetry;prop.text(percent node.MemoryPercent)]
            Html.div [prop.children [
                Html.div [prop.className "run-control-progress";prop.custom("role","progressbar");prop.custom("aria-label",node.NodeName+" completed tasks");prop.custom("aria-valuemin",0);prop.custom("aria-valuemax",max 1 total);prop.custom("aria-valuenow",node.CompletedCount);
                          prop.children [segment "completed" node.CompletedCount;segment "failed" node.FailedCount;segment "running" node.RunningCount]]
                Html.div [prop.className "run-control-caption";prop.text(($"{node.CompletedCount} / {total} completed")+(if node.FailedCount>0 then $" · {node.FailedCount} failed" else ""))]]]
            Html.span (duration node.ElapsedSeconds)
            Html.div [prop.title node.EstimateNote;prop.children [Html.strong (duration node.RemainingSeconds);Html.div [prop.className "run-control-caption";prop.text node.EstimateNote]]]]]
        Html.div [prop.className "run-control-body";prop.children [
            if node.TelemetryError.IsSome then Html.p [prop.className "text-xs opacity-60";prop.text("CPU / RAM: "+telemetry)]
            taskSection "Running" running showLog
            taskSection "Waiting · planned order" (node.Tasks |> List.filter(fun task -> task.State=RunTaskState.Pending || task.State=RunTaskState.NotSubmitted)) showLog
            taskSection "Needs attention" (node.Tasks |> List.filter(fun task -> task.State=RunTaskState.Failed || task.State=RunTaskState.Unknown || task.State=RunTaskState.Suspended)) showLog
            let finished=node.Tasks |> List.filter(fun task -> task.State=RunTaskState.Completed)
            if not finished.IsEmpty then Html.details [prop.children [Html.summary [prop.className "cursor-pointer py-3 font-semibold";prop.text($"Completed · {finished.Length}")];taskTable finished showLog]]
            if total=0 then Html.p "No tasks assigned to this node in this batch."
            Html.p [prop.className "text-xs opacity-60 mt-3";prop.text "Slurm may start array tasks in any order. Remaining time assumes the current concurrency continues."]]]]]

/// Renders the read-only monitor with polling isolated from batch changes and unmounts.
[<ReactComponent>]
let View () =
    let api=React.useMemo((fun () -> Api.makeProxy<ITopasApi>()),[||])
    let batches,setBatches=React.useState<RunBatchSummary list>([])
    let selected,setSelected=React.useStateWithUpdater("")
    let snapshot,setSnapshot=React.useState<RunControlSnapshot option>(None)
    let batchError,setBatchError=React.useState("")
    let error,setError=React.useState("")
    let busy,setBusy=React.useState(false)
    let revision,setRevision=React.useStateWithUpdater(0)
    let log,setLog=React.useState<(string * string) option>(None)
    let logRequest=React.useRef(0)
    React.useEffect((fun () ->
        let mutable disposed=false
        let mutable inFlight=false
        /// Refreshes batch choices without resetting the selected batch on each poll.
        let refresh () =
            if not disposed && not inFlight then
                inFlight <- true
                async {
                    try
                        let! result=api.getRunControlBatches()
                        if not disposed then
                            match result with
                            | Error message -> setBatchError message
                            | Ok values ->
                                setBatches values
                                setBatchError ""
                                setSelected(fun current -> if current<>"" then current else values |> List.tryFind(fun batch -> batch.SlurmJobId.IsSome) |> Option.orElse(List.tryHead values) |> Option.map _.SeedBase |> Option.defaultValue "")
                    with ex -> if not disposed then setBatchError ex.Message
                    inFlight <- false
                } |> Async.StartImmediate
        refresh()
        let timer=Browser.Dom.window.setInterval(refresh,60000)
        {new IDisposable with member _.Dispose()=disposed <- true;Browser.Dom.window.clearInterval timer}),[||])
    React.useEffect((fun () ->
        let mutable disposed=false
        let mutable inFlight=false
        /// Polls a single batch and retains its last successful snapshot after transient failures.
        let refresh () =
            if selected<>"" && not disposed && not inFlight then
                inFlight <- true
                setBusy true
                async {
                    try
                        let! result=api.getRunControl selected
                        if not disposed then
                            match result with Ok value -> setSnapshot(Some value);setError "" | Error message -> setError message
                    with ex -> if not disposed then setError ex.Message
                    inFlight <- false
                    if not disposed then setBusy false
                } |> Async.StartImmediate
        refresh()
        let timer=Browser.Dom.window.setInterval(refresh,10000)
        {new IDisposable with member _.Dispose()=disposed <- true;Browser.Dom.window.clearInterval timer}),[|box selected;box revision|])
    React.useEffect((fun () -> logRequest.current <- logRequest.current+1;setLog None),[|box selected|])
    React.useEffectOnce(fun () -> {new IDisposable with member _.Dispose()=logRequest.current <- logRequest.current+1})
    /// Loads a bounded log tail while ignoring responses for closed or superseded panels.
    let showLog (task:RunControlTask) =
        logRequest.current <- logRequest.current+1
        let request=logRequest.current
        setLog(Some(task.RunId,"Loading…"))
        async {
            try
                let! result=api.getRunControlLog {SeedBase=selected;RunId=task.RunId}
                if request=logRequest.current then setLog(Some(task.RunId,match result with Ok text -> text | Error message -> message))
            with ex -> if request=logRequest.current then setLog(Some(task.RunId,ex.Message))
        } |> Async.StartImmediate
    let active=snapshot |> Option.filter(fun value -> value.SeedBase=selected)
    Html.section [
        prop.className "run-control"
        prop.children [
            Html.div [
                prop.className "run-control-toolbar"
                prop.children [
                    Html.div [Html.h2 [prop.className "text-lg font-semibold";prop.text "Run Control"];Html.p [prop.className "text-sm opacity-60";prop.text "Live Slurm tasks · refreshes every 10 seconds"]]
                    Html.label [
                        prop.className "analysis-field"
                        prop.children [
                            Html.span "Batch"
                            Html.select [
                                prop.className "select select-bordered select-sm"
                                prop.custom("aria-label","Run Control batch")
                                prop.value selected
                                prop.onChange(fun (value:string) -> setSelected(fun _ -> value))
                                prop.children [for batch in batches -> Html.option [prop.value batch.SeedBase;prop.text($"{batch.SeedBase} · {batch.RunStatus}")]]
                            ]
                        ]
                    ]
                    button (if busy then "Refreshing…" else "Refresh") (busy || selected="") (fun () -> setRevision((+) 1))
                ]
            ]
            if batchError<>"" then Html.p [prop.className "alert alert-warning";prop.text batchError]
            if error<>"" then Html.p [prop.className "alert alert-warning";prop.text("Refresh failed: "+error+". Last successful data remains below.")]
            match active with
            | None -> Html.p [prop.className "p-8 opacity-60";prop.text(if selected="" then "No generated batches available." else "Loading node progress…")]
            | Some value ->
                Html.div [
                    prop.className "run-control-totals"
                    prop.children [
                        Html.strong ($"{value.CompletedCount} / {value.TotalCount} completed")
                        Html.span ($"{value.RunningCount} running")
                        Html.span [prop.className (if value.FailedCount>0 then "text-error" else "");prop.text($"{value.FailedCount} failed")]
                        Html.span ("Remaining: "+duration value.RemainingSeconds)
                        if value.RemainingSeconds<>Some 0. then Html.span ("Estimated finish: "+(value.EstimatedFinishAt |> Option.map localTime |> Option.defaultValue "Estimating / unavailable"))
                        Html.span [prop.className "text-xs opacity-60";prop.text("Updated "+localTime value.CapturedAt)]
                    ]
                ]
                for warning in value.Warnings do Html.p [prop.className "run-control-warning";prop.text warning]
                Html.div [
                    prop.className "run-control-scroll"
                    prop.children [
                        Html.div [
                            prop.className "run-control-grid"
                            prop.children [
                                Html.div [prop.className "run-control-row run-control-head";prop.children [for label in ["Node / current task";"CPU";"RAM";"Task progress";"Elapsed";"Remaining (estimate)"] -> Html.span label]]
                                for node in value.Nodes do nodeView node showLog
                            ]
                        ]
                    ]
                ]
            match log with
            | None -> ()
            | Some(runId,text) ->
                Html.section [
                    prop.className "run-control-log"
                    prop.children [
                        Html.div [prop.className "flex items-center justify-between gap-3";prop.children [Html.h3 (runId+" · last 64 KiB of log");button "Close log" false (fun () -> logRequest.current <- logRequest.current+1;setLog None)]]
                        Html.pre [prop.text text]
                    ]
                ]
        ]
    ]
