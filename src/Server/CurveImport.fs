module CurveImport

open System
open System.IO
open System.Text
open System.Text.RegularExpressions
open Shared
open FsToolkit.ErrorHandling

/// Decodes and bounds original files before any parser runs.
let decode (files: ImportFile list) =
    try
        result {
            do! Result.requireTrue "Choose 1 to 20 files." (files.Length>0 && files.Length<=20)
            let originals = files |> List.map (fun file -> file,Convert.FromBase64String file.ContentBase64)
            do! Result.requireTrue "Imports are limited to 10 MB in total." ((originals |> List.sumBy (fun (_,bytes) -> int64 bytes.Length))<=10000000L)
            do! Result.requireTrue "Every file needs a name and content." (originals |> List.forall (fun (file,bytes) -> not (String.IsNullOrWhiteSpace file.Name) && bytes.Length>0))
            return originals
        }
    with ex -> Error $"Cannot decode uploaded files: {ex.Message}"

/// Splits simple delimited scan rows without guessing decimal commas or quoted columns.
let private cells (line: string) =
    if line.Contains(';') then line.Split(';') |> Array.map _.Trim()
    elif line.Contains(',') then line.Split(',') |> Array.map _.Trim()
    else Regex.Split(line.Trim(), @"\s+")

/// Reads two or three numeric columns with an optional absolute uncertainty.
let private samples (lines: string list) = result {
    let rows = lines |> List.filter (String.IsNullOrWhiteSpace >> not)
    let! parsed = rows |> List.traverseResultM (fun line -> result {
        let fields = cells line
        do! Result.requireTrue "Expected position, value, and optional absolute uncertainty columns." (fields.Length=2 || fields.Length=3)
        let! position = Datasets.number fields[0]
        let! value = Datasets.number fields[1]
        let! uncertainty = if fields.Length=3 then Datasets.number fields[2] else Ok 0.
        return {Position=position;Dose=value;Uncertainty=uncertainty},fields.Length=3
    })
    do! Result.requireTrue "All rows in a scan must have the same number of columns." ((parsed |> List.map snd |> List.distinct |> List.length)=1)
    let points = parsed |> List.map fst |> List.sortBy _.Position
    do! Datasets.validatePoints points
    return points,(parsed |> List.head |> snd)
}

/// Creates a vendor-independent draft while leaving unspecified geometry unconfirmed.
let private draft fileIndex fileName scanId parser metadata points hasUncertainty : DatasetCurve =
    { Id=string fileIndex+":"+scanId; Metadata=metadata
      Provenance={FileIndex=fileIndex;OriginalFile=fileName;ScanId=scanId;ParserVersion=parser;Corrections=[]}
      Mapping={Datasets.mapping metadata with Confirmed=false}; MappingSource="Import review required."
      HasUncertainty=hasUncertainty; Points=points }

/// Reads a strict two-column dose export with optional unit-bearing headers.
let private delimited fileIndex name (text: string) = result {
    let lines = text.Replace("\r", "").Split('\n') |> Array.toList |> List.filter (fun line -> not (String.IsNullOrWhiteSpace line) && not (line.TrimStart().StartsWith("#")))
    do! Result.requireTrue "File contains no scan samples." (not lines.IsEmpty)
    let fields = cells lines.Head
    let numericFirst = fields.Length>=2 && Result.isOk(Datasets.number fields[0])
    let! metadata,rows =
        if numericFirst then Ok(Datasets.emptyMetadata (Path.GetFileNameWithoutExtension name),lines)
        else
            let headers = fields |> Array.map (fun value -> value.Trim().ToLowerInvariant())
            let positions = ["position", "";"position_mm","mm";"position_cm","cm";"depth_mm","mm";"x_mm","mm";"y_mm","mm"] |> Map.ofList
            let doses = ["dose","";"value","";"dose_gy","Gy";"dose_cgy","cGy";"relative_dose","relative";"dose_percent","%"] |> Map.ofList
            result {
                let supportedHeaders = headers.Length>=2 && headers.Length<=3 && positions.ContainsKey headers[0] && doses.ContainsKey headers[1]
                do! Result.requireTrue "Unsupported text layout. Use position,value[,uncertainty] columns with optional explicit headers. Vendor exports need a validated parser." supportedHeaders
                let valueUnit = doses[headers[1]]
                let uncertaintyHeader = if valueUnit="Gy" then "standard_uncertainty_gy" elif valueUnit="cGy" then "standard_uncertainty_cgy" else "uncertainty"
                do! Result.requireTrue "The third column must be absolute uncertainty in the value units." (headers.Length=2 || headers[2]=uncertaintyHeader || headers[2]="uncertainty")
                let role = match headers[0] with "depth_mm" -> "PDD" | "x_mm" -> "X profile" | "y_mm" -> "Y profile" | _ -> ""
                return {Datasets.emptyMetadata (Path.GetFileNameWithoutExtension name) with PositionUnit=positions[headers[0]];ValueUnit=valueUnit;Role=role},lines.Tail
            }
    let! points,uncertainty = samples rows
    return [draft fileIndex name "1" "delimited-dose/1" metadata points uncertainty]
}

/// Reads explicitly bounded MCC-style scans without assuming units or unrecognised metadata.
let private mcc fileIndex name (text: string) = result {
    let blocks = Regex.Matches(text, @"(?ms)^\s*BEGIN_SCAN\s+(\d+)\s*$([\s\S]*?)^\s*END_SCAN\s+\1\s*$") |> Seq.cast<Match> |> Seq.toList
    do! Result.requireTrue "No complete BEGIN_SCAN/END_SCAN blocks were found." (not blocks.IsEmpty)
    let starts = Regex.Matches(text,@"(?m)^\s*BEGIN_SCAN\s+\d+\s*$").Count
    do! Result.requireTrue "Incomplete scan blocks; import was stopped." (starts=blocks.Length)
    return! blocks |> List.traverseResultM (fun block -> result {
        let scanId,body = block.Groups[1].Value,block.Groups[2].Value
        let data = Regex.Matches(body,@"(?ms)^\s*BEGIN_DATA\s*$([\s\S]*?)^\s*END_DATA\s*$")
        do! Result.requireTrue $"Scan {scanId} requires exactly one complete data block." (data.Count=1)
        let parameters = Regex.Matches(body,@"(?m)^\s*([A-Z_]+)\s*=\s*([^\r\n]+)") |> Seq.cast<Match> |> Seq.map (fun m -> m.Groups[1].Value,m.Groups[2].Value.Trim()) |> Map.ofSeq
        let role = match Map.tryFind "SCAN_CURVETYPE" parameters with Some "PDD" -> "PDD" | Some "CROSSPLANE_PROFILE" -> "X profile" | Some "INPLANE_PROFILE" -> "Y profile" | _ -> ""
        let metadata = {Datasets.emptyMetadata ($"{Path.GetFileNameWithoutExtension name} · scan {scanId}") with Role=role}
        let rows = data[0].Groups[1].Value.Replace("\r", "").Split('\n') |> Array.toList |> List.filter (String.IsNullOrWhiteSpace >> not)
        do! Result.requireTrue $"Scan {scanId}: additional MCC columns require a validated vendor parser; they are not interpreted as uncertainty." (rows |> List.forall (fun line -> (cells line).Length=2))
        let! points,uncertainty = samples rows
        return draft fileIndex name scanId "mcc-blocks/1" metadata points uncertainty
    })
}

/// Parses supported text files while preserving their exact original bytes.
let preview files =
    try
        result {
            let! originals = decode files
            let! curves = originals |> List.indexed |> List.traverseResultM (fun (index,(file,bytes)) ->
                result {
                    let text = UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF')
                    let name = file.Name.Replace('\\','/') |> Path.GetFileName
                    let! scans = if text.Contains("BEGIN_SCAN") then mcc index name text else delimited index name text
                    return scans
                } |> Result.mapError (fun message -> $"{file.Name}: {message}"))
            let scans = List.concat curves
            do! Result.requireTrue "Imports are limited to 200 curves." (scans.Length<=200)
            do! Result.requireTrue "Duplicate scan identifiers in one file are unsupported." ((scans |> List.map _.Id |> List.distinct |> List.length)=scans.Length)
            return {Files=files;Curves=scans}
        }
    with ex -> Error $"Cannot parse import: {ex.Message}"
