module VendorImport

open System
open System.Globalization
open System.IO
open System.Text.RegularExpressions
open Shared
open FsToolkit.ErrorHandling

/// Reads a known vendor timestamp without using the server locale.
let date (formats: string array) (value: string) =
    match DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None) with
    | true,parsed -> parsed.ToString("yyyy-MM-ddTHH:mm:ss",CultureInfo.InvariantCulture)
    | _ -> ""

/// Converts a declared numeric geometry field to millimetres while preserving missing values.
let geometry scale (values: Map<string,string>) key = result {
    match Map.tryFind key values with
    | None | Some "" -> return ""
    | Some text ->
        let! number = Datasets.number text
        return (scale*number).ToString("G17",CultureInfo.InvariantCulture)
}

/// Reads all complete SNC scans from the exported dose table rather than raw detector channels.
let snc fileIndex (name: string) (text: string) = result {
    let text=text.Replace("\r","")
    let blocks=Regex.Matches(text,@"(?m)^[\t ]*BEGIN SCAN[\t ]*\n([\s\S]*?)^[\t ]*END SCAN[\t ]*$") |> Seq.cast<Match> |> Seq.toList
    let starts=Regex.Matches(text,@"(?m)^[\t ]*BEGIN SCAN[\t ]*$").Count
    do! Result.requireTrue "SNC export contains incomplete scan blocks." (starts>0 && starts=blocks.Length)
    let count=Regex.Match(text,@"(?m)^File Scan Count\t(\d+)")
    do! Result.requireTrue "SNC file scan count does not match complete scans." (not count.Success || int count.Groups[1].Value=blocks.Length)
    return! blocks |> List.indexed |> List.traverseResultM (fun (ordinal,block) -> result {
        let body=block.Groups[1].Value
        let metadata=body.Split('\n') |> Array.choose (fun line ->
            let fields=line.Split('\t')
            if fields.Length>=2 && fields[0].Trim()<>"" then Some(fields[0].Trim(),fields[1].Trim()) else None) |> Map.ofArray
        let tables=Regex.Matches(body,@"(?m)^[\t ]*BEGIN DOSE TABLE[^\n]*\n([\s\S]*?)^[\t ]*END DOSE TABLE[\t ]*$")
        do! Result.requireTrue "Each SNC scan requires exactly one dose table." (tables.Count=1)
        let lines=tables[0].Groups[1].Value.Split('\n') |> Array.toList |> List.map _.Trim() |> List.filter ((<>) "")
        let header=lines |> List.tryFindIndex (fun line -> line="X (cm)\tY (cm)\tZ (cm)\tRelative Dose (%)")
        let! headerIndex=header |> Result.requireSome "Unsupported SNC dose table columns or units."
        let! rows=lines |> List.skip (headerIndex+1) |> List.traverseResultM (fun line -> result {
            let cells=line.Split('\t') |> Array.map _.Trim()
            do! Result.requireTrue "SNC dose rows require X, Y, Z and relative dose." (cells.Length=4)
            let! numbers=cells |> Array.toList |> List.traverseResultM Datasets.number
            return numbers |> List.toArray
        })
        let varying=[0..2] |> List.filter (fun axis -> rows |> List.map (fun row -> row[axis]) |> List.distinct |> List.length > 1)
        do! Result.requireTrue "SNC import requires one varying coordinate; multidimensional scans are unsupported." (varying.Length=1)
        let axis=varying.Head
        let scanType=Map.tryFind "Scan Type" metadata |> Option.defaultValue ""
        do! Result.requireTrue "SNC depth scan must vary along Z." (scanType<>"Depth Scan" || axis=2)
        do! Result.requireTrue "SNC PDD needs an explicit vendor PDI-to-PDD conversion; raw ionization is not interpreted as dose." (axis<>2 || Map.tryFind "Is PDI to PDD" metadata=Some "True")
        let role=if axis=0 then "X profile" elif axis=1 then "Y profile" else "PDD"
        let scanId=Map.tryFind "Scan Id" metadata |> Option.filter ((<>) "") |> Option.defaultValue (string (ordinal+1))
        let! ssd=geometry 10. metadata "Source to Surface Distance (cm)"
        let! fieldX=geometry 10. metadata "Field Size X (cm)"
        let! fieldY=geometry 10. metadata "Field Size Y (cm)"
        let dateText=Map.tryFind "Scan Date" metadata |> Option.defaultValue ""
        let points=rows |> List.map (fun row -> {Position=row[axis];Dose=row[3];Uncertainty=0.}) |> List.sortBy _.Position
        do! Datasets.validatePoints points
        let depth=if axis=2 then "" else (rows.Head[2]*10.).ToString("G17",CultureInfo.InvariantCulture)
        let meta={Datasets.emptyMetadata ($"{Path.GetFileNameWithoutExtension name} · scan {scanId}") with
                    Role=role;PositionUnit="cm";ValueUnit="%";DepthMm=depth;SsdMm=ssd;FieldXmm=fieldX;FieldYmm=fieldY
                    AcquisitionDate=date [|"MM/dd/yyyy HH:mm";"M/d/yyyy H:mm"|] dateText}
        return {Id=string fileIndex+":"+scanId;Metadata=meta;Mapping={Datasets.mapping meta with Confirmed=false}
                MappingSource="SNC exported relative-dose table. Positions and values are preserved; no second centering, symmetry, normalization or detector offset is applied."
                Provenance={FileIndex=fileIndex;OriginalFile=name;ScanId=scanId;ParserVersion="snc-dose-table/1";Corrections=[]}
                HasUncertainty=false;Points=points}
    })
}
