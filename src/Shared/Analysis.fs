module Analysis

open System
open Shared

/// Returns the largest value when samples exist.
let private tryMaximum values = values |> List.sortDescending |> List.tryHead

/// Checks a physical mapping before calculating distances.
let validateMapping (m: AnalysisMapping) =
    if not m.Confirmed then Error "Confirm the physical coordinate mapping first."
    elif not ([m.StepMm; m.OriginMm; m.CaxMm] |> List.forall Double.IsFinite) || m.StepMm = 0.0 then Error "Mapping requires finite values and non-zero spacing."
    elif m.Axis < 0 || m.Axis > 2 then Error "Choose X, Y or Z."
    elif not (List.contains m.Role ["PDD"; "X profile"; "Y profile"]) then Error "Choose a curve role."
    else Ok m

/// Interpolates between adjacent samples without extrapolation.
let interpolate (points: AnalysisPoint list) x =
    match points |> List.tryFind (fun p -> p.Position = x) with
    | Some p -> Some p.Dose
    | None ->
        points |> List.pairwise |> List.tryPick (fun (a,b) ->
            if a.Position < x && x < b.Position then Some (a.Dose + (b.Dose-a.Dose)*(x-a.Position)/(b.Position-a.Position)) else None)

/// Finds threshold crossings in sampled data using linear interpolation.
let crossings level points =
    points |> List.pairwise |> List.choose (fun (a,b) ->
        if a.Dose <> b.Dose && (a.Dose-level)*(b.Dose-level) <= 0.0 then
            Some (a.Position + (level-a.Dose)*(b.Position-a.Position)/(b.Dose-a.Dose))
        else None) |> List.distinct

/// Computes distal range after the sampled dose maximum.
let distalRange fraction points =
    if List.isEmpty points then None else
    let peak = points |> List.maxBy _.Dose
    points |> List.filter (fun p -> p.Position >= peak.Position) |> crossings (peak.Dose*fraction) |> List.tryHead

/// Normalizes dose while scaling its absolute uncertainty by the same fixed factor.
let normalize mode referencePosition (points: AnalysisPoint list) =
    let denominator =
        match mode with
        | "None" -> Some 100.0
        | "Maximum" -> points |> List.map _.Dose |> tryMaximum
        | _ -> interpolate points referencePosition
    match denominator with
    | Some d when Double.IsFinite d && d > 0.0 ->
        Ok (points |> List.map (fun p -> {p with Dose=p.Dose*100.0/d; Uncertainty=p.Uncertainty*100.0/d}))
    | _ -> Error "Normalization reference is missing, outside the curve, or non-positive."

/// Calculates profile metrics with explicit region, centre and formula selection.
let profileMetrics ratio region cax (points: AnalysisPoint list) =
    let peak = points |> List.maxBy _.Dose
    let edges = crossings (peak.Dose*0.5) points
    match edges with
    | left :: _ when edges.Length >= 2 ->
        let right = List.last edges
        let centre = cax
        let halfWidth = min (centre-left) (right-centre) * region
        if halfWidth <= 0.0 then [] else
        let positions =
            [yield centre-halfWidth; yield centre+halfWidth
             for p in points do
                if abs(p.Position-centre) <= halfWidth then yield p.Position
                let mirrored = 2.0*centre-p.Position
                if abs(mirrored-centre) <= halfWidth then yield mirrored] |> List.distinct |> List.sort
        let doses = positions |> List.choose (interpolate points)
        let pairs = positions |> List.choose (fun x ->
            match interpolate points x, interpolate points (2.0*centre-x) with
            | Some a, Some b -> Some(a,b)
            | _ -> None)
        let maximum, minimum = List.max doses, List.min doses
        let flatness = if minimum <= 0.0 then None elif ratio then Some(100.0*maximum/minimum) else Some(100.0*(maximum-minimum)/(maximum+minimum))
        let symmetry =
            if ratio then
                if pairs |> List.exists (fun (a,b) -> min a b <= 0.0) then None
                else pairs |> List.map (fun (a,b) -> 100.0*max (a/b) (b/a)) |> tryMaximum
            else
                interpolate points centre |> Option.bind (fun d -> if d <= 0.0 then None else pairs |> List.map (fun (a,b) -> 100.0*abs(a-b)/d) |> tryMaximum)
        /// Finds a left threshold crossing.
        let leftCross f = crossings (peak.Dose*f) points |> List.tryFind (fun x -> x < (left+right)/2.0)
        /// Finds a right threshold crossing.
        let rightCross f = crossings (peak.Dose*f) points |> List.tryFindBack (fun x -> x > (left+right)/2.0)
        [ "FWHM (mm)", Some(right-left)
          "Field centre (mm)", Some((left+right)/2.0)
          "Left penumbra 20-80 (mm)", Option.map2 (fun a b -> abs(a-b)) (leftCross 0.2) (leftCross 0.8)
          "Right penumbra 20-80 (mm)", Option.map2 (fun a b -> abs(a-b)) (rightCross 0.2) (rightCross 0.8)
          "Flatness (%)", flatness; "Symmetry (%)", symmetry ]
    | _ -> []

/// Calculates dose-curve metrics without interpreting ionization as dose.
let metrics role ratio region cax points =
    if List.isEmpty points then []
    elif role = "PDD" then
        ["Sampled dmax (mm)", Some((points |> List.maxBy _.Dose).Position)
         "R90 (mm)", distalRange 0.9 points; "R80 (mm)", distalRange 0.8 points; "R50 (mm)", distalRange 0.5 points]
    else profileMetrics ratio region cax points

/// Applies independently selected flatness and symmetry definitions.
let metricsWithDefinitions role flatnessRatio symmetryRatio region cax points =
    let baseMetrics=metrics role flatnessRatio region cax points
    let symmetry=metrics role symmetryRatio region cax points |> List.tryFind(fun (name,_)->name="Symmetry (%)")
    baseMetrics |> List.map(fun (name,value)-> if name="Symmetry (%)" then defaultArg symmetry (name,value) else name,value)
