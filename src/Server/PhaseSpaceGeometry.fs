module PhaseSpaceGeometry

open System
open System.Globalization
open System.Text.RegularExpressions
open FsToolkit.ErrorHandling
open Shared

/// Reads one named IAEA header section without leaking into the next section.
let private section name (text: string) =
    let matched =
        Regex.Match(text, @"(?ms)^\s*\$" + name + @":\s*\r?\n(.*?)(?=^\s*\$|\z)")

    if matched.Success then matched.Groups[1].Value else ""

/// Reads finite invariant-culture values and rejects corrupt header coordinates.
let private numeric (value: string) =
    match Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture) with
    | true, number when Double.IsFinite number -> Ok number
    | _ -> Error $"Invalid phase-space coordinate: {value}"

/// Reads non-comment header rows without interpreting human-readable descriptions.
let private rows text =
    (text: string).Split('\n')
    |> Array.map (fun line -> (line.Split([| "//" |], StringSplitOptions.None)[0]).Trim())
    |> Array.filter (String.IsNullOrWhiteSpace >> not)

/// Reads header-reported coordinate ranges in centimetres and applies source transforms.
let parseBounds (scale: SceneVector) text : Result<SceneBounds, string> = result {
    let geometry = section "STATISTICAL_INFORMATION_GEOMETRY" text |> rows
    let pattern = @"([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)"
    let labeled = @"^([xyzXYZ])\s+" + pattern + @"\s+" + pattern + @"\s*$"
    let unlabeled = @"^" + pattern + @"\s+" + pattern + @"\s*$"

    let ranges =
        geometry
        |> Array.choose (fun line ->
            let matched = Regex.Match(line, labeled)

            if matched.Success then
                Some(matched.Groups[1].Value.ToUpperInvariant(), matched.Groups[2].Value, matched.Groups[3].Value)
            else
                None)

    let anonymous =
        geometry
        |> Array.choose (fun line ->
            let matched = Regex.Match(line, unlabeled)

            if matched.Success then
                Some(matched.Groups[1].Value, matched.Groups[2].Value)
            else
                None)

    let ranges =
        if ranges.Length = 0 then
            anonymous
            |> Array.truncate 3
            |> Array.mapi (fun i (low, high) -> (string ("XYZ"[i]), low, high))
        else
            ranges

    let contents = section "RECORD_CONTENTS" text |> rows
    let omittedZ = contents.Length > 2 && contents[2] = "0"

    /// Resolves measured ranges and the zero Z used by TOPAS for Limited records without Z.
    let axis name factor = result {
        if factor = 0. || (name = "Z" && omittedZ) then
            return 0., 0.
        else
            let! low, high =
                match ranges |> Array.tryFind (fun (axis, _, _) -> axis = name) with
                | Some(_, low, high) -> Ok(low, high)
                | None -> Error $"Phase-space header has no {name} bounds."

            let! low = numeric low
            let! high = numeric high

            if low > high then
                return! Error $"Reversed phase-space {name} bounds."

            let a, b = low * 10. * factor, high * 10. * factor

            if not (Double.IsFinite a && Double.IsFinite b) then
                return! Error $"Non-finite transformed phase-space {name} bounds."

            return min a b, max a b
    }

    let! xmin, xmax = axis "X" scale.X
    let! ymin, ymax = axis "Y" scale.Y
    let! zmin, zmax = axis "Z" scale.Z

    return {
        Min = { X = xmin; Y = ymin; Z = zmin }
        Max = { X = xmax; Y = ymax; Z = zmax }
    }
}