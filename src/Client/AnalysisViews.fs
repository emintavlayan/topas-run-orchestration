module AnalysisViews

open System
open Feliz
open Shared
open SAFE
open FsToolkit.ErrorHandling

/// Represents a selected curve and its current physical mapping.
type private Selection = { Curve: AnalysisCurve; Mapping: AnalysisMapping }

/// Formats exported numbers with invariant significant digits.
let private format (value: float) = value.ToString("G6", Globalization.CultureInfo.InvariantCulture)

/// Reads a browser default while migrating the earlier formula labels.
let private stored (key: string) (fallback: string) (options: string list) =
    try
        let value = Browser.Dom.window.localStorage.getItem key
        let migrated = if value="Difference (ideal 0%)" then "difference" elif value="Ratio (ideal 100%)" then "ratio" else value
        if List.contains migrated options then migrated else fallback
    with _ -> fallback

/// Saves a preference when browser storage is available.
let private persist (key: string) (setter: string -> unit) (value: string) =
    try Browser.Dom.window.localStorage.setItem(key,value) with _ -> ()
    setter value

/// Renders an accessible labelled selection with separate values and display names.
let private choice (label: string) (value: string) (options: (string * string) list) (changed: string -> unit) =
    Html.label [prop.className "analysis-field"; prop.children [
        Html.span label
        Html.select [prop.className "select select-bordered select-sm w-full"; prop.value value; prop.onChange changed
                     prop.children [for key,text in options -> Html.option [prop.value key; prop.text text]]]]]

/// Renders a labelled physical coordinate input.
let private numeric (label: string) (value: float) (changed: float -> unit) =
    Html.label [prop.className "analysis-field"; prop.children [
        Html.span label
        Html.input [prop.className "input input-bordered input-sm w-full"; prop.type' "number"; prop.custom ("step","any")
                    prop.value (format value); prop.onChange (fun (text: string) ->
                        match Double.TryParse text with
                        | true,v when not (Double.IsNaN v || Double.IsInfinity v) -> changed v
                        | _ -> ())]]]

/// Renders a compact checkbox with a visible label.
let private checkbox (label: string) (value: bool) (changed: bool -> unit) =
    Html.label [prop.className "flex items-center gap-2 text-sm cursor-pointer"; prop.children [
        Html.input [prop.type' "checkbox"; prop.className "checkbox checkbox-xs"; prop.isChecked value; prop.onChange changed]
        Html.span label]]

/// Renders a keyboard-accessible tab button.
let private tab (current: string) (label: string) (changed: string -> unit) =
    Html.button [prop.className (if current=label then "analysis-tab active" else "analysis-tab")
                 prop.custom ("aria-pressed",(current=label)); prop.text label; prop.onClick (fun _ -> changed label)]

/// Escapes dataset paths and metadata as CSV cells.
let private csvRow values = values |> List.map (fun (value: string) -> "\""+value.Replace("\"","\"\"")+"\"") |> String.concat ","

/// Resolves the display label for a known option value.
let private optionLabel key options = options |> List.find (fun (value,_) -> value=key) |> snd

/// Maps stored coordinates onto the physical axis.
let private physical selected =
    selected.Curve.Points
    |> List.map (fun p ->
        let scale = if selected.Curve.ValueUnit="cGy" then 0.01 else 1.
        {p with Position=selected.Mapping.OriginMm+selected.Mapping.StepMm*p.Position;Dose=p.Dose*scale;Uncertainty=p.Uncertainty*scale})
    |> List.sortBy _.Position

/// Extracts the run identifier from a collected output path.
let private label (dataset: AnalysisDataset) = dataset.Id.Split('/').[0]

/// Identifies one saved curve in messages and exports.
let private scorerLabel (dataset: AnalysisDataset) = dataset.Label

/// Resolves the common raw dose unit after conversion of cGy to Gy.
let private rawUnit (curve: AnalysisCurve) =
    if curve.ValueUnit="Gy" || curve.ValueUnit="cGy" then "Gy" else curve.ValueUnit

/// Normalizes each role using its own settings and confirmed geometry.
let private prepare profileMode pddMode count position selected =
    result {
        let! _ = Analysis.validateMapping selected.Mapping
        let points = physical selected
        let mode = if selected.Mapping.Role="PDD" then pddMode else profileMode
        if mode="Central mean" then return! Analysis.normalizeCentralMean count selected.Mapping.CaxMm points
        else return! Analysis.normalize mode (if mode="CAX" then selected.Mapping.CaxMm else position) points
    }

/// Renders one metric per column and one dataset per row.
let private metricTable role flatness symmetry region (curves: (Selection * AnalysisPoint list) list) =
    let headers =
        if role="PDD" then ["Sampled dmax (mm)","dmax [mm]"; "R90 (mm)","R90 [mm]"; "R80 (mm)","R80 [mm]"; "R50 (mm)","R50 [mm]"]
        else ["FWHM (mm)","FWHM [mm]"; "Left penumbra 20-80 (mm)","L 20–80 [mm]"; "Right penumbra 20-80 (mm)","R 20–80 [mm]"
              "Field centre (mm)","Centre [mm]"; "Flatness (%)","Flatness [%]"; "Symmetry (%)","Symmetry [%]"]
    Html.section [prop.className "space-y-2"; prop.children [
        Html.h3 [prop.className "text-sm font-semibold"; prop.text role]
        Html.div [prop.className "overflow-x-auto rounded-lg border border-base-300"; prop.children [
            Html.table [prop.className "table table-sm analysis-metrics"; prop.children [
                Html.thead [Html.tr [
                    yield Html.th "Dataset"
                    for _,text in headers do yield Html.th text]]
                Html.tbody [
                    for s,_ in curves do
                        let values = Analysis.metricsWithConventions role flatness symmetry region s.Mapping.CaxMm (physical s) |> Map.ofList
                        Html.tr [
                            Html.td [prop.title s.Curve.Dataset.Id; prop.children [
                                Html.span [prop.className "analysis-dot"; prop.style [style.backgroundColor (AnalysisPlot.color s.Curve.Dataset.Id)]]
                                Html.span s.Curve.Dataset.Label]]
                            for key,_ in headers do
                                let value = Map.tryFind key values |> Option.flatten
                                let display = if key="FWHM (mm)" then sprintf "%.0f" else Analysis.displayNumber
                                Html.td [prop.text (value |> Option.map display |> Option.defaultValue "—")]]]]]]]]]

/// Renders the compact Analyse workspace with dedicated settings and geometry editors.
[<ReactComponent>]
let View () =
    let api = React.useMemo((fun () -> Api.makeProxy<ITopasApi>()), [||])
    let savedDatasets,setSavedDatasets = React.useState<SavedDatasetSummary list>([])
    let selected,setSelected = React.useStateWithUpdater<Selection list>([])
    let drafts,setDrafts = React.useStateWithUpdater<Map<string,AnalysisMapping>>(Map.empty)
    let error,setError = React.useState("")
    let busy,setBusy = React.useState(false)
    let activeTab,setActiveTab = React.useState("Profiles")
    let roleFilter,setRoleFilter = React.useState("Profiles")
    let profileMode,setProfileMode = React.useState(fun () -> stored "topas.analysis.profileNorm" "Central mean" ["Central mean";"Maximum";"CAX";"At position";"None"])
    let pddMode,setPddMode = React.useState(fun () -> stored "topas.analysis.pddNorm" "Maximum" ["Maximum";"At position";"None"])
    let countText,setCountText = React.useState(fun () -> stored "topas.analysis.centralCount" "10" ["5";"10"])
    let flatness,setFlatness = React.useState(fun () -> stored "topas.analysis.formula" "ratio" ["ratio";"difference"])
    let symmetry,setSymmetry = React.useState(fun () -> stored "topas.analysis.symmetry" "ratio" ["ratio";"difference";"area"])
    let normPosition,setNormPosition = React.useState(0.0)
    let region,setRegion = React.useState(0.8)
    let bands,setBands = React.useState(true)
    let samples,setSamples = React.useState(false)
    let customBounds,setCustomBounds = React.useState(false)
    let xmin,setXmin = React.useState(-300.0)
    let xmax,setXmax = React.useState(300.0)
    let count = if countText="5" then 5 else 10
    let flatnessDefinition = if flatness="ratio" then Analysis.MaxMinRatio else Analysis.NormalizedDifference
    let symmetryDefinition = if symmetry="ratio" then Analysis.PointRatio elif symmetry="area" then Analysis.AreaDifference else Analysis.PointDifference
    let flatnessOptions = ["ratio","IEC / Elekta — max/min"; "difference","Varian / IAEA — difference"]
    let symmetryOptions = ["ratio","IEC / Elekta — point ratio"; "difference","Varian — point difference"; "area","IAEA — area difference"]
    /// Executes a server operation and surfaces any failure.
    let run operation =
        setBusy true
        setError ""
        async {
            try do! operation with ex -> setError ex.Message
            setBusy false
        } |> Async.StartImmediate
    /// Refreshes saved dataset versions without rediscovering filesystem curves.
    let refresh () = run (async {
        let! result = api.getSavedDatasets()
        match result with Ok values -> setSavedDatasets values | Error message -> setError message })
    React.useEffectOnce refresh
    let datasets = savedDatasets |> List.collect _.Curves
    let selectedRuns = selected |> List.map (fun s -> label s.Curve.Dataset) |> List.distinct
    /// Discards manual edits for one dataset without changing active geometry.
    let discardRunDrafts datasetId =
        setDrafts (fun values -> values |> Map.filter (fun id _ -> id.Split('/').[0]<>datasetId))
    /// Removes a dataset and its loaded curves from the comparison.
    let removeRun datasetId =
        setSelected (fun values -> values |> List.filter (fun s -> label s.Curve.Dataset<>datasetId))
        discardRunDrafts datasetId
    /// Loads the explicitly requested saved curves and surfaces individual failures.
    let loadCurves datasetId (outputs: AnalysisDataset list) replace = run (async {
        let! results = outputs |> List.map (fun dataset -> async {
            let! result = api.loadAnalysisCurve dataset.Id
            return dataset,result }) |> Async.Parallel
        let loaded = results |> Array.choose (fun (_,result) ->
            match result with
            | Error _ -> None
            | Ok curve ->
                let mapping = curve.Mapping |> Option.defaultValue {Role="PDD";Axis=2;StepMm=1.0;OriginMm=0.0;CaxMm=0.0;Confirmed=false}
                Some {Curve=curve;Mapping=mapping}) |> Array.toList
        setSelected (fun values ->
            let retained = values |> List.filter (fun s -> if replace then label s.Curve.Dataset<>datasetId else not (outputs |> List.exists (fun d -> d.Id=s.Curve.Dataset.Id)))
            retained @ loaded)
        if replace then discardRunDrafts datasetId
        if selected.IsEmpty then
            setRoleFilter (if loaded |> List.exists (fun s -> s.Mapping.Role="X profile" || s.Mapping.Role="Y profile") then "Profiles" else "PDD")
        let failures = results |> Array.choose (fun (d,result) -> match result with Error message -> Some (d.Label+": "+message) | Ok _ -> None)
        setError (String.concat "\n" failures) })
    /// Toggles all curves in one saved dataset version.
    let toggleRun datasetId =
        if List.contains datasetId selectedRuns then removeRun datasetId
        else loadCurves datasetId (datasets |> List.filter (fun d -> label d=datasetId)) true
    /// Toggles one independently selected scan within a saved dataset.
    let toggleCurve (dataset: AnalysisDataset) =
        if selected |> List.exists (fun s -> s.Curve.Dataset.Id=dataset.Id) then
            setSelected (fun values -> values |> List.filter (fun s -> s.Curve.Dataset.Id<>dataset.Id))
            setDrafts (Map.remove dataset.Id)
        else loadCurves (label dataset) [dataset] false
    /// Changes a manual draft while keeping the resolved geometry active.
    let editMapping s change =
        let id = s.Curve.Dataset.Id
        setDrafts (fun values ->
            let current = Map.tryFind id values |> Option.defaultValue s.Mapping
            Map.add id (change current) values)
    /// Saves a validated manual override and activates it after the server accepts it.
    let saveMapping s mapping = run (async {
        let resolved = {mapping with Confirmed=true}
        let id = s.Curve.Dataset.Id
        let! result = api.saveAnalysisMapping {Id=id;Mapping=resolved}
        match result with
        | Ok () ->
            setSelected (fun values -> values |> List.map (fun item ->
                if item.Curve.Dataset.Id=id then
                    {Curve={item.Curve with Mapping=Some resolved;MappingSource="Saved manual override."}; Mapping=resolved}
                else item))
            setDrafts (Map.remove id)
        | Error message -> setError message })
    /// Replaces a saved override with geometry re-derived from the archived inputs.
    let useInputs s = run (async {
        let id = s.Curve.Dataset.Id
        let! result = api.resetAnalysisMapping id
        match result with
        | Ok curve ->
            match curve.Mapping with
            | Some mapping ->
                setSelected (fun values -> values |> List.map (fun item -> if item.Curve.Dataset.Id=id then {Curve=curve;Mapping=mapping} else item))
                setDrafts (Map.remove id)
            | None -> setError "Input geometry is unavailable."
        | Error message -> setError message })
    let prepared = selected |> List.map (fun s -> s,prepare profileMode pddMode count normPosition s)
    let processed = prepared |> List.choose (fun (s,r) -> match r with Ok points -> Some (s,points) | Error _ -> None)
    /// Returns the active normalization for one curve role.
    let modeFor role = if role="PDD" then pddMode else profileMode
    /// Describes the applied normalization in exports and the toolbar.
    let modeLabel role = if modeFor role="Central mean" then $"Central mean · {count} points" else modeFor role
    /// Returns curve metrics from the complete physical samples.
    let metrics s = Analysis.metricsWithConventions s.Mapping.Role flatnessDefinition symmetryDefinition region s.Mapping.CaxMm (physical s)
    let curveExport =
        [yield "dataset,role,position_mm,dose,dose_unit,scaled_absolute_uncertainty,normalization,central_points,CAX_mm"
         for s,points in processed do
             for p in points do
                 yield csvRow [s.Curve.Dataset.Label;s.Mapping.Role;format p.Position;format p.Dose;(if modeFor s.Mapping.Role="None" then rawUnit s.Curve else "%");(if s.Curve.HasUncertainty then format p.Uncertainty else "");modeFor s.Mapping.Role
                               (if modeFor s.Mapping.Role="Central mean" then string count else "");format s.Mapping.CaxMm]] |> String.concat "\n"
    let metricExport =
        [yield "dataset,role,metric,value,flatness_definition,symmetry_definition,evaluation_fraction,CAX_mm"
         for s,_ in processed do
             for name,value in metrics s do
                 yield csvRow [s.Curve.Dataset.Label;s.Mapping.Role;name;value |> Option.map format |> Option.defaultValue ""
                               optionLabel flatness flatnessOptions;optionLabel symmetry symmetryOptions;format region;format s.Mapping.CaxMm]] |> String.concat "\n"
    let visible = processed |> List.filter (fun (s,_) ->
        if roleFilter="Profiles" then s.Mapping.Role="X profile" || s.Mapping.Role="Y profile"
        elif roleFilter="X" then s.Mapping.Role="X profile"
        elif roleFilter="Y" then s.Mapping.Role="Y profile"
        else s.Mapping.Role="PDD")
    let plotted = visible |> List.map (fun (s,points) ->
        let curve: AnalysisPlot.Curve =
            {Id=s.Curve.Dataset.Id; Label=s.Curve.Dataset.Label; Role=s.Mapping.Role; HasUncertainty=s.Curve.HasUncertainty
             Points=points |> List.filter (fun p -> not customBounds || (p.Position>=xmin && p.Position<=xmax))}
        curve)
    let normalization = modeLabel (if roleFilter="PDD" then "PDD" else "X profile")
    let raw = modeFor (if roleFilter="PDD" then "PDD" else "X profile")="None"
    let rawUnits = visible |> List.map (fun (s,_) -> rawUnit s.Curve) |> List.distinct
    let incompatibleUnits = raw && rawUnits.Length>1
    let unitName = if raw then "Dose ["+(rawUnits |> List.tryHead |> Option.defaultValue "unknown")+"]" else "Relative dose [%]"
    /// Renders a CSV download for currently processed datasets.
    let download (name: string) (text: string) (content: string) =
        Html.a [prop.className "analysis-button"; prop.download name
                prop.href ("data:text/csv;charset=utf-8,"+Uri.EscapeDataString content); prop.text text]
    Html.div [prop.className "analysis-workspace"; prop.children [
        Html.aside [prop.className "analysis-runs"; prop.children [
            Html.span [prop.className "text-sm font-semibold"; prop.text "Datasets"]
            Html.div [prop.className "analysis-run-choices"; prop.children [
                if savedDatasets.IsEmpty && not busy then Html.span [prop.className "text-sm text-base-content/60"; prop.text "No saved datasets. Open Import to add files or existing collections."]
                for dataset in savedDatasets do
                    let active = List.contains dataset.Id selectedRuns
                    Html.details [prop.className (if active then "analysis-run active" else "analysis-run"); prop.children [
                        Html.summary [prop.children [
                            Html.input [prop.type' "checkbox"; prop.className "checkbox checkbox-xs"; prop.disabled busy
                                        prop.custom ("aria-label","Include dataset "+dataset.Name); prop.isChecked active
                                        prop.onClick (fun event -> event.stopPropagation())
                                        prop.onChange (fun (_:bool) -> toggleRun dataset.Id)]
                            Html.span dataset.Name]]
                        Html.div [prop.className "analysis-collection-list";prop.children [
                            Html.p [prop.className "text-xs text-base-content/60";prop.text (dataset.Source+" · "+(if dataset.CollectionKey="" then dataset.SavedAt else dataset.CollectionKey))]
                            for curve in dataset.Curves do
                                Html.label [prop.className "analysis-collection";prop.children [
                                    Html.input [prop.type' "checkbox";prop.className "checkbox checkbox-xs";prop.disabled busy
                                                prop.isChecked (selected |> List.exists (fun s -> s.Curve.Dataset.Id=curve.Id))
                                                prop.onChange (fun (_:bool) -> toggleCurve curve)]
                                    Html.span [prop.className "analysis-dot";prop.style [style.backgroundColor (AnalysisPlot.color curve.Id)]]
                                    Html.span curve.Label]]]]]]]]
            Html.button [prop.className "btn btn-ghost btn-xs"; prop.disabled busy; prop.text (if busy then "Loading…" else "Refresh"); prop.onClick (fun _ -> refresh())]
            if not selected.IsEmpty then
                Html.button [prop.className "btn btn-ghost btn-xs"; prop.disabled busy; prop.text "Clear"
                             prop.onClick (fun _ -> setSelected (fun _ -> []); setDrafts (fun _ -> Map.empty))]]]
        Html.div [prop.className "analysis-main"; prop.children [
            Html.div [prop.className "analysis-toolbar"; prop.children [
                Html.div [prop.className "flex gap-5"; prop.children [for title in ["Profiles";"Metrics";"Settings"] -> tab activeTab title setActiveTab]]
                if not processed.IsEmpty then
                    Html.div [prop.className "flex gap-1"; prop.children [download "analysis-curves.csv" "Export Curves" curveExport; download "analysis-metrics.csv" "Export Metrics" metricExport]]]]
            if error<>"" then Html.div [prop.className "alert alert-error text-sm"; prop.custom ("role","alert"); prop.text error]
            for s,result in prepared do
                match result with
                | Error message when s.Mapping.Confirmed ->
                    Html.button [prop.className "analysis-notice text-error"; prop.title s.Curve.Dataset.Id
                                 prop.text $"{scorerLabel s.Curve.Dataset} · {s.Mapping.Role} omitted: {message}"
                                 prop.onClick (fun _ -> setActiveTab "Settings")]
                | Error _ ->
                    Html.button [prop.className "analysis-notice"; prop.title s.Curve.Dataset.Id; prop.text $"{scorerLabel s.Curve.Dataset} · {s.Mapping.Role}: Geometry unavailable · Settings"
                                 prop.onClick (fun _ -> setActiveTab "Settings")]
                | _ -> Html.none
            if activeTab="Profiles" then
                Html.div [prop.className "analysis-plot-toolbar"; prop.children [
                    Html.div [prop.className "join"; prop.children [
                        for role in ["Profiles";"X";"Y";"PDD"] do
                            Html.button [prop.className (if roleFilter=role then "join-item btn btn-sm btn-primary" else "join-item btn btn-sm btn-ghost")
                                         prop.text role; prop.onClick (fun _ -> setRoleFilter role)]]]
                    Html.span [prop.className "text-xs text-base-content/60"; prop.text normalization]]]
                if selected.IsEmpty then
                    Html.div [prop.className "analysis-empty"; prop.text "Select datasets or individual curves."]
                elif incompatibleUnits then
                    Html.div [prop.className "analysis-empty";prop.text "Selected raw curves have incompatible value units. Choose normalization in Settings or select compatible curves."]
                elif customBounds && xmin>=xmax then
                    Html.div [prop.className "analysis-empty"; prop.text "Plot minimum must be below maximum. Change bounds in Settings."]
                else
                    let plotContext = String.concat "|" [roleFilter;normalization;format normPosition;string customBounds;format xmin;format xmax]
                    AnalysisPlot.view roleFilter
                        plotContext (if roleFilter="PDD" then "Depth [mm]" else "Position [mm]") unitName
                        bands samples plotted
            elif activeTab="Metrics" then
                Html.div [prop.className "space-y-6 p-4"; prop.children [
                    Html.p [prop.className "text-xs text-base-content/60"; prop.text (String.concat " · " [optionLabel flatness flatnessOptions; optionLabel symmetry symmetryOptions; "Point region "+Analysis.displayNumber (100.0*region)+"%"])]
                    if processed.IsEmpty then Html.div [prop.className "analysis-empty"; prop.text "No metrics available for the selected datasets."]
                    for role in ["X profile";"Y profile";"PDD"] do
                        let curves = processed |> List.filter (fun (s,_) -> s.Mapping.Role=role)
                        if not curves.IsEmpty then metricTable role flatnessDefinition symmetryDefinition region curves]]
            else
                Html.div [prop.className "analysis-settings"; prop.children [
                    Html.section [prop.className "analysis-settings-card"; prop.children [
                        Html.h3 "Normalization"
                        choice "Profiles" profileMode (["Central mean";"Maximum";"CAX";"At position";"None"] |> List.map (fun v -> v,v)) (persist "topas.analysis.profileNorm" setProfileMode)
                        if profileMode="Central mean" then choice "Central samples" countText ["5","5 points";"10","10 points"] (persist "topas.analysis.centralCount" setCountText)
                        choice "PDD" pddMode (["Maximum";"At position";"None"] |> List.map (fun v -> v,v)) (persist "topas.analysis.pddNorm" setPddMode)
                        if profileMode="At position" || pddMode="At position" then numeric "Reference position / depth [mm]" normPosition setNormPosition]]
                    Html.section [prop.className "analysis-settings-card"; prop.children [
                        Html.h3 "Metrics"
                        choice "Flatness" flatness flatnessOptions (persist "topas.analysis.formula" setFlatness)
                        choice "Symmetry" symmetry symmetryOptions (persist "topas.analysis.symmetry" setSymmetry)
                        numeric "Point-metric region [%]" (region*100.0) (fun v -> if v>0.0 && v<=100.0 then setRegion (v/100.0))]]
                    Html.section [prop.className "analysis-settings-card analysis-plot-settings"; prop.children [
                        Html.h3 "Plot"
                        Html.div [prop.className "analysis-plot-options"; prop.children [
                            checkbox "Uncertainty band" bands setBands
                            checkbox "Sample markers" samples setSamples
                            checkbox "Custom horizontal range" customBounds setCustomBounds]]
                        if customBounds then
                            Html.div [prop.className "grid grid-cols-2 gap-3"; prop.children [numeric "Minimum [mm]" xmin setXmin; numeric "Maximum [mm]" xmax setXmax]]]]
                    Html.section [prop.className "analysis-settings-card analysis-geometry"; prop.children [
                        Html.h3 "Geometry"
                        if selected.IsEmpty then Html.p [prop.className "text-sm text-base-content/60"; prop.text "No loaded curves."]
                        for s in selected do
                            let id = s.Curve.Dataset.Id
                            let draft = Map.tryFind id drafts
                            let editing = draft.IsSome || not s.Mapping.Confirmed
                            let mapping = draft |> Option.defaultValue s.Mapping
                            let source = s.Curve.MappingSource.Replace("Unconfirmed editable preset. Input detection: ", "")
                            Html.details [prop.className "analysis-mapping"; prop.children [
                                Html.summary [prop.children [
                                    Html.span $"{scorerLabel s.Curve.Dataset} · {s.Mapping.Role}"
                                    Html.span [prop.className "analysis-mapping-status"; prop.text (if not s.Mapping.Confirmed then "Unavailable" elif source.StartsWith("Derived") then "Imported geometry" else "Override")]]]
                                Html.div [prop.className "analysis-mapping-body"; prop.children [
                                    Html.p [prop.className "text-xs break-words text-base-content/60"; prop.text source]
                                    if s.Mapping.Confirmed && not (source.StartsWith("Derived")) then
                                        Html.button [prop.className "analysis-button"; prop.disabled busy; prop.text "Restore imported geometry"; prop.onClick (fun _ -> useInputs s)]
                                    if editing then
                                        Html.div [prop.className "analysis-mapping-fields"; prop.children [
                                            choice "Role" mapping.Role (["PDD";"X profile";"Y profile"] |> List.map (fun v -> v,v)) (fun v -> editMapping s (fun m -> {m with Role=v}))
                                            numeric "Spacing [mm / coordinate]" mapping.StepMm (fun v -> editMapping s (fun m -> {m with StepMm=v}))
                                            numeric "Origin [mm]" mapping.OriginMm (fun v -> editMapping s (fun m -> {m with OriginMm=v}))
                                            numeric "CAX [mm]" mapping.CaxMm (fun v -> editMapping s (fun m -> {m with CaxMm=v}))]]
                                        Html.div [prop.className "analysis-actions"; prop.children [
                                            Html.button [prop.className "analysis-button"; prop.disabled busy; prop.text "Save values"; prop.onClick (fun _ -> saveMapping s mapping)]
                                            if draft.IsSome then Html.button [prop.className "analysis-button"; prop.disabled busy; prop.text "Cancel"; prop.onClick (fun _ -> setDrafts (Map.remove id))]]]
                                    else
                                        let mapped = physical s
                                        let minimum = mapped |> List.minBy _.Position |> _.Position
                                        let maximum = mapped |> List.maxBy _.Position |> _.Position
                                        Html.p [prop.className "text-xs text-base-content/60"; prop.text $"Spacing {Analysis.displayNumber mapping.StepMm} mm · Origin {Analysis.displayNumber mapping.OriginMm} mm · CAX {Analysis.displayNumber mapping.CaxMm} mm"]
                                        Html.p [prop.className "text-xs text-base-content/60"; prop.text $"Mapped range {Analysis.displayNumber minimum} to {Analysis.displayNumber maximum} mm · Stored coordinates {Analysis.displayNumber s.Curve.Points.Head.Position} to {Analysis.displayNumber (List.last s.Curve.Points).Position}"]
                                        Html.button [prop.className "analysis-button"; prop.disabled busy; prop.text "Enter manual values"; prop.onClick (fun _ -> editMapping s (fun mapping -> mapping))]
                                    Html.p [prop.className "text-xs text-base-content/60"; prop.title s.Curve.Dataset.Id; prop.text $"{s.Curve.Points.Length} samples · {s.Curve.Dataset.Label}"]]]]]]]
                    Html.details [prop.className "analysis-methods"; prop.children [
                        Html.summary "Equations and references"
                        Html.div [prop.className "space-y-3 pt-3 text-sm"; prop.children [
                            Html.p "Flatness: IEC / Elekta = 100 Dmax/Dmin; Varian / IAEA = 100 (Dmax−Dmin)/(Dmax+Dmin)."
                            Html.p "Symmetry: IEC / Elekta = 100 max(DL/DR, DR/DL); Varian = 100 max|DL−DR|/D(CAX); IAEA = 100 (AL−AR)/(AL+AR)."
                            Html.p "Point metrics: symmetric CAX region, limited by the nearer 50% peak-dose edge. IAEA area: CAX to each 50% CAX-dose edge. No smoothing."
                            Html.p "Convention names identify equations. Electron acceptance protocols also specify depth, reference dose and evaluation region; these settings do not select a complete acceptance protocol."
                            Html.p "Central mean uses the nearest N samples about CAX, with lower position breaking ties. Uncertainty scales by the fixed normalization factor. Raw cGy values are converted to Gy; incompatible raw units cannot share a plot."
                            Html.p "R50 is dose-based. Metrics use the full curve. Missing values are shown as —."
                            Html.a [prop.className "link"; prop.href "https://static.standardimaging.com/referenced-manuals/80614-11-DoseView-MNL.pdf#page=131"; prop.target "_blank"; prop.rel "noreferrer"; prop.text "DoseView · Table 16-2"]
                            Html.br []
                            Html.a [prop.className "link"; prop.href "https://international.anl.gov/training/materials/IAEA%20Publications/P1196_Handbook%20RADIATION%20ONCOLOGY%20PHYSICS.pdf"; prop.target "_blank"; prop.rel "noreferrer"; prop.text "IAEA handbook · §6.9"]]]]]
                ]]
        ]]
    ]]
