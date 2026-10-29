module AnalysisOperation

open System
open System.IO
open System.Globalization
open System.Text.Json
open Shared
open TsebtConfig
open FsToolkit.ErrorHandling

/// Resolves the output root used for analysis discovery.
let private root settings = Path.GetFullPath(Path.Combine(settings.AppRoot, settings.Paths.Outputs))

/// Enumerates only final collected uncertainty files, including historical layouts.
let private files settings =
    let folder = root settings
    if Directory.Exists folder then Directory.GetFiles(folder, "dose_with_uncertainty.csv", SearchOption.AllDirectories) |> Array.toList else []

/// Lists dataset identifiers relative to the configured output root.
let listDatasets settings =
    try
        files settings |> List.map (fun path ->
            let id = Path.GetRelativePath(root settings, path).Replace('\\','/')
            {Id=id; Label=id.Replace("/dose_with_uncertainty.csv", "")}) |> List.sortByDescending _.Id |> Ok
    with ex -> Error $"Cannot list analysis outputs: {ex.Message}"

/// Resolves an existing dataset by discovery rather than accepting arbitrary filesystem paths.
let private resolve settings id =
    files settings |> List.tryFind (fun path -> Path.GetRelativePath(root settings,path).Replace('\\','/') = id)
    |> Result.requireSome "Collected dataset no longer exists."

/// Reads a finite invariant numeric CSV value.
let private number (text: string) =
    let mutable value = 0.0
    if Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, &value) && Double.IsFinite value then value
    else failwith $"Invalid numeric CSV value: {text}"

/// Loads collected coordinates and uncertainties without silently projecting multidimensional scorers.
let loadCurve settings id =
    try
        result {
            let! path = resolve settings id
            let mappingPath = path + ".analysis.json"
            let mapping = if File.Exists mappingPath then Some(JsonSerializer.Deserialize<AnalysisMapping>(File.ReadAllText mappingPath)) else None
            let axis = mapping |> Option.map _.Axis |> Option.defaultValue 2
            if FileInfo(path).Length > 50000000L then
                return! Error "This dataset is too large for 1D analysis. Use a dedicated 1D scorer."
            let lines = File.ReadAllLines path
            let headers = lines[0].Split(',')
            /// Resolves a required collected CSV column.
            let index name = headers |> Array.findIndex ((=) name)
            let doseColumn, uncertaintyColumn = index "dose_to_medium_Gy", index "standard_uncertainty_Gy"
            let rows = lines |> Array.skip 1 |> Array.filter (String.IsNullOrWhiteSpace >> not) |> Array.map (fun line -> line.Split(','))
            let varyingAxes = [0..2] |> List.filter (fun a -> rows |> Array.map (fun row -> number row[a]) |> Array.distinct |> Array.length > 1)
            let selectedAxis = if mapping.IsNone && varyingAxes.Length = 1 then varyingAxes.Head else axis
            if varyingAxes |> List.exists ((<>) selectedAxis) then
                return! Error "This scorer has multiple varying axes. Extract a 1D scorer before analysis; no implicit averaging is performed."
            let points = rows |> Array.map (fun row -> {Position=number row[selectedAxis]; Dose=number row[doseColumn]; Uncertainty=number row[uncertaintyColumn]}) |> Array.sortBy _.Position |> Array.toList
            if points.Length < 2 || points |> List.exists (fun p -> p.Dose < 0.0 || p.Uncertainty < 0.0) then return! Error "Curve requires at least two non-negative dose samples."
            if (points |> List.map _.Position |> List.distinct |> List.length) <> points.Length then
                return! Error "Duplicate coordinates are not supported; extract a single curve first."
            let suggested = {Role=(if id.Contains("pdd") then "PDD" elif selectedAxis=0 then "X profile" else "Y profile"); Axis=selectedAxis; StepMm=2.0; OriginMm=(if id.Contains("pdd") then 1.0 else -299.0); CaxMm=0.0; Confirmed=false}
            let resolved,source =
                match mapping with
                | Some saved -> saved,"Saved explicit mapping: " + mappingPath
                | None ->
                    match AnalysisInput.infer settings id selectedAxis points.Length with
                    | Ok derived -> derived
                    | Error reason -> suggested,"Unconfirmed editable preset. Input detection: " + reason
            return {Dataset={Id=id;Label=id.Replace("/dose_with_uncertainty.csv", "")}; Mapping=Some resolved; MappingSource=source; Points=points}
        }
    with ex -> Error $"Cannot load curve: {ex.Message}"

/// Saves validated physical mapping metadata beside an immutable collected CSV.
let saveMapping settings (request: SaveAnalysisMapping) =
    try
        result {
            let! _ = Analysis.validateMapping request.Mapping
            let! path = resolve settings request.Id
            let destination = path + ".analysis.json"
            let temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp"
            File.WriteAllText(temporary, JsonSerializer.Serialize request.Mapping)
            File.Move(temporary, destination, true)
            return ()
        }
    with ex -> Error $"Cannot save analysis mapping: {ex.Message}"
