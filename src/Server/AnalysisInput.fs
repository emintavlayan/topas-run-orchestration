module AnalysisInput

open System
open System.IO
open System.Globalization
open System.Text.RegularExpressions
open Shared
open TsebtConfig

/// Parses explicit TOPAS parameter assignments, retaining the last override.
let private parameters (text:string) =
    Regex.Matches(text, @"(?m)^\s*[a-z]+:([^=\s]+)\s*=\s*([^#\r\n]+)")
    |> Seq.cast<Match> |> Seq.map(fun m -> m.Groups[1].Value,m.Groups[2].Value.Trim().Trim('"')) |> Map.ofSeq

/// Resolves unrotated geometry for exactly one scorer selected by its output path.
let private mappingFromMatcher matchesOutput axis count text : Result<AnalysisMapping,string> =
    try
        let values=parameters text
        /// Retrieves a required TOPAS parameter.
        let get key = values |> Map.tryFind key |> Option.defaultWith(fun () -> failwith $"Missing parameter: {key}")
        /// Resolves a literal value or direct parameter reference.
        let rec numeric level key =
            if level>12 then failwith "Cyclic parameter reference."
            let value=get key
            if values.ContainsKey value then numeric (level+1) value else
            let m=Regex.Match(value,@"^([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)\s*(mm|cm|m|um|deg|rad)?$")
            if not m.Success then failwith $"Unsupported expression in {key}."
            let factor=match m.Groups[2].Value with | "cm" ->10. | "m" ->1000. | "um" ->0.001 | "rad" ->180./Math.PI | _ ->1.
            Double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)*factor
        /// Resolves one numeric parameter in millimetres or degrees.
        let number key=numeric 0 key
        /// Uses zero for omitted displacement and rotation parameters.
        let optional key=if values.ContainsKey key then number key else 0.
        let scorers=values |> Map.toList |> List.choose(fun (key,value)->
            if key.StartsWith("Sc/") && key.EndsWith("/OutputFile") && matchesOutput value
            then Some(key.Substring(0,key.Length-"OutputFile".Length)) else None)
        if scorers.Length<>1 then failwith "Cannot uniquely match this collected scorer to an input OutputFile."
        let scorer=scorers.Head
        if get(scorer+"Quantity")<>"DoseToMedium" && get(scorer+"Quantity")<>"DoseToWater" then failwith "Scorer is not absorbed dose."
        let scorerComponent=get(scorer+"Component")
        let ge="Ge/"+scorerComponent+"/"
        let shape=get(ge+"Type")
        if shape<>"TsCylinder" && shape<>"TsBox" then failwith "Unsupported scoring geometry."
        let cylinder=shape="TsCylinder"
        let dimension= [|"X";"Y";"Z"|][axis]
        if cylinder && axis<>2 then failwith "Radial/angular coordinates require a dedicated mapping."
        let half=number(ge+(if cylinder then "HL" else "HL"+dimension))
        let binKey=dimension+"Bins"
        let bins=
            if values.ContainsKey(scorer+binKey) then number(scorer+binKey)
            elif values.ContainsKey(ge+binKey) then number(ge+binKey)
            else 1.
        if half<=0. || bins<=0. then failwith "Scoring dimensions and bin count must be positive."
        if bins<>float count then
            failwith $"Scorer {scorerComponent}: input {binKey}={bins:g}, collected samples={count}. Checked scorer bins, component bins and the default of 1."
        let step=2.*half/bins
        /// Accumulates translations only for unrotated parent chains.
        let rec translation level name =
            if level>12 then failwith "Cyclic geometry parent."
            let prefix="Ge/"+name+"/"
            for rotation in ["RotX";"RotY";"RotZ"] do
                if abs(optional(prefix+rotation))>1e-9 then failwith "Rotated scorer ancestry needs explicit mapping."
            let local=optional(prefix+"Trans"+dimension)
            if name="World" then local else local+translation (level+1) (get(prefix+"Parent"))
        let centre=translation 0 scorerComponent
        let origin,role =
            if axis=2 then
                let parent=get(ge+"Parent")
                let pg="Ge/"+parent+"/"
                if get(pg+"Material")<>"G4_WATER" then failwith "PDD depth origin requires a direct water parent."
                let parentHalf=number(pg+(if get(pg+"Type")="TsCylinder" then "HL" else "HLZ"))
                centre-half+step/2.-(translation 0 parent-parentHalf),"PDD"
            else centre-half+step/2.,(if axis=0 then "X profile" else "Y profile")
        Ok {Role=role;Axis=axis;StepMm=step;OriginMm=origin;CaxMm=0.;Confirmed=true}
    with ex -> Error ex.Message

/// Resolves a suffixed scorer for historical manifests without source CSV paths.
let mappingFromText suffix axis count text =
    mappingFromMatcher (fun value -> suffix<>"default" && value.EndsWith("_"+suffix,StringComparison.Ordinal)) axis count text

/// Resolves the input scorer whose output corresponds exactly to a manifest source CSV.
let mappingFromCsvPath (csvPath:string) axis count text =
    /// Normalizes path separators without changing case-sensitive file identities.
    let normalize (path:string) = path.Replace('\\','/')
    let expected = normalize csvPath
    mappingFromMatcher (fun value -> normalize(value+".csv")=expected) axis count text

/// Derives a mapping from the inputs of all contributing retained runs and checks agreement.
let infer (settings:TsebtSettings) (id:string) axis count =
    try
        let parts=id.Replace('\\','/').Split('/')
        let batch=parts[0]
        let output=Path.Combine(settings.AppRoot,settings.Paths.Outputs,id)
        let folder=Path.GetDirectoryName output
        let suffix=Path.GetFileName folder
        let manifest=Path.Combine(folder,"collect_manifest.tsv")
        if not(File.Exists manifest) then Error "No scorer manifest; use explicit mapping for historical outputs."
        else
            let rows=File.ReadAllLines manifest
            let headers=rows[0].Split('\t')
            let runColumn=headers |> Array.findIndex ((=) "runId")
            let csvColumn=headers |> Array.tryFindIndex ((=) "csvPath")
            let scorerColumn=headers |> Array.tryFindIndex ((=) "scorer")
            let contributors=
                rows |> Array.skip 1 |> Array.filter(String.IsNullOrWhiteSpace >> not)
                |> Array.map(fun line -> line.Split('\t'))
                |> Array.filter(fun fields -> scorerColumn |> Option.forall (fun column -> String.Equals(fields[column],suffix,StringComparison.OrdinalIgnoreCase)))
                |> Array.map(fun fields -> fields[runColumn],csvColumn |> Option.map(fun column -> fields[column]))
                |> Array.distinct
            if contributors.Length=0 then failwith $"No manifest contributors for scorer {suffix}."
            let results=contributors |> Array.map(fun (runId,csvPath) ->
                let path=Path.Combine(settings.AppRoot,settings.Paths.Inputs,batch,runId+".txt")
                if not(File.Exists path) then Error $"Generated input missing: {path}"
                else
                    let text=File.ReadAllText path
                    (match csvPath with
                     | Some source -> mappingFromCsvPath source axis count text
                     | None -> mappingFromText suffix axis count text)
                    |> Result.mapError (fun reason -> $"{runId}.txt: {reason}"))
            match results |> Array.tryPick(function Error e->Some e | _ ->None) with
            | Some message -> Error message
            | None ->
                let mappings=results |> Array.choose(function Ok m->Some m | _ ->None) |> Array.distinct
                if mappings.Length<>1 then Error "Contributing inputs have inconsistent scorer geometries."
                else Ok(mappings[0],$"Derived from inputs/{batch}: {contributors.Length} contributing generated inputs agree; scorer {suffix}.")
    with ex -> Error $"Cannot derive input geometry: {ex.Message}"
