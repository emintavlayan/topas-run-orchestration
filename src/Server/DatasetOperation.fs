module DatasetOperation

open System
open System.IO
open Shared
open TsebtConfig
open FsToolkit.ErrorHandling

/// Checks dataset identity fields without requiring an invented acquisition date.
let private validateHeader name source acquired = result {
    do! Result.requireTrue "Enter a dataset name." (not (String.IsNullOrWhiteSpace name))
    do! Result.requireTrue "Choose Measurement or Simulation." (List.contains source ["Measurement";"Simulation"])
    do! Datasets.validateDate acquired
}

/// Reparses original bytes and saves only reviewed scans with server-owned provenance.
let import settings (request: SaveDatasetRequest) = result {
    do! validateHeader request.Name request.Source request.AcquisitionDate
    do! Result.requireTrue "Confirm scan roles, units and that the values represent dose." request.Confirmed
    let! parsed = CurveImport.preview request.Preview.Files
    let detected = parsed.Curves |> List.map (fun c -> c.Id,c) |> Map.ofList
    let reviewed = request.Preview.Curves
    do! Result.requireTrue "Select at least one scan." (not reviewed.IsEmpty)
    let uniqueOriginalScans =
        (reviewed |> List.map _.Id |> List.distinct |> List.length)=reviewed.Length
        && (reviewed |> List.forall (fun c -> detected.ContainsKey c.Id))
    do! Result.requireTrue "Each reviewed scan must identify a unique original scan." uniqueOriginalScans
    let! curves = reviewed |> List.traverseResultM (fun curve -> result {
        do! Datasets.validateMetadata false curve.Metadata
        let original = detected[curve.Id]
        return {original with Metadata=curve.Metadata;Mapping=Datasets.mapping curve.Metadata;MappingSource="Reviewed import: physical coordinates in mm."
                              Provenance={original.Provenance with Corrections=Datasets.corrections original.Metadata curve.Metadata}}
    })
    let! originals = CurveImport.decode parsed.Files
    return! DatasetStore.create settings request.Name request.Source request.AcquisitionDate "" originals curves
}

/// Validates and persists metadata edits using optimistic concurrency.
let update settings (request: UpdateDatasetRequest) = result {
    do! validateHeader request.Name request.Source request.AcquisitionDate
    return! DatasetStore.update settings request
}

/// Groups legacy scorer files into exact collection versions without path guessing.
let listCollections settings =
    AnalysisOperation.listDatasets settings |> Result.map (fun datasets ->
        datasets |> List.groupBy (fun dataset ->
            let parts = dataset.Id.Split('/')
            if parts.Length>=4 then String.concat "/" parts[..1] else parts[0])
        |> List.map (fun (id,_) -> {Id=id;Label=id}) |> List.sortByDescending _.Id)

/// Snapshots one existing collection with its original CSV files and resolved geometry.
let registerCollection settings key = result {
    let! existing = DatasetStore.list settings
    match existing |> List.tryFind (fun d -> d.CollectionKey=key) with
    | Some summary -> return summary
    | None ->
        let! available = listCollections settings
        do! Result.requireTrue "Select a discovered collection." (available |> List.exists (fun d -> d.Id=key))
        let! discovered = AnalysisOperation.listDatasets settings
        let selected = discovered |> List.filter (fun d ->
            let parts=d.Id.Split('/')
            let collection=if parts.Length>=4 then String.concat "/" parts[..1] else parts[0]
            collection=key)
        let! loaded = selected |> List.traverseResultM (fun dataset -> result {
            let! curve = AnalysisOperation.loadCurve settings dataset.Id
            let! mapping = curve.Mapping |> Result.requireSome "Collected curve has no coordinate mapping."
            let path = Path.Combine(settings.AppRoot,settings.Paths.Outputs,dataset.Id)
            return dataset,curve,mapping,path
        })
        let originalFiles = loaded |> List.collect (fun (_,_,_,path) ->
            [yield path
             let manifest=Path.Combine(Path.GetDirectoryName path,"collect_manifest.tsv")
             if File.Exists manifest then yield manifest
             if File.Exists(path+".analysis.json") then yield path+".analysis.json"])
        let originals = originalFiles |> List.map (fun path ->
            let name=Path.GetRelativePath(Path.Combine(settings.AppRoot,settings.Paths.Outputs),path).Replace('\\','/')
            {Name=name;ContentBase64=""},File.ReadAllBytes path)
        let curves = loaded |> List.map (fun (dataset,curve,mapping,path) ->
            let metadata={Datasets.emptyMetadata (Path.GetFileName(Path.GetDirectoryName path)) with Role=mapping.Role;PositionUnit="index";ValueUnit="Gy"}
            {Id="";Metadata=metadata;Mapping=mapping;MappingSource=curve.MappingSource;HasUncertainty=true;Points=curve.Points
             Provenance={FileIndex=originalFiles |> List.findIndex ((=) path);OriginalFile=dataset.Id;ScanId=metadata.Name;ParserVersion="topas-collected/1";Corrections=[]}})
        do! Result.requireTrue "No 1D curves were found in this collection." (not curves.IsEmpty)
        return! DatasetStore.create settings key "Simulation" "" key originals curves
}

/// Registers a newly completed collection using its exact version folder.
let registerCollectedResult settings (collected: CollectResult) =
    let key=Path.GetRelativePath(Path.Combine(settings.AppRoot,settings.Paths.Outputs),collected.OutputFolder).Replace('\\','/')
    try registerCollection settings key with ex -> Error $"Cannot register collection: {ex.Message}"

/// Lists the common saved curve identifiers used by Analyse.
let listAnalysisDatasets settings =
    DatasetStore.list settings |> Result.map (List.collect (fun dataset -> dataset.Curves |> List.map (fun c -> {c with Label=dataset.Name+" · "+c.Label})))
