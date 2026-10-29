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

/// Resolves unrotated scorer dimensions and placement from one generated input.
let mappingFromText suffix axis count text : Result<AnalysisMapping,string> =
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
            if key.StartsWith("Sc/") && key.EndsWith("/OutputFile") &&
               suffix<>"default" && value.EndsWith("_"+suffix,StringComparison.Ordinal)
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
        let bins=number(scorer+dimension+"Bins")
        if bins<>float count then failwith "Input bin count differs from collected sample count."
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
            let runColumn=rows[0].Split('\t') |> Array.findIndex ((=) "runId")
            let runIds=rows |> Array.skip 1 |> Array.filter(String.IsNullOrWhiteSpace >> not) |> Array.map(fun line->line.Split('\t')[runColumn]) |> Array.distinct
            let results=runIds |> Array.map(fun runId ->
                let path=Path.Combine(settings.AppRoot,settings.Paths.Inputs,batch,runId+".txt")
                if not(File.Exists path) then Error $"Generated input missing: {path}"
                else mappingFromText suffix axis count (File.ReadAllText path))
            match results |> Array.tryPick(function Error e->Some e | _ ->None) with
            | Some message -> Error message
            | None ->
                let mappings=results |> Array.choose(function Ok m->Some m | _ ->None) |> Array.distinct
                if mappings.Length<>1 then Error "Contributing inputs have inconsistent scorer geometries."
                else Ok(mappings[0],$"Derived from inputs/{batch}: {runIds.Length} contributing generated inputs agree; scorer {suffix}.")
    with ex -> Error $"Cannot derive input geometry: {ex.Message}"
