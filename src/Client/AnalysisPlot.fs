module AnalysisPlot

open System
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Shared

/// Represents a displayed curve with stable identity and line styling.
type Curve = { Id: string; Label: string; Role: string; Points: AnalysisPoint list }

/// Imports the React component that manages the Plotly chart lifecycle.
[<Import("default", "./AnalysisChart.jsx")>]
let private chartComponent: obj = jsNative

/// Creates a React element without calling a hooked component as a function.
[<Import("createElement", "react")>]
let private createElement (component: obj, props: obj): Fable.React.ReactElement = jsNative

/// Assigns a stable colour to each run across scorers and collection changes.
let color (id: string) =
    let key = id.Split('/').[0]
    let index = key |> Seq.fold (fun total c -> (total*31+int c)%8) 0
    let palette = [|"#2563eb"; "#e05a36"; "#009e73"; "#a855f7"; "#d18a00"; "#0891b2"; "#db2777"; "#64748b"|]
    palette.[index]

/// Converts a run colour to a translucent uncertainty fill.
let private fillColor (hex: string) =
    let red = Convert.ToInt32(hex.Substring(1,2),16)
    let green = Convert.ToInt32(hex.Substring(3,2),16)
    let blue = Convert.ToInt32(hex.Substring(5,2),16)
    $"rgba({red},{green},{blue},0.10)"

/// Builds paired uncertainty boundaries followed by the dose trace.
let private traces bands samples valueFormat (curve: Curve) =
    let positions = curve.Points |> List.map _.Position |> List.toArray
    let stroke = color curve.Id
    [|
        if bands then
            for direction in [-1.0;1.0] do
                yield createObj [
                    "type" ==> "scatter"; "mode" ==> "lines"
                    "uid" ==> (curve.Id+(if direction>0.0 then "/upper" else "/lower"))
                    "x" ==> positions
                    "y" ==> (curve.Points |> List.map (fun p -> p.Dose+direction*p.Uncertainty) |> List.toArray)
                    "line" ==> createObj ["width" ==> 0]
                    "fill" ==> (if direction>0.0 then "tonexty" else "none")
                    "fillcolor" ==> fillColor stroke
                    "hoverinfo" ==> "skip"; "showlegend" ==> false]
        yield createObj [
            "type" ==> "scatter"; "mode" ==> (if samples then "lines+markers" else "lines")
            "uid" ==> curve.Id; "name" ==> (curve.Label+" · "+curve.Role)
            "x" ==> positions; "y" ==> (curve.Points |> List.map _.Dose |> List.toArray)
            "line" ==> createObj ["color" ==> stroke; "width" ==> 2.2; "dash" ==> (if curve.Role="Y profile" then "dash" else "solid")]
            "marker" ==> createObj ["color" ==> stroke; "size" ==> 5]
            "text" ==> (curve.Points |> List.map (fun p ->
                let dose = if valueFormat=".1e" then sprintf "%.1e" p.Dose else Analysis.displayNumber p.Dose
                Analysis.displayNumber p.Position+" mm · "+dose) |> List.toArray)
            "hovertemplate" ==> "%{text}<extra>%{fullData.name}</extra>"
            "showlegend" ==> false]
    |]

/// Builds a labelled axis with one-decimal ticks and a visible zero line.
let private axis title valueFormat =
    createObj [
        "title" ==> createObj ["text" ==> title; "standoff" ==> 12]
        "tickformat" ==> valueFormat; "hoverformat" ==> valueFormat
        "automargin" ==> true; "gridcolor" ==> "rgba(128,128,128,0.15)"
        "zerolinecolor" ==> "rgba(128,128,128,0.45)"; "zerolinewidth" ==> 1]

/// Renders an interactive Plotly overlay with the run selector as its legend.
let view (title: string) (context: string) (xTitle: string) (unitName: string) bands samples (curves: Curve list) =
    let points = curves |> List.collect _.Points
    if points.IsEmpty then
        Html.div [prop.className "analysis-empty"; prop.text "No curves in this view."]
    else
        let lower = points |> List.map (fun p -> p.Dose-(if bands then p.Uncertainty else 0.0)) |> List.min
        let upper = points |> List.map (fun p -> p.Dose+(if bands then p.Uncertainty else 0.0)) |> List.max
        let bottom = min 0.0 lower
        let range = [|bottom;max (bottom+1e-12) (upper+0.05*max 1e-12 (upper-bottom))|]
        let valueFormat = if unitName.Contains("Gy") && max (abs lower) (abs upper)<0.1 then ".1e" else ".1f"
        let first = points |> List.minBy _.Position |> _.Position
        let last = points |> List.maxBy _.Position |> _.Position
        let revision = String.concat "|" [title;context;unitName;string first;string last;curves |> List.map _.Id |> String.concat ";"]
        let layout = createObj [
            "autosize" ==> true; "showlegend" ==> false; "uirevision" ==> revision
            "dragmode" ==> "zoom"; "hovermode" ==> "closest"
            "paper_bgcolor" ==> "rgba(0,0,0,0)"; "plot_bgcolor" ==> "rgba(0,0,0,0)"
            "font" ==> createObj ["family" ==> "system-ui, sans-serif"; "size" ==> 12]
            "margin" ==> createObj ["l" ==> 72; "r" ==> 20; "t" ==> 40; "b" ==> 60]
            "xaxis" ==> axis xTitle ".1f"
            "yaxis" ==> createObj [
                "title" ==> createObj ["text" ==> unitName; "standoff" ==> 12]
                "tickformat" ==> valueFormat; "hoverformat" ==> valueFormat
                "automargin" ==> true; "range" ==> range; "autorange" ==> false
                "gridcolor" ==> "rgba(128,128,128,0.15)"; "zerolinecolor" ==> "rgba(128,128,128,0.45)"]]
        let config = createObj [
            "responsive" ==> true; "scrollZoom" ==> true; "displaylogo" ==> false
            "displayModeBar" ==> true; "doubleClick" ==> "reset"
            "modeBarButtonsToRemove" ==> [|"select2d";"lasso2d"|]
            "toImageButtonOptions" ==> createObj ["format" ==> "png"; "filename" ==> ("topas-"+title); "scale" ==> 2]]
        createElement (chartComponent, createObj [
            "key" ==> revision
            "title" ==> title; "data" ==> (curves |> List.toArray |> Array.collect (traces bands samples valueFormat))
            "layout" ==> layout; "config" ==> config])
