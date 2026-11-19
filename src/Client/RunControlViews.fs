module RunControlViews

open System
open Feliz
open Shared
open SAFE
open Fable.Core

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

/// Opens the native dialog once, preserving keyboard focus while its text refreshes.
[<Emit("if ($0 && !$0.open) $0.showModal()")>]
let private openDialog (element:obj) : unit = jsNative

/// Closes the native dialog and restores focus to the control that opened it.
[<Emit("if ($0 && $0.open) $0.close()")>]
let private closeDialog (element:obj) : unit = jsNative

/// Renders completed, running and failed task counts as a compact progress bar.
let private progress (node:RunControlNode) =
    let total=node.Tasks.Length
    /// Renders one count segment without estimating progress inside an individual task.
    let segment (name:string) count = Html.span [prop.className name;prop.style [style.width(length.percent(if total=0 then 0. else 100.*float count/float total))]]
    Html.div [
        Html.div [prop.className "run-control-progress";prop.custom("role","progressbar");prop.custom("aria-label","Completed tasks");prop.custom("aria-valuemin",0);prop.custom("aria-valuemax",max 1 total);prop.custom("aria-valuenow",node.CompletedCount);
                  prop.children [segment "completed" node.CompletedCount;segment "failed" node.FailedCount;segment "running" node.RunningCount]]
        Html.div [prop.className "run-control-caption";prop.text(sprintf "%d / %d completed%s" node.CompletedCount total (if node.FailedCount=0 then "" else sprintf " · %d failed" node.FailedCount))]
    ]

/// Reveals individual PHSP tasks only when the user requests the run's task details.
let private taskDetails seed (node:RunControlNode) showLog =
    Html.details [prop.className "run-control-task-details";prop.children [
        Html.summary [prop.className "analysis-button";prop.text(sprintf "Task details · %d task%s" node.Tasks.Length (if node.Tasks.Length=1 then "" else "s"))]
        Html.div [prop.className "run-control-task-body";prop.children [
            taskSection "Running" (node.Tasks |> List.filter(fun task -> task.State=RunTaskState.Running)) (showLog seed)
            taskSection "Waiting" (node.Tasks |> List.filter(fun task -> task.State=RunTaskState.Pending || task.State=RunTaskState.NotSubmitted)) (showLog seed)
            taskSection "Needs attention" (node.Tasks |> List.filter(fun task -> task.State=RunTaskState.Failed || task.State=RunTaskState.Unknown || task.State=RunTaskState.Suspended)) (showLog seed)
            taskSection "Completed" (node.Tasks |> List.filter(fun task -> task.State=RunTaskState.Completed)) (showLog seed)
        ]]
    ]]

/// Renders a run-level row inside a live node without exposing PHSP identifiers immediately.
let private runRow (activity:RunControlActivity) (snapshots:RunControlSnapshot list) showLog =
    let node=snapshots |> List.tryFind(fun snapshot -> snapshot.SeedBase=activity.SeedBase) |> Option.bind(fun snapshot -> snapshot.Nodes |> List.tryFind(fun node -> node.NodeName=activity.NodeName))
    Html.section [prop.key activity.SeedBase;prop.className "run-control-run";prop.children [
        Html.div [prop.className "run-control-run-heading";prop.children [
            Html.strong ("Run "+activity.SeedBase)
            Html.span (stateLabel activity.State)
            Html.span [prop.className "text-xs opacity-60";prop.text("Slurm "+String.concat ", " activity.ArrayIds)]
        ]]
        match node with
        | None -> Html.p [prop.className "text-sm opacity-60";prop.text "Run details unavailable."]
        | Some value ->
            progress value
            Html.p [prop.className "text-xs opacity-60 my-2";prop.text("Elapsed "+duration value.ElapsedSeconds+" · Remaining "+duration value.RemainingSeconds)]
            taskDetails activity.SeedBase value showLog
    ]]

/// Renders either live activity or the selected historical run on a configured node.
let private nodeView (reading:RunControlNode) (activities:RunControlActivity list) (snapshots:RunControlSnapshot list) selected queueUnavailable showLog =
    let runs=activities |> List.filter(fun activity -> activity.NodeName=reading.NodeName) |> List.sortBy(fun activity -> (if activity.State=RunTaskState.Running then 0 else 1),activity.ArrayIds |> List.tryHead |> Option.map(fun id -> id.Length,id) |> Option.defaultValue(0,""))
    let historical=snapshots |> List.tryFind(fun snapshot -> snapshot.SeedBase=selected) |> Option.bind(fun snapshot -> snapshot.Nodes |> List.tryFind(fun node -> node.NodeName=reading.NodeName))
    let current=if selected<>"" then historical else runs |> List.tryHead |> Option.bind(fun activity -> snapshots |> List.tryFind(fun snapshot -> snapshot.SeedBase=activity.SeedBase) |> Option.bind(fun snapshot -> snapshot.Nodes |> List.tryFind(fun node -> node.NodeName=reading.NodeName)))
    let caption=
        if selected<>"" then "Run "+selected+" · selected run"
        else match runs with
             | [] -> if queueUnavailable then "State unavailable" else "Idle"
             | first::_ -> "Run "+first.SeedBase+" · "+stateLabel first.State+" · Slurm "+String.concat ", " first.ArrayIds+(if runs.Length>1 then sprintf " · %d more run(s)" (runs.Length-1) else "")
    let telemetry=reading.TelemetryError |> Option.defaultValue (match reading.MemoryUsedGb,reading.MemoryTotalGb with Some used,Some total -> sprintf "RAM %.1f / %.1f GiB" used total | _ -> "CPU needs two samples.")
    Html.details [prop.key (selected+"|"+reading.NodeName);prop.className "run-control-node";prop.children [
        Html.summary [prop.className "run-control-row";prop.children [
            Html.div [prop.className "run-control-name";prop.children [Html.strong reading.NodeName;Html.span caption]]
            Html.span [prop.title telemetry;prop.text(percent reading.CpuPercent)]
            Html.span [prop.title telemetry;prop.text(percent reading.MemoryPercent)]
            match current with
            | None -> Html.span "—";Html.span "—";Html.span "—"
            | Some value -> progress value;Html.span(duration value.ElapsedSeconds);Html.span [prop.title value.EstimateNote;prop.text(duration value.RemainingSeconds)]
        ]]
        Html.div [prop.className "run-control-body";prop.children [
            if selected<>"" then
                match historical with Some value -> taskDetails selected value showLog | None -> Html.p "No tasks for this node."
            elif runs.IsEmpty then Html.p [prop.className "text-sm opacity-60";prop.text "No active or queued runs. Select a run above to inspect history."]
            else
                for activity in runs do runRow activity snapshots showLog
                Html.p [prop.className "text-xs opacity-60 mt-3";prop.text "Queue order follows submitted array IDs. Slurm may start work in a different order."]
            if reading.TelemetryError.IsSome then Html.p [prop.className "text-xs opacity-60 mt-3";prop.text("CPU / RAM: "+telemetry)]
        ]]
    ]]

/// Renders the live node overview with optional run history and a native modal log viewer.
[<ReactComponent>]
let View () =
    let api=React.useMemo((fun () -> Api.makeProxy<ITopasApi>()),[||])
    let batches,setBatches=React.useState<RunBatchSummary list>([])
    let selected,setSelected=React.useState("")
    let overview,setOverview=React.useState<RunControlOverview option>(None)
    let snapshot,setSnapshot=React.useState<RunControlSnapshot option>(None)
    let error,setError=React.useState("")
    let busy,setBusy=React.useState(false)
    let revision,setRevision=React.useStateWithUpdater(0)
    let log,setLog=React.useState<(string * string) option>(None)
    let requestId=React.useRef(0)
    React.useEffect((fun () ->
        let mutable disposed=false
        /// Refreshes history choices without changing the selected view.
        let refresh () =
          async {
            try
                let! result=api.getRunControlBatches()
                if not disposed then match result with Ok values -> setBatches values | Error message -> setError message
            with ex -> if not disposed then setError ex.Message
          } |> Async.StartImmediate
        refresh()
        let timer=Browser.Dom.window.setInterval(refresh,60000)
        {new IDisposable with member _.Dispose()=disposed <- true;Browser.Dom.window.clearInterval timer}),[||])
    React.useEffect((fun () ->
        let mutable disposed=false
        let mutable inFlight=false
        /// Polls the selected view and ignores results from a previous selection or unmounted page.
        let refresh () =
            if not disposed && not inFlight then
                inFlight <- true
                setBusy true
                async {
                    try
                        if selected="" then
                            let! result=api.getRunControlOverview()
                            if not disposed then match result with Ok value -> setOverview(Some value);setError "" | Error message -> setError message
                        else
                            let! result=api.getRunControl selected
                            if not disposed then match result with Ok value -> setSnapshot(Some value);setError "" | Error message -> setError message
                    with ex -> if not disposed then setError ex.Message
                    inFlight <- false
                    if not disposed then setBusy false
                  } |> Async.StartImmediate
        refresh()
        let timer=Browser.Dom.window.setInterval(refresh,10000)
        {new IDisposable with member _.Dispose()=disposed <- true;Browser.Dom.window.clearInterval timer}),[|box selected;box revision|])
    /// Closes the log, invalidates pending responses and restores the opener's keyboard focus.
    let dismissLog () =
        requestId.current <- requestId.current+1
        closeDialog(Browser.Dom.document.getElementById "run-control-log-dialog")
        setLog None
    React.useEffect((fun () -> if log.IsSome then openDialog(Browser.Dom.document.getElementById "run-control-log-dialog")),[|box log|])
    React.useEffect((fun () -> dismissLog()),[|box selected|])
    React.useEffectOnce(fun () -> {new IDisposable with member _.Dispose()=requestId.current <- requestId.current+1})
    /// Opens the log immediately in a modal and prevents late responses from reopening a closed log.
    let showLog seed (task:RunControlTask) =
        requestId.current <- requestId.current+1
        let request=requestId.current
        setLog(Some(task.RunId,"Loading…"))
        async {
            try
                let! result=api.getRunControlLog {SeedBase=seed;RunId=task.RunId}
                if request=requestId.current then setLog(Some(task.RunId,match result with Ok text -> text | Error message -> message))
            with ex -> if request=requestId.current then setLog(Some(task.RunId,ex.Message))
        } |> Async.StartImmediate
    let historical=snapshot |> Option.filter(fun value -> value.SeedBase=selected)
    let nodes,runs,activities,updated,warnings=
        if selected="" then
            match overview with Some value -> value.Nodes,value.Runs,value.Activities,value.CapturedAt,value.Warnings | None -> [],[],[],"",[]
        else
            match historical with Some value -> (value.Nodes |> List.filter(fun node -> not node.Tasks.IsEmpty)),[value],[],value.CapturedAt,value.Warnings | None -> [],[],[],"",[]
    Html.section [prop.className "run-control";prop.children [
        Html.div [prop.className "run-control-toolbar";prop.children [
            Html.div [Html.h2 [prop.className "text-lg font-semibold";prop.text "Run Control"];Html.p [prop.className "text-sm opacity-60";prop.text "Refreshes every 10 seconds"]]
            Html.label [prop.className "analysis-field";prop.children [
                Html.span "View"
                Html.select [prop.className "select select-bordered select-sm";prop.custom("aria-label","Run Control view");prop.value selected;prop.onChange setSelected
                             prop.children [Html.option [prop.value "";prop.text "Live nodes"];for batch in batches do Html.option [prop.value batch.SeedBase;prop.text("Run "+batch.SeedBase)]]]
            ]]
            button (if busy then "Refreshing…" else "Refresh") busy (fun () -> setRevision((+) 1))
        ]]
        Html.div [prop.className "run-control-totals";prop.children [
            Html.strong (if selected="" then "Live node activity" else "Viewing run "+selected)
            if selected<>"" then Html.span [prop.className "text-xs opacity-60";prop.text "Run progress; CPU/RAM show current node usage."]
            if updated<>"" then Html.span [prop.className "text-xs opacity-60";prop.text("Updated "+localTime updated)]
        ]]
        if error<>"" then Html.p [prop.className "run-control-warning";prop.text("Refresh failed: "+error+". Last successful data remains visible.")]
        for warning in warnings do Html.p [prop.className "run-control-warning";prop.text warning]
        if updated="" then Html.p [prop.className "p-8 opacity-60";prop.text "Loading nodes…"]
        else Html.div [prop.className "run-control-scroll";prop.children [Html.div [prop.className "run-control-grid";prop.children [
            Html.div [prop.className "run-control-row run-control-head";prop.children [for label in ["Node / run";"CPU";"RAM";"Run progress";"Elapsed";"Remaining"] -> Html.span label]]
            for node in nodes do nodeView node activities runs selected (warnings |> List.exists(fun warning -> warning.StartsWith("Live queue unavailable:"))) showLog
        ]]]]
        match log with
        | None -> ()
        | Some(title,text) ->
            Html.dialog [
                prop.id "run-control-log-dialog"
                prop.className "run-control-log-dialog"
                prop.custom("aria-labelledby","run-control-log-title")
                prop.custom("onCancel",fun (event:Browser.Types.Event) -> event.preventDefault();dismissLog())
                prop.children [
                    Html.div [prop.className "run-control-log-heading";prop.children [
                        Html.div [Html.h3 [prop.id "run-control-log-title";prop.text title];Html.p "Last 64 KiB of TOPAS log"]
                        Html.button [prop.className "analysis-button";prop.autoFocus true;prop.text "Close log";prop.onClick(fun _ -> dismissLog())]
                    ]]
                    Html.pre [prop.tabIndex 0;prop.text text]
                ]
            ]
    ]]
