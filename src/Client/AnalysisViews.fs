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
    |> List.map (fun p -> {p with Position=selected.Mapping.OriginMm+selected.Mapping.StepMm*p.Position})
    |> List.sortBy _.Position

/// Shortens a collected path without losing collection identity.
let private label (dataset: AnalysisDataset) = dataset.Label.Replace("/", " · ")

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
        else ["FWHM (mm)","50% width [mm]"; "Left penumbra 20-80 (mm)","L 20–80 [mm]"; "Right penumbra 20-80 (mm)","R 20–80 [mm]"
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
                                Html.span (label s.Curve.Dataset)]]
                            for key,_ in headers do
                                let value = Map.tryFind key values |> Option.flatten
                                Html.td [prop.text (value |> Option.map (fun v -> v.ToString("F2", Globalization.CultureInfo.InvariantCulture)) |> Option.defaultValue "—")]]]]]]]]]

/// Renders the compact Analyse workspace with dedicated settings and geometry editors.
[<ReactComponent>]
let view () =
    let api = React.useMemo((fun () -> Api.makeProxy<ITopasApi>()), [||])
    let datasets,setDatasets = React.useState<AnalysisDataset list>([])
    let selected,setSelected = React.useStateWithUpdater<Selection list>([])
    let error,setError = React.useState("")
    let busy,setBusy = React.useState(false)
    let activeTab,setActiveTab = React.useState("Profiles")
    let roleFilter,setRoleFilter = React.useState("Profiles")
    let query,setQuery = React.useState("")
    let profileMode,setProfileMode = React.useState(fun () -> stored "topas.analysis.profileNorm" "Central mean" ["Central mean";"Maximum";"CAX";"At position";"None"])
    let pddMode,setPddMode = React.useState(fun () -> stored "topas.analysis.pddNorm" "Maximum" ["Maximum";"At position";"None"])
    let countText,setCountText = React.useState(fun () -> stored "topas.analysis.centralCount" "10" ["5";"10"])
    let flatness,setFlatness = React.useState(fun () -> stored "topas.analysis.formula" "ratio" ["ratio";"difference"])
    let symmetry,setSymmetry = React.useState(fun () -> stored "topas.analysis.symmetry" "ratio" ["ratio";"difference";"area"])
    let normPosition,setNormPosition = React.useState(0.0)
    let region,setRegion = React.useState(0.8)
    let bands,setBands = React.useState(true)
    let samples,setSamples = React.useState(false)
    let reference,setReference = React.useState("")
    let differences,setDifferences = React.useState(false)
    let plateau,setPlateau = React.useState(false)
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
    /// Refreshes collected output discovery.
    let refresh () = run (async {
        let! result = api.listAnalysisDatasets()
        match result with Ok values -> setDatasets values | Error message -> setError message })
    React.useEffectOnce refresh
    /// Updates a selected mapping and suspends its analysis until saved.
    let update id change =
        setSelected (fun values -> values |> List.map (fun s -> if s.Curve.Dataset.Id=id then {s with Mapping=change s.Mapping} else s))
    /// Removes a curve and any reference to it.
    let remove id =
        setSelected (fun values -> values |> List.filter (fun s -> s.Curve.Dataset.Id<>id))
        if reference=id then
            setReference ""
            setDifferences false
    /// Loads a curve or removes an existing selection.
    let toggle (dataset: AnalysisDataset) =
        if selected |> List.exists (fun s -> s.Curve.Dataset.Id=dataset.Id) then remove dataset.Id
        else run (async {
            let! result = api.loadAnalysisCurve dataset.Id
            match result with
            | Error message -> setError message
            | Ok curve ->
                let mapping = curve.Mapping |> Option.defaultValue {Role="PDD";Axis=2;StepMm=1.0;OriginMm=0.0;CaxMm=0.0;Confirmed=false}
                if selected.IsEmpty then setRoleFilter (if mapping.Role="PDD" then "PDD" else "Profiles")
                setSelected (fun values -> values @ [{Curve=curve;Mapping=mapping}]) })
    /// Saves an explicit mapping after server validation.
    let saveMapping s = run (async {
        let mapping = {s.Mapping with Confirmed=true}
        let! result = api.saveAnalysisMapping {Id=s.Curve.Dataset.Id;Mapping=mapping}
        match result with
        | Ok () -> update s.Curve.Dataset.Id (fun _ -> mapping)
        | Error message -> setError message })
    let prepared = selected |> List.map (fun s -> s,prepare profileMode pddMode count normPosition s)
    let processed = prepared |> List.choose (fun (s,r) -> match r with Ok points -> Some (s,points) | Error _ -> None)
    let referenceCurve = processed |> List.tryFind (fun (s,_) -> s.Curve.Dataset.Id=reference)
    /// Returns the active normalization for one curve role.
    let modeFor role = if role="PDD" then pddMode else profileMode
    /// Describes the applied normalization in exports and the toolbar.
    let modeLabel role = if modeFor role="Central mean" then $"Central mean · {count} points" else modeFor role
    /// Returns curve metrics from the complete physical samples.
    let metrics s = Analysis.metricsWithConventions s.Mapping.Role flatnessDefinition symmetryDefinition region s.Mapping.CaxMm (physical s)
    let curveExport =
        [yield "dataset,role,position_mm,dose,scaled_absolute_uncertainty,normalization,central_points,CAX_mm"
         for s,points in processed do
             for p in points do
                 yield csvRow [s.Curve.Dataset.Label;s.Mapping.Role;format p.Position;format p.Dose;format p.Uncertainty;modeFor s.Mapping.Role
                               (if modeFor s.Mapping.Role="Central mean" then string count else "");format s.Mapping.CaxMm]] |> String.concat "\n"
    let metricExport =
        [yield "dataset,role,metric,value,flatness_definition,symmetry_definition,evaluation_fraction,CAX_mm"
         for s,_ in processed do
             for name,value in metrics s do
                 yield csvRow [s.Curve.Dataset.Label;s.Mapping.Role;name;value |> Option.map format |> Option.defaultValue ""
                               optionLabel flatness flatnessOptions;optionLabel symmetry symmetryOptions;format region;format s.Mapping.CaxMm]] |> String.concat "\n"
    let visible = processed |> List.filter (fun (s,_) ->
        if roleFilter="Profiles" then s.Mapping.Role<>"PDD"
        elif roleFilter="X" then s.Mapping.Role="X profile"
        elif roleFilter="Y" then s.Mapping.Role="Y profile"
        else s.Mapping.Role="PDD")
    let useDifference = differences && referenceCurve.IsSome
    let plotted = visible |> List.choose (fun (s,points) ->
        let values =
            if not useDifference then Some points
            else
                match referenceCurve with
                | Some (r,rp) when r.Mapping.Role=s.Mapping.Role && r.Curve.Dataset.Id<>s.Curve.Dataset.Id ->
                    Some (points |> List.choose (fun p -> Analysis.interpolate rp p.Position |> Option.map (fun baseline -> {p with Dose=p.Dose-baseline;Uncertainty=0.0})))
                | _ -> None
        values |> Option.map (fun p ->
            let curve: AnalysisPlot.Curve =
                {Id=s.Curve.Dataset.Id; Label=label s.Curve.Dataset; Role=s.Mapping.Role
                 Points=p |> List.filter (fun p -> not customBounds || (p.Position>=xmin && p.Position<=xmax))}
            curve))
    let normalization = modeLabel (if roleFilter="PDD" then "PDD" else "X profile")
    let raw = modeFor (if roleFilter="PDD" then "PDD" else "X profile")="None"
    let unitName = if raw && useDifference then "Δ dose [Gy]" elif raw then "Dose [Gy]" elif useDifference then "Δ dose [pp]" else "Relative dose [%]"
    let pending = selected |> List.filter (fun s -> not s.Mapping.Confirmed) |> List.length
    /// Renders a CSV download for currently processed datasets.
    let download (name: string) (text: string) (content: string) =
        Html.a [prop.className "btn btn-ghost btn-sm"; prop.download name
                prop.href ("data:text/csv;charset=utf-8,"+Uri.EscapeDataString content); prop.text text]
    Html.div [prop.className "analysis-workspace"; prop.children [
        Html.aside [prop.className "analysis-sidebar"; prop.children [
            Html.div [prop.className "flex items-center justify-between gap-2"; prop.children [
                Html.h2 [prop.className "text-sm font-semibold"; prop.text "Datasets"]
                Html.button [prop.className "btn btn-ghost btn-xs"; prop.disabled busy; prop.text (if busy then "Loading…" else "Refresh"); prop.onClick (fun _ -> refresh())]]]
            Html.input [prop.className "input input-bordered input-sm w-full"; prop.placeholder "Filter outputs"; prop.custom ("aria-label","Filter outputs")
                        prop.value query; prop.onChange setQuery]
            Html.div [prop.className "analysis-dataset-list"; prop.children [
                if datasets.IsEmpty && not busy then Html.p [prop.className "text-sm text-base-content/60"; prop.text "No collected outputs."]
                for dataset in datasets |> List.filter (fun d -> d.Label.ToLowerInvariant().Contains(query.ToLowerInvariant())) do
                    let isSelected = selected |> List.exists (fun s -> s.Curve.Dataset.Id=dataset.Id)
                    Html.label [prop.className (if isSelected then "analysis-dataset selected" else "analysis-dataset"); prop.title dataset.Id; prop.children [
                        Html.input [prop.type' "checkbox"; prop.className "checkbox checkbox-xs"; prop.disabled busy; prop.isChecked isSelected; prop.onChange (fun (_:bool) -> toggle dataset)]
                        Html.span [prop.className "analysis-dot"; prop.style [style.backgroundColor (AnalysisPlot.color dataset.Id)]]
                        Html.span [prop.className "min-w-0 break-words"; prop.text (label dataset)]]]]]
            Html.div [prop.className "analysis-sidebar-footer"; prop.children [
                Html.span $"{selected.Length} selected"
                Html.button [prop.className "btn btn-ghost btn-xs"; prop.disabled (selected.IsEmpty || busy); prop.text "Clear"
                             prop.onClick (fun _ -> setSelected (fun _ -> []); setReference ""; setDifferences false)]]]
        ]]
        Html.div [prop.className "analysis-main"; prop.children [
            Html.div [prop.className "analysis-toolbar"; prop.children [
                Html.div [prop.className "flex gap-5"; prop.children [for title in ["Profiles";"Metrics";"Settings"] -> tab activeTab title setActiveTab]]
                if not processed.IsEmpty then
                    Html.div [prop.className "flex gap-1"; prop.children [download "analysis-curves.csv" "Curves CSV" curveExport; download "analysis-metrics.csv" "Metrics CSV" metricExport]]]]
            if error<>"" then Html.div [prop.className "alert alert-error text-sm"; prop.custom ("role","alert"); prop.text error]
            if pending>0 then
                Html.button [prop.className "analysis-notice"; prop.text $"{pending} mapping(s) require confirmation · Open settings"; prop.onClick (fun _ -> setActiveTab "Settings")]
            for s,result in prepared do
                match result with
                | Error message when s.Mapping.Confirmed -> Html.p [prop.className "text-error text-sm"; prop.text $"{label s.Curve.Dataset}: {message}"]
                | _ -> Html.none
            if activeTab="Profiles" then
                Html.div [prop.className "analysis-plot-toolbar"; prop.children [
                    Html.div [prop.className "join"; prop.children [
                        for role in ["Profiles";"X";"Y";"PDD"] do
                            Html.button [prop.className (if roleFilter=role then "join-item btn btn-sm btn-primary" else "join-item btn btn-sm btn-ghost")
                                         prop.text role; prop.onClick (fun _ -> setRoleFilter role)]]]
                    Html.span [prop.className "text-xs text-base-content/60"; prop.text normalization]
                    Html.div [prop.className "flex items-center gap-3"; prop.children [
                        if referenceCurve.IsSome then checkbox "Difference" differences setDifferences
                        if not raw && not useDifference then checkbox "95–105%" plateau setPlateau]]]]
                if selected.IsEmpty then
                    Html.div [prop.className "analysis-empty"; prop.text "Select collected outputs."]
                elif customBounds && xmin>=xmax then
                    Html.div [prop.className "analysis-empty"; prop.text "Plot minimum must be below maximum. Change bounds in Settings."]
                else
                    AnalysisPlot.view (if useDifference then "Reference difference" else roleFilter)
                        (if roleFilter="PDD" then "Depth [mm]" else "Position [mm]") unitName
                        (bands && not useDifference) samples (plateau && not raw && not useDifference) plotted
                if useDifference then
                    match referenceCurve with
                    | Some (s,_) -> Html.p [prop.className "text-xs text-base-content/60"; prop.text $"Reference: {label s.Curve.Dataset} · {s.Mapping.Role}"]
                    | _ -> Html.none
            elif activeTab="Metrics" then
                Html.div [prop.className "space-y-6 p-4"; prop.children [
                    Html.p [prop.className "text-xs text-base-content/60"; prop.text (String.concat " · " [optionLabel flatness flatnessOptions; optionLabel symmetry symmetryOptions; "Point region "+format (100.0*region)+"%"])]
                    if processed.IsEmpty then Html.div [prop.className "analysis-empty"; prop.text "Select curves with confirmed mappings."]
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
                    Html.section [prop.className "analysis-settings-card"; prop.children [
                        Html.h3 "Plot"
                        checkbox "Uncertainty band" bands setBands
                        checkbox "Sample markers" samples setSamples
                        checkbox "Custom horizontal range" customBounds setCustomBounds
                        if customBounds then
                            Html.div [prop.className "grid grid-cols-2 gap-3"; prop.children [numeric "Minimum [mm]" xmin setXmin; numeric "Maximum [mm]" xmax setXmax]]
                        choice "Difference reference" reference (("","None") :: (processed |> List.map (fun (s,_) -> s.Curve.Dataset.Id,label s.Curve.Dataset)))
                            (fun value -> setReference value; setDifferences (value<>""))]]
                    Html.section [prop.className "analysis-settings-card"; prop.children [
                        Html.h3 "Geometry"
                        if selected.IsEmpty then Html.p [prop.className "text-sm text-base-content/60"; prop.text "No selected datasets."]
                        for s in selected do
                            let id = s.Curve.Dataset.Id
                            Html.details [prop.className "analysis-mapping"; prop.custom ("open",not s.Mapping.Confirmed); prop.children [
                                Html.summary [prop.children [Html.span (label s.Curve.Dataset); Html.span [prop.className (if s.Mapping.Confirmed then "text-base-content/50" else "text-warning"); prop.text (if s.Mapping.Confirmed then "Mapped" else "Confirm")]]]
                                Html.div [prop.className "space-y-3 pt-3"; prop.children [
                                    Html.p [prop.className "text-xs break-words text-base-content/60"; prop.text s.Curve.MappingSource]
                                    choice "Role" s.Mapping.Role (["PDD";"X profile";"Y profile"] |> List.map (fun v -> v,v)) (fun v -> update id (fun m -> {m with Role=v;Confirmed=false}))
                                    numeric "Spacing [mm / coordinate]" s.Mapping.StepMm (fun v -> update id (fun m -> {m with StepMm=v;Confirmed=false}))
                                    numeric "Origin [mm]" s.Mapping.OriginMm (fun v -> update id (fun m -> {m with OriginMm=v;Confirmed=false}))
                                    numeric "CAX [mm]" s.Mapping.CaxMm (fun v -> update id (fun m -> {m with CaxMm=v;Confirmed=false}))
                                    Html.p [prop.className "text-xs text-base-content/60"; prop.text $"{s.Curve.Points.Length} samples · Stored axis {s.Mapping.Axis} · {format s.Curve.Points.Head.Position} to {format (List.last s.Curve.Points).Position}"]
                                    Html.button [prop.className "btn btn-sm btn-primary"; prop.disabled busy; prop.text "Confirm and save"; prop.onClick (fun _ -> saveMapping s)]]]]]]]
                    Html.details [prop.className "analysis-methods"; prop.children [
                        Html.summary "Equations and references"
                        Html.div [prop.className "space-y-3 pt-3 text-sm"; prop.children [
                            Html.p "Flatness: IEC / Elekta = 100 Dmax/Dmin; Varian / IAEA = 100 (Dmax−Dmin)/(Dmax+Dmin)."
                            Html.p "Symmetry: IEC / Elekta = 100 max(DL/DR, DR/DL); Varian = 100 max|DL−DR|/D(CAX); IAEA = 100 (AL−AR)/(AL+AR)."
                            Html.p "Point metrics: symmetric CAX region, limited by the nearer 50% peak-dose edge. IAEA area: CAX to each 50% CAX-dose edge. No smoothing."
                            Html.p "Convention names identify equations. Electron acceptance protocols also specify depth, reference dose and evaluation region; these settings do not select a complete acceptance protocol."
                            Html.p "Central mean uses the nearest N samples about CAX, with lower position breaking ties. Uncertainty scales by the fixed normalization factor. Raw dose is summed Gy."
                            Html.p "R50 is dose-based. Metrics use the full curve. Missing values are shown as —."
                            Html.a [prop.className "link"; prop.href "https://static.standardimaging.com/referenced-manuals/80614-11-DoseView-MNL.pdf#page=131"; prop.target "_blank"; prop.rel "noreferrer"; prop.text "DoseView · Table 16-2"]
                            Html.br []
                            Html.a [prop.className "link"; prop.href "https://international.anl.gov/training/materials/IAEA%20Publications/P1196_Handbook%20RADIATION%20ONCOLOGY%20PHYSICS.pdf"; prop.target "_blank"; prop.rel "noreferrer"; prop.text "IAEA handbook · §6.9"]]]]]
                ]]
        ]]
    ]]
