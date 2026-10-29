module AnalysisViews

open System
open Feliz
open Shared
open SAFE

/// Represents one selected curve and its editable physical mapping.
type private Selection = { Curve: AnalysisCurve; Mapping: AnalysisMapping }

/// Formats numbers consistently for scientific labels and export.
let private format (value:float) = value.ToString("G6", Globalization.CultureInfo.InvariantCulture)

/// Escapes CSV cells including dataset paths containing commas or quotes.
let private csvRow values = values |> List.map (fun (value:string) -> "\"" + value.Replace("\"", "\"\"") + "\"") |> String.concat ","

/// Renders a labelled choice field.
let private choice (label:string) (value:string) (options:string list) (changed:string -> unit) =
    Html.label [prop.className "form-control"; prop.children [
        Html.span label
        Html.select [prop.className "select select-bordered select-sm"; prop.value value; prop.onChange changed
                     prop.children [for option in options -> Html.option [prop.value option; prop.text option]]]]]

/// Renders a physical mapping numeric input.
let private numeric (label:string) value (changed:float -> unit) =
    Html.label [prop.className "form-control"; prop.children [Html.span label;
        Html.input [prop.className "input input-bordered input-sm w-36"; prop.type' "number"; prop.value (format value)
                    prop.onChange (fun (text:string) -> match Double.TryParse text with | true,v -> changed v | _ -> ())]]]

/// Maps stored bin coordinates onto a confirmed physical axis.
let private physical selected =
    selected.Curve.Points |> List.map (fun p -> {p with Position=selected.Mapping.OriginMm + selected.Mapping.StepMm*p.Position}) |> List.sortBy _.Position

/// Renders scientific line overlays with sample markers and optional scaled uncertainty bands.
let private plot (title:string) (unitName:string) bands (curves: (string * AnalysisPoint list) list) =
    let all = curves |> List.collect snd
    if all.IsEmpty then Html.p $"{title}: select and confirm compatible curves."
    else
        let xmin, xmax = all |> List.minBy _.Position |> _.Position, all |> List.maxBy _.Position |> _.Position
        let ymin = min 0.0 (all |> List.map (fun p -> p.Dose-p.Uncertainty) |> List.min)
        let ymax = max (ymin+1e-12) (all |> List.map (fun p -> p.Dose+(if bands then p.Uncertainty else 0.0)) |> List.max)
        /// Maps a physical position onto the plot axis.
        let x p = 65.0 + (p-xmin)/max 1e-12 (xmax-xmin)*790.0
        /// Maps a dose ordinate onto the plot axis.
        let y p = 290.0 - (p-ymin)/(ymax-ymin)*260.0
        let colors = [|"#2563eb";"#dc2626";"#059669";"#9333ea";"#d97706"|]
        Html.div [prop.className "card bg-base-100 p-4 border border-base-300 space-y-2"; prop.children [
            Html.h3 [prop.className "font-semibold";prop.text title]
            Svg.svg [svg.viewBox(0,0,900,330);svg.custom("role","img");svg.children [
                Svg.line [svg.x1 65;svg.y1 290;svg.x2 855;svg.y2 290;svg.stroke "currentColor"]
                Svg.line [svg.x1 65;svg.y1 30;svg.x2 65;svg.y2 290;svg.stroke "currentColor"]
                for i in 0..4 do
                    let xv = xmin+(xmax-xmin)*float i/4.0
                    let yv = ymin+(ymax-ymin)*float i/4.0
                    Svg.text [svg.x (x xv);svg.y 310;svg.fontSize 11;svg.fill "currentColor";svg.text (format xv)]
                    Svg.text [svg.x 2;svg.y (y yv);svg.fontSize 11;svg.fill "currentColor";svg.text (format yv)]
                Svg.text [svg.x 420;svg.y 328;svg.fontSize 12;svg.fill "currentColor";svg.text "Position / depth (mm)"]
                for i,(_,points) in List.indexed curves do
                    let color=colors[i%colors.Length]
                    if bands then
                        let outline = (points |> List.map (fun p -> x p.Position,y(p.Dose+p.Uncertainty))) @ (points |> List.rev |> List.map(fun p -> x p.Position,y(p.Dose-p.Uncertainty)))
                        Svg.polygon [svg.points (outline |> List.map(fun (a,b)-> $"{a},{b}") |> String.concat " ");svg.fill color;svg.fillOpacity 0.12]
                    Svg.polyline [svg.points (points |> List.map(fun p -> $"{x p.Position},{y p.Dose}") |> String.concat " ");svg.fill "none";svg.stroke color;svg.strokeWidth 1.7]
                    for p in points do
                        Svg.circle [svg.cx(x p.Position);svg.cy(y p.Dose);svg.r 2;svg.fill color
                                    svg.children [Svg.title $"{format p.Position} mm: {format p.Dose} {unitName}"]]
            ]]
            Html.p $"Y: {unitName}"
            for i,(label,_) in List.indexed curves do Html.p [prop.style [style.color colors[i%colors.Length]];prop.text label]
        ]]

/// Renders the analysis workspace with independent API state and persisted mappings.
[<ReactComponent>]
let view () =
    let api = React.useMemo((fun () -> Api.makeProxy<ITopasApi>()), [||])
    let datasets,setDatasets = React.useState<AnalysisDataset list>([])
    let selected,setSelected = React.useStateWithUpdater<Selection list>([])
    let error,setError = React.useState("")
    let busy,setBusy = React.useState(false)
    let normalization,setNormalization = React.useState("Maximum")
    let normPosition,setNormPosition = React.useState(0.0)
    let formula,setFormula = React.useState(fun () ->
        let stored=Browser.Dom.window.localStorage.getItem("topas.analysis.formula")
        if stored="Difference (ideal 0%)" then stored else "Ratio (ideal 100%)")
    let symmetryFormula,setSymmetryFormula = React.useState(fun () ->
        let stored=Browser.Dom.window.localStorage.getItem("topas.analysis.symmetry")
        if stored="Difference (ideal 0%)" then stored else "Ratio (ideal 100%)")
    let region,setRegion = React.useState(0.8)
    let bands,setBands = React.useState(true)
    let reference,setReference = React.useState("")
    let xmin,setXmin = React.useState(-1000.0)
    let xmax,setXmax = React.useState(1000.0)
    /// Executes an API operation and reports failures in the workspace.
    let run operation =
        setBusy true
        async {
            try do! operation
            with ex -> setError ex.Message
            setBusy false
        } |> Async.StartImmediate
    /// Loads the available collected datasets.
    let refresh () = run (async {
        let! result = api.listAnalysisDatasets()
        match result with | Ok values -> setDatasets values; setError "" | Error message -> setError message })
    React.useEffectOnce refresh
    /// Updates one selected physical mapping without mutating the collected data.
    let update id change = setSelected(fun values -> values |> List.map(fun s -> if s.Curve.Dataset.Id=id then {s with Mapping=change s.Mapping} else s))
    /// Adds or removes one collected curve.
    let toggle (dataset:AnalysisDataset) =
        if selected |> List.exists(fun s -> s.Curve.Dataset.Id=dataset.Id) then setSelected(fun values -> values |> List.filter(fun s -> s.Curve.Dataset.Id<>dataset.Id))
        else run (async {
            let! result = api.loadAnalysisCurve dataset.Id
            match result with
            | Error message -> setError message
            | Ok curve ->
                let mapping = curve.Mapping |> Option.defaultValue {Role="PDD";Axis=2;StepMm=1.0;OriginMm=0.0;CaxMm=0.0;Confirmed=false}
                setSelected(fun values -> values @ [{Curve=curve;Mapping=mapping}]); setError "" })
    let processed = selected |> List.choose(fun s ->
        match Analysis.validateMapping s.Mapping with
        | Error _ -> None
        | Ok _ ->
            match Analysis.normalize normalization (if normalization="CAX" then s.Mapping.CaxMm else normPosition) (physical s) with
            | Ok points -> Some(s,points)
            | Error _ -> None)
    let referenceCurve = processed |> List.tryFind(fun (s,_) -> s.Curve.Dataset.Id=reference)
    let ratio = formula.StartsWith("Ratio")
    let symmetryRatio = symmetryFormula.StartsWith("Ratio")
    let export =
        [yield "dataset,role,position_mm,dose,scaled_absolute_uncertainty,normalization"
         for s,points in processed do
            for p in points do yield csvRow [s.Curve.Dataset.Label;s.Mapping.Role;format p.Position;format p.Dose;format p.Uncertainty;normalization]] |> String.concat "\n"
    let metricExport =
        [yield "dataset,metric,value,flatness_definition,symmetry_definition,evaluation_fraction,CAX_mm"
         for s,_ in processed do
             for name,value in Analysis.metricsWithDefinitions s.Mapping.Role ratio symmetryRatio region s.Mapping.CaxMm (physical s) do
                 let text=value |> Option.map format |> Option.defaultValue ""
                 yield csvRow [s.Curve.Dataset.Label;name;text;formula;symmetryFormula;format region;format s.Mapping.CaxMm]] |> String.concat "\n"
    Html.div [prop.className "space-y-4";prop.children [
        Html.h2 [prop.className "text-xl font-semibold";prop.text "Analyse collected dose curves"]
        Html.p "Select outputs. Geometry is resolved from the matching generated inputs and checked across contributing runs. Unresolved geometry uses an editable, unconfirmed preset; confirm and save an explicit mapping to enable analysis."
        Html.button [prop.className "btn btn-sm";prop.disabled busy;prop.text "Refresh outputs";prop.onClick(fun _ -> refresh())]
        if error<>"" then Html.div [prop.className "alert alert-error";prop.text error]
        if busy then Html.p "Loading…"
        Html.div [prop.className "max-h-64 overflow-auto bg-base-100 p-3";prop.children [
            for dataset in datasets do
                Html.label [prop.className "flex gap-3 py-1";prop.children [
                    Html.input [prop.type' "checkbox";prop.disabled busy;prop.isChecked(selected |> List.exists(fun s -> s.Curve.Dataset.Id=dataset.Id));prop.onChange(fun (_:bool)->toggle dataset)]
                    Html.span dataset.Label]]]]
        for s in selected do
            let id=s.Curve.Dataset.Id
            Html.div [prop.className "card bg-base-100 p-4 space-y-2 border border-base-300";prop.children [
                Html.h3 s.Curve.Dataset.Label
                Html.p s.Curve.MappingSource
                Html.div [prop.className "flex flex-wrap gap-3";prop.children [
                    choice "Role" s.Mapping.Role ["PDD";"X profile";"Y profile"] (fun v->update id (fun m->{m with Role=v;Confirmed=false}))
                    numeric "mm per stored coordinate" s.Mapping.StepMm (fun v->update id (fun m->{m with StepMm=v;Confirmed=false}))
                    numeric "Position at coordinate 0 (mm)" s.Mapping.OriginMm (fun v->update id (fun m->{m with OriginMm=v;Confirmed=false}))
                    numeric "CAX (mm)" s.Mapping.CaxMm (fun v->update id (fun m->{m with CaxMm=v;Confirmed=false}))]]
                Html.p $"Sample count: {s.Curve.Points.Length}; stored range: {format s.Curve.Points.Head.Position} to {format (List.last s.Curve.Points).Position}; axis: {s.Mapping.Axis} (0=X, 1=Y, 2=Z)"
                Html.button [prop.className "btn btn-sm btn-primary";prop.disabled busy;prop.text(if s.Mapping.Confirmed then "Save mapping / override" else "Confirm physical mapping and save")
                             prop.onClick(fun _ -> run (async {
                                 let mapping={s.Mapping with Confirmed=true}
                                 let! result=api.saveAnalysisMapping {Id=id;Mapping=mapping}
                                 match result with | Ok () -> update id (fun _ ->mapping);setError "" | Error message -> setError message }))]
                if s.Mapping.Confirmed then
                    match Analysis.normalize normalization (if normalization="CAX" then s.Mapping.CaxMm else normPosition) (physical s) with
                    | Error message -> Html.p [prop.className "text-error";prop.text message]
                    | _ -> Html.none
            ]]
        Html.div [prop.className "flex flex-wrap gap-4";prop.children [
            choice "Normalization" normalization ["Maximum";"CAX";"At position";"None"] setNormalization
            numeric "Normalization position/depth (mm)" normPosition setNormPosition
            choice "Flatness (saved default)" formula ["Ratio (ideal 100%)";"Difference (ideal 0%)"] (fun value -> Browser.Dom.window.localStorage.setItem("topas.analysis.formula",value);setFormula value)
            choice "Symmetry (saved default)" symmetryFormula ["Ratio (ideal 100%)";"Difference (ideal 0%)"] (fun value -> Browser.Dom.window.localStorage.setItem("topas.analysis.symmetry",value);setSymmetryFormula value)
            numeric "Evaluation fraction of FWHM (0–1)" region (fun v -> if v>0.0 && v<=1.0 then setRegion v)
            numeric "Plot minimum (mm)" xmin setXmin; numeric "Plot maximum (mm)" xmax setXmax
            choice "Reference for differences" reference ("" :: (processed |> List.map(fun (s,_) -> s.Curve.Dataset.Id))) setReference
            Html.label [prop.children [Html.input [prop.type' "checkbox";prop.isChecked bands;prop.onChange setBands];Html.span " Show scaled absolute uncertainty"]]]]
        Html.p (if ratio then "Flatness = 100 × Dmax/Dmin. Ideal: 100%." else "Flatness = 100 × (Dmax−Dmin)/(Dmax+Dmin). Ideal: 0%.")
        Html.p (if symmetryRatio then "Symmetry = 100 × max(D(x)/D(2CAX−x), inverse). Ideal: 100%." else "Symmetry = 100 × max|D(x)−D(2CAX−x)|/D(CAX). Ideal: 0%.")
        Html.p "Evaluation uses the symmetric region about the specified CAX, limited by the nearer 50% field edge. No smoothing or extrapolation. Ranges are distal crossings relative to each curve maximum. R50 is dose-based, not I50. Scaled uncertainty bands treat normalization as fixed; metric and normalization covariance uncertainties are not calculated. Unnormalized dose is summed Gy, not Gy/MU."
        for role in ["PDD";"X profile";"Y profile"] do
            let curves = processed |> List.filter(fun (s,_) -> s.Mapping.Role=role)
            if not curves.IsEmpty then
                plot role (if normalization="None" then "Gy" else "%") bands (curves |> List.map(fun (s,p)->s.Curve.Dataset.Label,p |> List.filter(fun p->p.Position>=xmin && p.Position<=xmax)))
                Html.table [prop.className "table table-sm bg-base-100";prop.children [
                    Html.thead [Html.tr [Html.th "Dataset";Html.th "Metric";Html.th "Value"]]
                    Html.tbody [
                        for s,_ in curves do
                          for name,value in Analysis.metricsWithDefinitions role ratio symmetryRatio region s.Mapping.CaxMm (physical s) do
                              Html.tr [Html.td s.Curve.Dataset.Label;Html.td name;Html.td(value |> Option.map format |> Option.defaultValue "Unavailable")]]]]
                match referenceCurve with
                | Some(r,rp) when r.Mapping.Role=role ->
                    let differences = curves |> List.filter(fun (s,_)->s.Curve.Dataset.Id<>reference) |> List.map(fun (s,points)->
                        s.Curve.Dataset.Label, points |> List.choose(fun p -> Analysis.interpolate rp p.Position |> Option.map(fun baseline->{p with Dose=p.Dose-baseline;Uncertainty=0.0})) |> List.filter(fun p->p.Position>=xmin && p.Position<=xmax))
                    plot (role+" difference from reference") (if normalization="None" then "Gy" else "percentage points") false differences
                | _ -> Html.none
        Html.a [prop.className "btn btn-outline btn-sm";prop.download "analysis-curves.csv";prop.href("data:text/csv;charset=utf-8,"+Uri.EscapeDataString export);prop.text "Export analysed curves CSV"]
        Html.a [prop.className "btn btn-outline btn-sm ml-2";prop.download "analysis-metrics.csv";prop.href("data:text/csv;charset=utf-8,"+Uri.EscapeDataString metricExport);prop.text "Export metrics CSV"]
    ]]
