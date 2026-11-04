module TopasScene

open System
open System.Globalization
open System.Text.RegularExpressions
open FsToolkit.ErrorHandling
open Shared

/// Removes comments while preserving hash characters inside quoted paths.
let private uncomment (line: string) =
    let mutable quoted = false

    line
    |> Seq.takeWhile (fun c ->
        if c = '"' then
            quoted <- not quoted

        c <> '#' || quoted)
    |> Seq.toArray
    |> String

/// Reads generated assignments in order and rejects unresolved external includes.
let private parameters (text: string) =
    let lines = text.Split('\n') |> Array.map uncomment

    if lines |> Array.exists (fun line -> Regex.IsMatch(line, @"^\s*includeFile\s*=")) then
        Error "This viewer requires a generated input with includes already resolved."
    else
        lines
        |> Array.choose (fun line ->
            let m =
                Regex.Match(line, @"^\s*(?:[a-zA-Z]+:)?((?:Ge|Sc|So)/[^=\s]+)\s*=\s*(.*?)\s*$")

            if m.Success then
                Some(m.Groups[1].Value, m.Groups[2].Value.Trim().Trim('"'))
            else
                None)
        |> Map.ofArray
        |> Ok

/// Reads one required parameter without inventing a value.
let private required (values: Map<string, string>) key =
    Map.tryFind key values |> Result.requireSome $"Missing parameter: {key}"

/// Represents the expected dimension of a numeric geometry parameter.
type private NumberKind =
    | Length
    | Angle
    | Count

/// Resolves finite literals and direct references with dimensional unit checking.
let private number values kind key =
    /// Resolves a parameter reference while detecting cycles.
    let rec resolve seen name = result {
        if Set.contains name seen then
            return! Error $"Cyclic parameter reference: {name}"

        let! value = required values name

        if Map.containsKey value values then
            return! resolve (Set.add name seen) value
        else
            let m =
                Regex.Match(value, @"^([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)\s*([a-zA-Z]*)$")

            if not m.Success then
                return! Error $"Unsupported numeric expression: {name} = {value}"

            let units = m.Groups[2].Value

            let factor =
                match kind, units with
                | Length, "mm" -> Some 1.
                | Length, "cm" -> Some 10.
                | Length, "m" -> Some 1000.
                | Length, "um" -> Some 0.001
                | Length, "nm" -> Some 0.000001
                | Angle, "deg" -> Some 1.
                | Angle, "rad" -> Some(180. / Math.PI)
                | Count, "" -> Some 1.
                | _ -> None

            let! factor = factor |> Result.requireSome $"Unexpected units for {name}: {units}"
            let parsed = Double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * factor

            if not (Double.IsFinite parsed) then
                return! Error $"Non-finite value: {name}"

            return parsed
    }

    resolve Set.empty key

/// Reads an optional numeric value using only documented placement defaults.
let private optionalNumber values kind fallback key =
    if Map.containsKey key values then
        number values kind key
    else
        Ok fallback

/// Reads a TOPAS boolean and rejects unsupported expressions.
let private boolean (values: Map<string, string>) fallback key =
    match Map.tryFind key values with
    | None -> Ok fallback
    | Some value ->
        match value.ToLowerInvariant() with
        | "true" -> Ok true
        | "false" -> Ok false
        | _ -> Error $"Invalid boolean: {key}"

/// Finds object names by their actual creation parameter, retaining slash-containing names.
let private names (values: Map<string, string>) (prefix: string) (suffix: string) =
    values
    |> Map.toList
    |> List.choose (fun (key, _) ->
        if
            key.StartsWith(prefix, StringComparison.Ordinal)
            && key.EndsWith(suffix, StringComparison.Ordinal)
        then
            Some(key.Substring(prefix.Length, key.Length - prefix.Length - suffix.Length))
        else
            None)

/// Reads a local translation or intrinsic XYZ rotation.
let private vector values kind prefix part = result {
    let! x = optionalNumber values kind 0. (prefix + part + "X")
    let! y = optionalNumber values kind 0. (prefix + part + "Y")
    let! z = optionalNumber values kind 0. (prefix + part + "Z")
    return { X = x; Y = y; Z = z }
}

/// Requires a strictly positive solid dimension.
let private positive values key =
    number values Length key
    |> Result.bind (fun value ->
        if value > 0. then
            Ok value
        else
            Error $"Dimension must be positive: {key}")

/// Parses the supported solids without substituting unsupported geometry.
let private shape values prefix = result {
    let! kind = required values (prefix + "Type")

    match kind with
    | "Group"
    | "TsGroup" -> return Group
    | "TsBox" ->
        let! x = positive values (prefix + "HLX")
        let! y = positive values (prefix + "HLY")
        let! z = positive values (prefix + "HLZ")
        return Box { X = x; Y = y; Z = z }
    | "TsCylinder" ->
        let! radius = positive values (prefix + "RMax")
        let! inner = optionalNumber values Length 0. (prefix + "RMin")
        let! half = positive values (prefix + "HL")
        let! start = optionalNumber values Angle 0. (prefix + "SPhi")
        let! sweep = optionalNumber values Angle 360. (prefix + "DPhi")

        if inner < 0. || inner >= radius || sweep <= 0. || sweep > 360. then
            return! Error $"Invalid cylinder dimensions: {prefix}"

        return Cylinder(inner, radius, half, start, sweep)
    | "TsCAD" ->
        let! path = required values (prefix + "InputFile")
        let! unit = positive values (prefix + "Units")
        let format = Map.tryFind (prefix + "FileFormat") values |> Option.defaultValue "stl"

        let file =
            if path.EndsWith("." + format, StringComparison.OrdinalIgnoreCase) then
                path
            else
                path + "." + format

        return Cad(file, unit)
    | other -> return Unsupported other
}

/// Parses a component and its original local coordinate system.
let private parseComponent values name = result {
    let p = "Ge/" + name + "/"

    let! parent =
        if name = "World" then
            Ok None
        else
            required values (p + "Parent") |> Result.map Some

    let! solid = shape values p
    let! translation = vector values Length p "Trans"
    let! rotation = vector values Angle p "Rot"
    let! included = boolean values true (p + "Include")
    let! invisible = boolean values false (p + "Invisible")
    let! isParallel = boolean values false (p + "IsParallel")

    return {
        Name = name
        Parent = parent
        Shape = solid
        Translation = translation
        Rotation = rotation
        Included = included
        Invisible = invisible
        Parallel = isParallel
        Material = Map.tryFind (p + "Material") values
        Color = Map.tryFind (p + "Color") values
    }
}

/// Parses a scorer's attachment, surface and bin counts.
let private scorer (values: Map<string, string>) name = result {
    let p = "Sc/" + name + "/"
    let! quantity = required values (p + "Quantity")
    let surface = Map.tryFind (p + "Surface") values

    let! owner =
        match surface with
        | Some value when value.Contains('/') -> Ok(value.Substring(0, value.LastIndexOf('/')))
        | Some _ -> Error $"Invalid scoring surface: {name}"
        | None -> required values (p + "Component")

    let! bins =
        ([ "X"; "Y"; "Z"; "R"; "Phi" ]
         |> List.map (fun axis -> result {
             let suffix = axis + "Bins"

             let key =
                 if Map.containsKey (p + suffix) values then
                     p + suffix
                 else
                     "Ge/" + owner + "/" + suffix

             let! count = optionalNumber values Count 1. key

             if count < 1. || count > float Int32.MaxValue || count <> floor count then
                 return! Error $"Invalid bin count: {key}"

             return axis, int count
         })
         |> List.sequenceResultM)

    let! active = boolean values true (p + "Active")

    return {
        Name = name
        Component = owner
        Surface = surface |> Option.map (fun value -> value.Substring(value.LastIndexOf('/') + 1))
        Quantity = quantity
        Bins = bins
        Active = active
    }
}

/// Checks explicit parents, cycles and scorer/source ownership before rendering.
let private validateRelationships
    (components: SceneComponent list)
    (scorers: SceneScorer list)
    (sources: SceneSource list)
    =
    result {
        let byName = components |> List.map (fun c -> c.Name, c) |> Map.ofList

        /// Walks the actual parent graph rather than interpreting slashes in names.
        let rec walk seen name = result {
            if Set.contains name seen then
                return! Error $"Cyclic geometry parent: {name}"

            let! c =
                Map.tryFind name byName
                |> Result.requireSome $"Unknown geometry component: {name}"

            match c.Parent with
            | None -> return ()
            | Some parent -> return! walk (Set.add name seen) parent
        }

        do!
            components
            |> List.map (fun c -> walk Set.empty c.Name)
            |> List.sequenceResultM
            |> Result.map ignore

        do!
            (scorers |> List.map _.Component) @ (sources |> List.map _.Component)
            |> List.map (fun name -> walk Set.empty name)
            |> List.sequenceResultM
            |> Result.map ignore
    }

/// Converts one resolved generated TOPAS input into an engine-independent scene.
let parse text : Result<SimulationScene, string> =
    try
        result {
            let! values = parameters text
            let componentNames = names values "Ge/" "/Parent" |> List.filter ((<>) "World")
            let! components = componentNames |> List.map (parseComponent values) |> List.sequenceResultM
            let zero = { X = 0.; Y = 0.; Z = 0. }

            let! world =
                if Map.containsKey "Ge/World/Type" values then
                    parseComponent values "World"
                else
                    Ok {
                        Name = "World"
                        Parent = None
                        Shape = Group
                        Translation = zero
                        Rotation = zero
                        Included = true
                        Invisible = false
                        Parallel = false
                        Material = None
                        Color = None
                    }

            let components = world :: components

            let! scorers =
                names values "Sc/" "/Quantity"
                |> List.map (scorer values)
                |> List.sequenceResultM

            let! sources =
                (names values "So/" "/Type"
                 |> List.map (fun name -> result {
                     let! owner = required values ("So/" + name + "/Component")
                     let! kind = required values ("So/" + name + "/Type")

                     return {
                         Name = name
                         Component = owner
                         SourceType = kind
                     }
                 })
                 |> List.sequenceResultM)

            do! validateRelationships components scorers sources

            if components.Length = 1 then
                return! Error "No geometry components found in this generated input."

            let warnings =
                components
                |> List.choose (fun c ->
                    match c.Shape with
                    | Unsupported kind -> Some $"{c.Name}: unsupported component {kind}; local frame only."
                    | _ -> None)

            return {
                Components = components
                Scorers = scorers
                Sources = sources
                Warnings = warnings
            }
        }
    with ex ->
        Error $"Cannot read generated geometry: {ex.Message}"