module ImportViews

open System
open Feliz
open Fable.Core
open Shared
open SAFE

/// Reads local file bytes without interpreting vendor formats in the browser.
[<Import("readFiles", "./ImportFiles.js")>]
let private readFiles (files: Browser.Types.FileList) : JS.Promise<ImportFile array> = jsNative

/// Renders a labelled editable metadata field.
let private field (label: string) (value: string) (changed: string -> unit) =
    Html.label [prop.className "analysis-field";prop.children [
        Html.span label
        Html.input [prop.className "input input-bordered input-sm w-full";prop.value value;prop.onChange changed]]]

/// Renders a labelled selection with an explicit unknown option.
let private choice (label: string) (value: string) (options: (string * string) list) (changed: string -> unit) =
    Html.label [prop.className "analysis-field";prop.children [
        Html.span label
        Html.select [prop.className "select select-bordered select-sm w-full";prop.value value;prop.onChange changed
                     prop.children [for key,text in options -> Html.option [prop.value key;prop.text text]]]]]

/// Renders a disabled-aware action button.
let private button (label: string) (busy: bool) (action: unit -> unit) =
    Html.button [prop.className "btn btn-outline btn-sm";prop.disabled busy;prop.text label;prop.onClick (fun _ -> action())]

/// Renders dataset import, review and management using the common curve model.
[<ReactComponent>]
let View () =
    let api = React.useMemo((fun () -> Api.makeProxy<ITopasApi>()),[||])
    let datasets,setDatasets = React.useState<SavedDatasetSummary list>([])
    let collections,setCollections = React.useState<AnalysisDataset list>([])
    let preview,setPreview = React.useState<ImportPreview option>(None)
    let editing,setEditing = React.useState<SavedDatasetSummary option>(None)
    let curves,setCurves = React.useStateWithUpdater<DatasetCurve list>([])
    let included,setIncluded = React.useStateWithUpdater<string list>([])
    let name,setName = React.useState("")
    let source,setSource = React.useState("Measurement")
    let acquired,setAcquired = React.useState("")
    let confirmed,setConfirmed = React.useState(false)
    let active,setActive = React.useState("")
    let error,setError = React.useState("")
    let notice,setNotice = React.useState("")
    let busy,setBusy = React.useState(false)
    let deleting,setDeleting = React.useState("")
    let original,setOriginal = React.useState<ImportFile option>(None)
    /// Executes a UI operation while retaining review state on failure.
    let run operation =
        setBusy true
        setError ""
        setNotice ""
        async {
            try do! operation with ex -> setError ex.Message
            setBusy false
        } |> Async.StartImmediate
    /// Refreshes saved versions and existing collected output choices.
    let refresh () = async {
        let! saved = api.getSavedDatasets()
        match saved with Ok items -> setDatasets items | Error message -> setError message
        let! found = api.listCollectedDatasets()
        match found with Ok items -> setCollections items | Error message -> setError message
    }
    React.useEffectOnce (fun () -> run (refresh()))
    /// Clears completed review state without discarding saved datasets.
    let clearReview () =
        setPreview None
        setEditing None
        setCurves (fun _ -> [])
        setIncluded (fun _ -> [])
        setOriginal None
        setConfirmed false
        setActive ""
    /// Loads several local files and asks the server to detect individual scans.
    let chooseFiles (files: Browser.Types.FileList) = run (async {
        let! rawFiles = readFiles files |> Async.AwaitPromise
        let uploaded = rawFiles |> Array.map (fun file -> {Name=file.Name;ContentBase64=file.ContentBase64})
        let! parsed = api.previewImport (uploaded |> Array.toList)
        match parsed with
        | Error message -> setError message
        | Ok value ->
            clearReview()
            setPreview (Some value)
            setCurves (fun _ -> value.Curves)
            setIncluded (fun _ -> value.Curves |> List.map _.Id)
            setName (uploaded |> Array.tryHead |> Option.map (fun file -> file.Name) |> Option.defaultValue "")
            setSource "Measurement"
            setAcquired ""
            setActive value.Curves.Head.Id
    })
    /// Loads a saved version for metadata editing while retaining its original samples.
    let edit id = run (async {
        let! loaded = api.getSavedDataset id
        match loaded with
        | Error message -> setError message
        | Ok value ->
            clearReview()
            setEditing (Some value.Summary)
            setName value.Summary.Name
            setSource value.Summary.Source
            setAcquired value.Summary.AcquisitionDate
            setCurves (fun _ -> value.Curves)
            setIncluded (fun _ -> value.Curves |> List.map _.Id)
            setActive value.Curves.Head.Id
    })
    /// Updates one reviewed curve without changing parser-owned samples or provenance.
    let changeCurve id change =
        setConfirmed false
        setCurves (fun items -> items |> List.map (fun c -> if c.Id=id then {c with Metadata=change c.Metadata} else c))
    /// Saves a new reviewed version or a revision-checked metadata edit.
    let save () = run (async {
        let! saved =
            match editing,preview with
            | Some dataset,_ -> api.updateSavedDataset {
                Id=dataset.Id;Revision=dataset.Revision;Name=name;Source=source;AcquisitionDate=acquired
                Curves=curves |> List.map (fun c -> c.Id,c.Metadata) }
            | None,Some parsed -> async {
                let! result = api.saveImportedDataset {Name=name;Source=source;AcquisitionDate=acquired;Confirmed=confirmed
                                                      Preview={parsed with Curves=curves |> List.filter (fun c -> List.contains c.Id included) |> List.map (fun c -> {c with Points=[]})}}
                return result |> Result.map ignore }
            | _ -> async { return Error "Choose files first." }
        match saved with
        | Error message -> setError message
        | Ok () -> clearReview(); do! refresh(); setNotice "Dataset saved. Select it in Analyse."
    })
    /// Registers an existing collection once without replacing previously saved curves.
    let importCollection id = run (async {
        let! saved = api.importCollectedDataset id
        match saved with
        | Error message -> setError message
        | Ok _ -> do! refresh(); setNotice "Collection registered. Select it in Analyse."
    })
    /// Deletes only the explicitly confirmed saved dataset version.
    let delete (dataset: SavedDatasetSummary) = run (async {
        let! deleted = api.deleteSavedDataset {Id=dataset.Id;Revision=dataset.Revision}
        match deleted with
        | Error message -> setError message
        | Ok () ->
            if editing |> Option.exists (fun value -> value.Id=dataset.Id) then clearReview()
            setDeleting ""
            do! refresh()
            setNotice "Saved dataset deleted."
    })
    /// Retrieves an archived file and exposes its exact bytes for download.
    let downloadOriginal datasetId fileIndex = run (async {
        let! result = api.getDatasetOriginal {DatasetId=datasetId;FileIndex=fileIndex}
        match result with Ok file -> setOriginal (Some file) | Error message -> setError message
    })
    let reviewed = curves |> List.filter (fun c -> List.contains c.Id included)
    let issues = reviewed |> List.choose (fun c ->
        match Datasets.validateMetadata (editing.IsSome && c.Metadata.PositionUnit="index") c.Metadata with
        | Error message -> Some (c.Metadata.Name+": "+message)
        | Ok () -> None)
    let headerValid = not (String.IsNullOrWhiteSpace name) && Result.isOk(Datasets.validateDate acquired)
    let reviewOpen = preview.IsSome || editing.IsSome
    Html.div [prop.className "space-y-4";prop.children [
        Html.section [prop.className "rounded-box border border-base-300 bg-base-100 p-4 space-y-3";prop.children [
            Html.h2 [prop.className "font-semibold";prop.text "Import"]
            Html.p [prop.className "text-sm text-base-content/70";prop.text "Choose files → Review curves and metadata → Import. Values must represent dose; ionization-to-dose conversion is not performed."]
            Html.input [prop.type' "file";prop.multiple true;prop.disabled busy
                        prop.className "file-input file-input-bordered file-input-sm w-full max-w-xl"
                        prop.onChange (fun (event: Browser.Types.Event) -> chooseFiles (event.target :?> Browser.Types.HTMLInputElement).files)]
            Html.p [prop.className "text-xs text-base-content/60";prop.text "Dose text/CSV: position,value[,absolute uncertainty]. Headers can declare mm/cm and Gy/cGy/relative units. MCC scan blocks require explicit unit review. SNC vendor layouts await validation against actual examples. Maximum 20 files / 10 MB."]]]
        if error<>"" then Html.div [prop.className "alert alert-error text-sm";prop.custom ("role","alert");prop.text error]
        if notice<>"" then Html.div [prop.className "alert text-sm";prop.custom ("role","status");prop.text notice]
        if busy then Html.p [prop.className "text-sm";prop.text "Working…"]
        if reviewOpen then
            Html.section [prop.className "rounded-box border border-base-300 bg-base-100 p-4 space-y-4";prop.children [
                Html.h3 [prop.className "font-semibold";prop.text (if editing.IsSome then "Edit dataset metadata" else "Review detected curves")]
                Html.div [prop.className "grid gap-3 md:grid-cols-3";prop.children [
                    field "Dataset name" name (fun value -> setName value;setConfirmed false)
                    choice "Source" source ["Measurement","Measurement";"Simulation","Simulation"] (fun value -> setSource value;setConfirmed false)
                    field "Acquisition date (ISO; blank = unknown)" acquired (fun value -> setAcquired value;setConfirmed false)]]
                for curve in curves do
                    let m=curve.Metadata
                    let picked=List.contains curve.Id included
                    Html.details [prop.className "border border-base-300 rounded-lg p-3";prop.children [
                        Html.summary [prop.className "flex items-center gap-3 cursor-pointer";prop.children [
                            if editing.IsNone then
                                Html.input [prop.type' "checkbox";prop.isChecked picked;prop.disabled busy;prop.className "checkbox checkbox-xs"
                                            prop.onClick (fun event -> event.stopPropagation())
                                            prop.onChange (fun (value: bool) -> setConfirmed false;setIncluded (fun ids -> if value then curve.Id::ids else ids |> List.filter ((<>) curve.Id)))]
                            Html.span $"{m.Name} · {curve.Points.Length} samples"
                            Html.span [prop.className "text-xs text-base-content/60";prop.text (if m.Role="" || m.PositionUnit="" || m.ValueUnit="" then "Metadata required" else m.Role)]]]
                        Html.div [prop.className "grid gap-3 md:grid-cols-3 pt-3";prop.children [
                            field "Curve name" m.Name (fun value -> changeCurve curve.Id (fun metadata -> {metadata with Name=value}))
                            choice "Role" m.Role ["","Unknown — choose";"X profile","X profile";"Y profile","Y profile";"PDD","PDD"] (fun value -> changeCurve curve.Id (fun metadata -> {metadata with Role=value}))
                            choice "Original coordinate unit" m.PositionUnit
                                ((if m.PositionUnit="index" then ["index","TOPAS bin index"] else ["","Unknown — choose";"mm","mm";"cm","cm";"m","m"]))
                                (fun value -> changeCurve curve.Id (fun metadata -> {metadata with PositionUnit=value}))
                            choice "Original dose unit" m.ValueUnit ["","Unknown — choose";"Gy","Gy";"cGy","cGy";"%","%";"relative","Relative dose"] (fun value -> changeCurve curve.Id (fun metadata -> {metadata with ValueUnit=value}))
                            field "Depth [mm] (blank = unknown)" m.DepthMm (fun value -> changeCurve curve.Id (fun metadata -> {metadata with DepthMm=value}))
                            field "SSD [mm] (blank = unknown)" m.SsdMm (fun value -> changeCurve curve.Id (fun metadata -> {metadata with SsdMm=value}))
                            field "Field X [mm] (blank = unknown)" m.FieldXmm (fun value -> changeCurve curve.Id (fun metadata -> {metadata with FieldXmm=value}))
                            field "Field Y [mm] (blank = unknown)" m.FieldYmm (fun value -> changeCurve curve.Id (fun metadata -> {metadata with FieldYmm=value}))
                            field "Scan acquisition date (ISO; blank = unknown)" m.AcquisitionDate (fun value -> changeCurve curve.Id (fun metadata -> {metadata with AcquisitionDate=value}))]]
                        Html.p [prop.className "text-xs py-2 text-base-content/60";prop.text (curve.Provenance.OriginalFile+" · scan "+curve.Provenance.ScanId+" · parser "+curve.Provenance.ParserVersion+" · uncertainty: "+(if curve.HasUncertainty then "supplied" else "not supplied"))]
                        Html.div [prop.className "flex gap-2";prop.children [
                            button "Preview raw samples" busy (fun () -> setActive curve.Id)
                            match editing with
                            | Some dataset -> button "Retrieve original" busy (fun () -> downloadOriginal dataset.Id curve.Provenance.FileIndex)
                            | None -> Html.none]]
                        if not curve.Provenance.Corrections.IsEmpty then
                            Html.ul [prop.className "text-xs list-disc pl-5 pt-2";prop.children [for correction in curve.Provenance.Corrections -> Html.li correction]]]]
                match curves |> List.tryFind (fun c -> c.Id=active) with
                | Some curve ->
                    Html.div [prop.style [style.height 360];prop.children [
                        AnalysisPlot.view "Import preview" curve.Id ("Original position ["+(if curve.Metadata.PositionUnit="" then "unknown" else curve.Metadata.PositionUnit)+"]")
                            ("Original dose ["+(if curve.Metadata.ValueUnit="" then "unknown" else curve.Metadata.ValueUnit)+"]") false true
                            [{Id=curve.Id;Label=curve.Metadata.Name;Role=curve.Metadata.Role;HasUncertainty=curve.HasUncertainty;Points=curve.Points}]]]
                | None -> Html.none
                match original with
                | Some file -> Html.a [prop.className "btn btn-outline btn-sm";prop.download (file.Name.Replace('\\','/').Split('/') |> Array.last)
                                      prop.href ("data:application/octet-stream;base64,"+file.ContentBase64);prop.text ("Download "+file.Name)]
                | None -> Html.none
                if not issues.IsEmpty then Html.ul [prop.className "text-sm text-warning";prop.children [for issue in issues -> Html.li issue]]
                Html.label [prop.className "flex gap-2 items-center text-sm";prop.children [
                    Html.input [prop.type' "checkbox";prop.className "checkbox checkbox-sm";prop.isChecked confirmed;prop.onChange setConfirmed]
                    Html.span "I confirm the scan roles, units and dose quantity. Blank geometry/date fields remain unknown."]]
                Html.div [prop.className "flex gap-2";prop.children [
                    button (if editing.IsSome then "Save metadata" else "Import selected scans") (busy || not confirmed || not headerValid || reviewed.IsEmpty || not issues.IsEmpty) save
                    button "Cancel review" busy clearReview]]]]
        Html.section [prop.className "rounded-box border border-base-300 bg-base-100 p-4 space-y-3";prop.children [
            Html.h3 [prop.className "font-semibold";prop.text "Saved datasets"]
            button "Refresh" busy (fun () -> run (refresh()))
            if datasets.IsEmpty then Html.p [prop.className "text-sm text-base-content/60";prop.text "No saved datasets. Import files or register an existing collection below."]
            for dataset in datasets do
                Html.div [prop.className "flex flex-wrap items-center gap-3 border-t border-base-300 py-3";prop.children [
                    Html.div [prop.className "flex-1 min-w-48";prop.children [
                        Html.div [prop.className "font-medium text-sm";prop.text dataset.Name]
                        Html.div [prop.className "text-xs text-base-content/60";prop.text ($"{dataset.Source} · {dataset.Curves.Length} curves · saved {dataset.SavedAt} · acquisition " + (if dataset.AcquisitionDate="" then "unknown" else dataset.AcquisitionDate))]
                        if dataset.CollectionKey<>"" then Html.div [prop.className "text-xs";prop.text dataset.CollectionKey]]]
                    button "Edit / originals" busy (fun () -> edit dataset.Id)
                    if deleting=dataset.Id then
                        Html.span "Delete this saved version and its originals?"
                        button "Confirm delete" busy (fun () -> delete dataset)
                        button "Cancel" busy (fun () -> setDeleting "")
                    else button "Delete" busy (fun () -> setDeleting dataset.Id)]]]]
        Html.details [prop.className "rounded-box border border-base-300 bg-base-100 p-4";prop.children [
            Html.summary [prop.className "font-semibold cursor-pointer";prop.text "Existing simulation collections"]
            Html.div [prop.className "space-y-2 pt-3";prop.children [
                Html.p [prop.className "text-sm text-base-content/60";prop.text "Register a historical collection once. New Collect operations register automatically. Unresolved simulation geometry can be confirmed in Analyse → Settings."]
                for collection in collections do
                    let saved=datasets |> List.exists (fun d -> d.CollectionKey=collection.Id)
                    Html.div [prop.className "flex items-center gap-3";prop.children [
                        Html.span [prop.className "text-sm flex-1";prop.text collection.Label]
                        button (if saved then "Registered" else "Register collection") (busy || saved) (fun () -> importCollection collection.Id)]]]]]]
    ]]
