module SceneViews

open System
open Feliz
open Fable.Core
open Fable.Core.JsInterop
open Shared
open SAFE

/// Imports the component that owns the JSCAD renderer lifecycle.
[<Import("default", "./SceneCanvas.jsx")>]
let private sceneCanvas: obj = jsNative

/// Creates a React element for the isolated renderer component.
[<Import("createElement", "react")>]
let private createElement (reactComponent: obj, props: obj) : Fable.React.ReactElement = jsNative

/// Converts a typed vector to the renderer's three-coordinate array.
let private coordinates (v: SceneVector) = [| v.X; v.Y; v.Z |]

/// Converts a typed component into a renderer boundary object.
let private renderComponent (c: SceneComponent) =
    let kind, dimensions, path, unit =
        match c.Shape with
        | Group -> "group", [||], "", 1.
        | Box half -> "box", coordinates half, "", 1.
        | Cylinder(inner, outer, half, start, sweep) -> "cylinder", [| inner; outer; half; start; sweep |], "", 1.
        | Cad(path, unit) -> "cad", [||], path, unit
        | Unsupported _ -> "unsupported", [||], "", 1.

    createObj [
        "name" ==> c.Name
        "parent" ==> (c.Parent |> Option.toObj)
        "kind" ==> kind
        "dimensions" ==> dimensions
        "path" ==> path
        "unit" ==> unit
        "translation" ==> coordinates c.Translation
        "rotation" ==> coordinates c.Rotation
        "material" ==> (c.Material |> Option.toObj)
        "color" ==> (c.Color |> Option.toObj)
        "included" ==> c.Included
    ]

/// Describes full solid dimensions in physical units for inspection.
let private shapeDescription shape =
    match shape with
    | Group -> "Coordinate frame"
    | Box half -> sprintf "Box: %.2f × %.2f × %.2f mm" (2. * half.X) (2. * half.Y) (2. * half.Z)
    | Cylinder(inner, outer, half, start, sweep) ->
        sprintf
            "Cylinder: R %.2f–%.2f mm · length %.2f mm · angle %.1f–%.1f°"
            inner
            outer
            (2. * half)
            start
            (start + sweep)
    | Cad(path, unit) -> sprintf "STL: %s · %.3f mm per unit" path unit
    | Unsupported kind -> "Unsupported solid: " + kind

/// Renders a compact action button.
let private button (label: string) action =
    Html.button [
        prop.className "analysis-button"
        prop.text label
        prop.onClick (fun _ -> action ())
    ]

/// Formats physical coordinates without modifying their values.
let private position (v: SceneVector) = sprintf "%.2f, %.2f, %.2f" v.X v.Y v.Z

/// Computes tree depth from explicit ownership rather than component naming.
let private depth (components: SceneComponent list) (c: SceneComponent) =
    /// Counts the validated parent chain for indentation.
    let rec count n parent =
        match parent with
        | None -> n
        | Some name -> count (n + 1) (components |> List.find (fun p -> p.Name = name) |> _.Parent)

    count 0 c.Parent

/// Traverses parent ownership so descendants follow their actual parent in the tree.
let private treeOrder (components: SceneComponent list) =
    /// Appends children recursively after their parent.
    let rec children parent =
        components
        |> List.filter (fun c -> c.Parent = parent)
        |> List.sortBy _.Name
        |> List.collect (fun c -> c :: children (Some c.Name))

    children None

/// Renders a generated-run geometry inspector with independent request lifetimes.
[<ReactComponent>]
let View () =
    let api = React.useMemo ((fun () -> Api.makeProxy<ITopasApi> ()), [||])
    let inputs, setInputs = React.useState<SceneInput list> ([])
    let inputId, setInputId = React.useState ("")
    let scene, setScene = React.useState<SimulationScene option> (None)
    let error, setError = React.useState ("")
    let busy, setBusy = React.useState (false)
    let selected, setSelected = React.useState ("")
    let hidden, setHidden = React.useState<Set<string>> (Set.empty)
    let assets, setAssets = React.useState<Map<string, string>> (Map.empty)
    let assetErrors, setAssetErrors = React.useState<string list> ([])
    let reset, setReset = React.useStateWithUpdater (0)
    let view, setView = React.useState ("orbit")
    let axes, setAxes = React.useState (true)
    let sources, setSources = React.useState (true)
    let revision, setRevision = React.useState (0)
    let generation = React.useRef (0)

    React.useEffect (
        (fun () ->
            let mutable live = true

            async {
                try
                    let! result = api.listSceneInputs ()

                    if live then
                        match result with
                        | Ok values -> setInputs values
                        | Error reason -> setError reason
                with ex ->
                    if live then
                        setError ex.Message
            }
            |> Async.StartImmediate

            { new IDisposable with
                member _.Dispose() = live <- false
            }),
        [| box revision |]
    )

    React.useEffect (
        (fun () ->
            generation.current <- generation.current + 1
            let request = generation.current
            setScene None
            setAssets Map.empty
            setAssetErrors []
            setError ""

            if inputId <> "" then
                setBusy true

                async {
                    try
                        let! result = api.loadScene inputId

                        if generation.current = request then
                            match result with
                            | Error reason -> setError reason
                            | Ok value ->
                                setScene (Some value)
                                setSelected ""
                                setHidden Set.empty
                                setReset (fun current -> current + 1)

                                let cad =
                                    value.Components
                                    |> List.filter (fun c ->
                                        match c.Shape with
                                        | Cad _ -> true
                                        | _ -> false)

                                let! loaded =
                                    (cad
                                     |> List.map (fun c -> async {
                                         try
                                             let! asset =
                                                 api.loadSceneAsset {
                                                     InputId = inputId
                                                     Component = c.Name
                                                 }

                                             return c.Name, asset
                                         with ex ->
                                             return c.Name, Error ex.Message
                                     })
                                     |> Async.Parallel)

                                if generation.current = request then
                                    setReset (fun current -> current + 1)

                                    setAssets (
                                        loaded
                                        |> Array.choose (fun (name, result) ->
                                            match result with
                                            | Ok asset -> Some(name, asset.Base64)
                                            | _ -> None)
                                        |> Map.ofArray
                                    )

                                    setAssetErrors (
                                        loaded
                                        |> Array.choose (fun (name, result) ->
                                            match result with
                                            | Error reason -> Some(name + ": " + reason)
                                            | _ -> None)
                                        |> Array.toList
                                    )

                        if generation.current = request then
                            setBusy false
                    with ex ->
                        if generation.current = request then
                            setError ex.Message
                            setBusy false
                }
                |> Async.StartImmediate
            else
                setBusy false

            { new IDisposable with
                member _.Dispose() =
                    generation.current <- generation.current + 1
            }),
        [| box inputId; box revision |]
    )

    /// Selects a standard camera orientation and refits the scene.
    let changeView name =
        setView name
        setReset (fun current -> current + 1)

    Html.section [
        prop.className "scene-workspace"
        prop.children [
            Html.div [
                prop.className "scene-toolbar"
                prop.children [
                    Html.label [
                        prop.children [
                            Html.span "Generated input "
                            Html.select [
                                prop.className "select select-bordered select-sm"
                                prop.value inputId
                                prop.onChange setInputId
                                prop.custom ("aria-label", "Generated input")
                                prop.children [
                                    Html.option [ prop.value ""; prop.text "Select a generated input" ]
                                    for batch, items in inputs |> List.groupBy _.Batch do
                                        Html.optgroup [
                                            prop.label batch
                                            prop.children [
                                                for input in items ->
                                                    Html.option [ prop.value input.Id; prop.text input.FileName ]
                                            ]
                                        ]
                                ]
                            ]
                        ]
                    ]
                    button "Refresh" (fun () -> setRevision (revision + 1))
                    for label, name in
                        [
                            "Fit", "orbit"
                            "Front (−Z)", "front"
                            "Side (+X)", "side"
                            "Top (+Y)", "top"
                        ] do
                        button label (fun () -> changeView name)
                    Html.label [
                        Html.input [ prop.type'.checkbox; prop.isChecked axes; prop.onChange setAxes ]
                        Html.span " Local axes"
                    ]
                    Html.label [
                        Html.input [ prop.type'.checkbox; prop.isChecked sources; prop.onChange setSources ]
                        Html.span " Sources"
                    ]
                ]
            ]
            if busy then
                Html.p [
                    prop.custom ("role", "status")
                    prop.text "Loading generated geometry and CAD…"
                ]
            if error <> "" then
                Html.p [ prop.className "text-error"; prop.custom ("role", "alert"); prop.text error ]
            match scene with
            | None ->
                if not busy && error = "" then
                    Html.p "Select a generated input to inspect its geometry."
            | Some value ->
                Html.div [
                    prop.className "scene-layout"
                    prop.children [
                        Html.aside [
                            prop.className "scene-tree"
                            prop.children [
                                Html.h3 "Components"
                                for c in treeOrder value.Components do
                                    Html.div [
                                        prop.className (
                                            "scene-tree-row" + (if selected = c.Name then " selected" else "")
                                        )
                                        prop.style [ style.paddingLeft (depth value.Components c * 12) ]
                                        prop.children [
                                            Html.input [
                                                prop.type'.checkbox
                                                prop.isChecked (not (hidden.Contains c.Name))
                                                prop.disabled (not c.Included)
                                                prop.custom ("aria-label", "Show " + c.Name)
                                                prop.onChange (fun enabled ->
                                                    setHidden (
                                                        if enabled then hidden.Remove c.Name else hidden.Add c.Name
                                                    ))
                                            ]
                                            Html.button [
                                                prop.text (c.Name + (if c.Included then "" else " (excluded)"))
                                                prop.onClick (fun _ -> setSelected c.Name)
                                            ]
                                        ]
                                    ]
                                match value.Components |> List.tryFind (fun c -> c.Name = selected) with
                                | None ->
                                    Html.p [
                                        prop.className "text-sm opacity-60"
                                        prop.text "Select a component for placement and scorer details."
                                    ]
                                | Some c ->
                                    Html.div [
                                        prop.className "scene-details"
                                        prop.children [
                                            Html.h3 c.Name
                                            Html.p ("Parent: " + (c.Parent |> Option.defaultValue "—"))
                                            Html.p ("Material: " + (c.Material |> Option.defaultValue "—"))
                                            Html.p ("Local position (mm): " + position c.Translation)
                                            Html.p ("Local rotation (°): " + position c.Rotation)
                                            Html.p (shapeDescription c.Shape)
                                            if c.Parallel then
                                                Html.p "Parallel scoring geometry"
                                            if c.Invisible then
                                                Html.p "Shown for inspection; invisible in TOPAS graphics."
                                            for scorer in value.Scorers |> List.filter (fun s -> s.Component = c.Name) do
                                                Html.div [
                                                    prop.children [
                                                        Html.strong (scorer.Name + " · " + scorer.Quantity)
                                                        Html.p (
                                                            "Bins: "
                                                            + (scorer.Bins
                                                               |> List.filter (fun (_, count) -> count > 1)
                                                               |> List.map (fun (axis, count) ->
                                                                   axis + "=" + string count)
                                                               |> String.concat " · ")
                                                        )
                                                        Html.p (scorer.Surface |> Option.defaultValue "Volume scorer")
                                                        if not scorer.Active then
                                                            Html.p "Inactive scorer"
                                                    ]
                                                ]
                                            for source in value.Sources |> List.filter (fun s -> s.Component = c.Name) do
                                                Html.p (source.Name + " · " + source.SourceType)
                                        ]
                                    ]
                            ]
                        ]
                        Html.div [
                            prop.className "scene-main"
                            prop.children [
                                createElement (
                                    sceneCanvas,
                                    createObj [
                                        "components" ==> (value.Components |> List.map renderComponent |> List.toArray)
                                        "scorers"
                                        ==> (value.Scorers
                                             |> List.map (fun s ->
                                                 createObj [
                                                     "component" ==> s.Component
                                                     "active" ==> s.Active
                                                     "surface" ==> (s.Surface |> Option.toObj)
                                                     "bins"
                                                     ==> (s.Bins
                                                          |> List.map (fun (axis, count) -> axis, box count)
                                                          |> createObj)
                                                 ])
                                             |> List.toArray)
                                        "sources"
                                        ==> (value.Sources
                                             |> List.map (fun s -> createObj [ "component" ==> s.Component ])
                                             |> List.toArray)
                                        "assets"
                                        ==> (assets
                                             |> Map.toList
                                             |> List.map (fun (name, data) -> name, box data)
                                             |> createObj)
                                        "hidden" ==> (hidden |> Set.toArray)
                                        "selected" ==> selected
                                        "reset" ==> reset
                                        "view" ==> view
                                        "showAxes" ==> axes
                                        "showSources" ==> sources
                                    ]
                                )
                                Html.p [
                                    prop.className "scene-caption"
                                    prop.text
                                        "Drag to orbit · Shift+drag to pan · Scroll to zoom · Coordinates in mm. Source squares indicate local frames, not measured phase-space extents."
                                ]
                            ]
                        ]
                    ]
                ]

                for warning in value.Warnings @ assetErrors do
                    Html.p [ prop.className "scene-warning"; prop.text warning ]
        ]
    ]