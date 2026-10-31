module AnalysisPlot

open System
open Feliz
open Shared

/// Represents a displayed curve with stable identity and line styling.
type Curve = { Id: string; Label: string; Role: string; Points: AnalysisPoint list }

/// Formats compact axis labels without changing exported precision.
let private number (value: float) = value.ToString("G4", Globalization.CultureInfo.InvariantCulture)

/// Formats visible values to one decimal place with scientific notation for small doses.
let private displayNumber (value: float) =
    let precision = if value<>0.0 && abs value<0.1 then "E1" else "F1"
    value.ToString(precision, Globalization.CultureInfo.InvariantCulture)

/// Assigns a stable colour to each run across scorers and collection changes.
let color (id: string) =
    let parts = id.Split('/')
    let key = parts.[0]
    let index = key |> Seq.fold (fun total c -> (total*31+int c)%8) 0
    let palette = [|"#2563eb"; "#e05a36"; "#009e73"; "#a855f7"; "#d18a00"; "#0891b2"; "#db2777"; "#64748b"|]
    palette.[index]

/// Chooses a readable major grid spacing for the current data interval.
let private tickStep span =
    let raw = max 1e-12 span / 6.0
    let magnitude = 10.0 ** floor (log10 raw)
    let fraction = raw/magnitude
    (if fraction <= 1.0 then 1.0 elif fraction <= 2.0 then 2.0 elif fraction <= 5.0 then 5.0 else 10.0)*magnitude

/// Enumerates labelled major grid positions within an axis range.
let private ticks minimum maximum =
    let step = tickStep (maximum-minimum)
    let start = ceil (minimum/step)*step
    [for i in 0..int (floor ((maximum-start)/step)) -> start+float i*step]

/// Renders one responsive overlay with a compact legend and sample tooltips.
let view (title: string) (xTitle: string) (unitName: string) bands samples plateau (curves: Curve list) =
    let points = curves |> List.collect _.Points
    if points.IsEmpty then
        Html.div [prop.className "analysis-empty"; prop.text "No curves in this view."]
    else
        let minimumX = points |> List.minBy _.Position |> _.Position
        let maximumX = points |> List.maxBy _.Position |> _.Position
        let xmin, xmax = if minimumX=maximumX then minimumX-1.0, maximumX+1.0 else minimumX, maximumX
        let lower = points |> List.map (fun p -> p.Dose-(if bands then p.Uncertainty else 0.0)) |> List.min
        let upper = points |> List.map (fun p -> p.Dose+(if bands then p.Uncertainty else 0.0)) |> List.max
        let ymin, ymax =
            if plateau then 95.0, 105.0
            else
                let bottom = min 0.0 lower
                bottom, max (bottom+1e-12) (upper+0.05*max 1e-12 (upper-bottom))
        /// Maps a physical abscissa to the chart viewport.
        let x value = 76.0+(value-xmin)/(xmax-xmin)*884.0
        /// Maps a dose ordinate to the chart viewport.
        let y value = 450.0-(value-ymin)/(ymax-ymin)*420.0
        /// Formats polyline vertices in SVG coordinate syntax.
        let vertices values = values |> List.map (fun (a,b) -> $"{number a},{number b}") |> String.concat " "
        Html.div [prop.className "analysis-plot"; prop.children [
            Svg.svg [svg.viewBox (0,0,1000,510); svg.custom ("role","img"); svg.custom ("aria-label",title); svg.children [
                Svg.title title
                Svg.defs [svg.children [Svg.clipPath [svg.id "analysis-plot-clip"; svg.children [
                    Svg.rect [svg.x 76; svg.y 30; svg.width 884; svg.height 420]]]]]
                for value in ticks ymin ymax do
                    Svg.line [svg.x1 76; svg.x2 960; svg.y1 (y value); svg.y2 (y value); svg.custom ("class","analysis-grid")]
                    Svg.text [svg.x 64; svg.y (y value+4.0); svg.custom ("text-anchor","end"); svg.custom ("class","analysis-tick"); svg.text (displayNumber value)]
                for value in ticks xmin xmax do
                    Svg.line [svg.x1 (x value); svg.x2 (x value); svg.y1 30; svg.y2 450; svg.custom ("class","analysis-grid")]
                    Svg.text [svg.x (x value); svg.y 474; svg.custom ("text-anchor","middle"); svg.custom ("class","analysis-tick"); svg.text (displayNumber value)]
                if xmin < 0.0 && xmax > 0.0 then
                    Svg.line [svg.x1 (x 0.0); svg.x2 (x 0.0); svg.y1 30; svg.y2 450; svg.custom ("class","analysis-zero")]
                Svg.line [svg.x1 76; svg.x2 960; svg.y1 450; svg.y2 450; svg.custom ("class","analysis-axis")]
                Svg.line [svg.x1 76; svg.x2 76; svg.y1 30; svg.y2 450; svg.custom ("class","analysis-axis")]
                Svg.text [svg.x 518; svg.y 502; svg.custom ("text-anchor","middle"); svg.custom ("class","analysis-axis-title"); svg.text xTitle]
                Svg.text [svg.x 18; svg.y 240; svg.custom ("transform","rotate(-90 18 240)"); svg.custom ("text-anchor","middle"); svg.custom ("class","analysis-axis-title"); svg.text unitName]
                Svg.g [svg.custom ("clip-path","url(#analysis-plot-clip)"); svg.children [
                    for curve in curves do
                        let stroke = color curve.Id
                        if bands then
                            let outline =
                                (curve.Points |> List.map (fun p -> x p.Position, y (p.Dose+p.Uncertainty))) @
                                (curve.Points |> List.rev |> List.map (fun p -> x p.Position, y (p.Dose-p.Uncertainty)))
                            Svg.polygon [svg.points (vertices outline); svg.fill stroke; svg.fillOpacity 0.10]
                        Svg.polyline [svg.points (curve.Points |> List.map (fun p -> x p.Position,y p.Dose) |> vertices)
                                      svg.fill "none"; svg.stroke stroke; svg.strokeWidth 2.2
                                      svg.custom ("stroke-dasharray",if curve.Role="Y profile" then "7 5" else "none")]
                        for p in curve.Points do
                            Svg.circle [svg.cx (x p.Position); svg.cy (y p.Dose); svg.r (if samples then 2.5 else 4.0)
                                        svg.fill stroke; svg.fillOpacity (if samples then 0.85 else 0.0)
                                        svg.children [Svg.title $"{curve.Label} · {curve.Role}\n{displayNumber p.Position} mm · {displayNumber p.Dose} {unitName}"]]
                ]]
            ]]
            Html.div [prop.className "analysis-legend"; prop.children [
                for curve in curves do
                    let roleLabel = curve.Role.Replace(" profile", "")
                    Html.span [prop.title curve.Id; prop.className "analysis-legend-item"; prop.children [
                        Html.span [prop.className (if curve.Role="Y profile" then "analysis-swatch dashed" else "analysis-swatch")
                                   prop.style [style.borderColor (color curve.Id)]]
                        Html.span $"{curve.Label} · {roleLabel}"]]]]
        ]]
