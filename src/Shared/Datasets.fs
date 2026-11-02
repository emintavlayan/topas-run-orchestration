module Datasets

open System
open System.Globalization
open Shared
open FsToolkit.ErrorHandling

/// Represents unknown acquisition metadata without inferred geometry or units.
let emptyMetadata name : CurveMetadata =
    { Name=name; Role=""; PositionUnit=""; ValueUnit=""; DepthMm=""; SsdMm=""; FieldXmm=""; FieldYmm=""; AcquisitionDate="" }

/// Parses an invariant finite numeric metadata value.
let number (text: string) =
    match Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture) with
    | true,value when not (Double.IsNaN value || Double.IsInfinity value) -> Ok value
    | _ -> Error $"Invalid finite number: {text}"

/// Checks optional geometry values while retaining missing values explicitly.
let private validateOptional label positive text = result {
    if not (String.IsNullOrWhiteSpace text) then
        let! value = number text
        do! Result.requireTrue ($"{label} must be " + (if positive then "positive." else "non-negative.")) (if positive then value>0. else value>=0.)
}

/// Checks dates as ISO calendar dates or ISO timestamps without inventing missing dates.
let validateDate (text: string) =
    if String.IsNullOrWhiteSpace text then Ok ()
    else
        match DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None) with
        | true,_ when text.Length>=10 && text[4]='-' && text[7]='-' -> Ok ()
        | _ -> Error "Use an ISO acquisition date, for example 2026-11-02, or leave it unknown."

/// Checks reviewed metadata before a curve becomes available to analysis.
let validateMetadata allowIndex (metadata: CurveMetadata) = result {
    do! Result.requireTrue "Enter a curve name." (not (String.IsNullOrWhiteSpace metadata.Name))
    do! Result.requireTrue "Choose X profile, Y profile or PDD." (List.contains metadata.Role ["X profile";"Y profile";"PDD"])
    do! Result.requireTrue "Confirm coordinate units." (List.contains metadata.PositionUnit (if allowIndex then ["mm";"cm";"m";"index"] else ["mm";"cm";"m"]))
    do! Result.requireTrue "Confirm value units." (List.contains metadata.ValueUnit ["Gy";"cGy";"%";"relative"])
    do! validateOptional "Depth" false metadata.DepthMm
    do! validateOptional "SSD" true metadata.SsdMm
    do! validateOptional "Field X" true metadata.FieldXmm
    do! validateOptional "Field Y" true metadata.FieldYmm
    do! validateDate metadata.AcquisitionDate
}

/// Checks finite ordered samples without smoothing, averaging or normalization.
let validatePoints (points: AnalysisPoint list) = result {
    do! Result.requireTrue "A curve requires 2 to 100000 samples." (points.Length>=2 && points.Length<=100000)
    do! Result.requireTrue "Samples require finite positions and non-negative finite values and uncertainties."
        (points |> List.forall (fun p -> ([p.Position;p.Dose;p.Uncertainty] |> List.forall (fun v -> not (Double.IsNaN v || Double.IsInfinity v))) && p.Dose>=0. && p.Uncertainty>=0.))
    do! Result.requireTrue "Duplicate coordinates are unsupported; select a single scan." ((points |> List.map _.Position |> List.distinct |> List.length)=points.Length)
}

/// Converts known coordinate units to millimetres without changing original samples.
let coordinateScale unitName = match unitName with "cm" -> 10. | "m" -> 1000. | _ -> 1.

/// Creates the physical mapping for reviewed measurement coordinates.
let mapping (metadata: CurveMetadata) : AnalysisMapping =
    { Role=metadata.Role; Axis=(if metadata.Role="X profile" then 0 elif metadata.Role="Y profile" then 1 else 2)
      StepMm=coordinateScale metadata.PositionUnit; OriginMm=0.; CaxMm=0.; Confirmed=true }

/// Records each changed metadata field with its previous and reviewed values.
let corrections (before: CurveMetadata) (after: CurveMetadata) =
    ["Name",before.Name,after.Name; "Role",before.Role,after.Role; "Position unit",before.PositionUnit,after.PositionUnit
     "Value unit",before.ValueUnit,after.ValueUnit; "Depth [mm]",before.DepthMm,after.DepthMm; "SSD [mm]",before.SsdMm,after.SsdMm
     "Field X [mm]",before.FieldXmm,after.FieldXmm; "Field Y [mm]",before.FieldYmm,after.FieldYmm
     "Acquisition date",before.AcquisitionDate,after.AcquisitionDate]
    |> List.choose (fun (name,a,b) -> if a=b then None else Some ($"{name}: '{a}' → '{b}'"))
