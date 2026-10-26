module GenerateViews

open Feliz
open GenerateLogic
open GenerateTypes
open WizardShell
open SAFE
open Shared

/// Groups template files by configured folder group.
let groupTemplateFiles (files: TemplateFileInfo list) : (string * TemplateFileInfo list) list =
    files
    |> List.groupBy _.Group
    |> List.sortBy fst
    |> List.map (fun (groupName, groupedFiles) -> groupName, groupedFiles |> List.sortBy _.FileName)

/// Renders a checkbox with label text.
let checkBoxRow (isChecked: bool) (labelText: string) (onToggle: unit -> unit) =
    Html.label [
        prop.className "label cursor-pointer justify-start gap-3 rounded px-2 py-2 hover:bg-base-200"
        prop.children [
            Html.input [
                prop.type'.checkbox
                prop.isChecked isChecked
                prop.className "checkbox checkbox-primary checkbox-sm"
                prop.onChange (fun (_: bool) -> onToggle ())
            ]
            Html.span [ prop.className "label-text"; prop.text labelText ]
        ]
    ]

/// Renders the welcome step.
let viewWelcome (generate: GenerateModel) =
    let seedBase =
        match generate.Config with
        | Loaded config -> config.SeedBase
        | _ -> "..."

    let nodeCount =
        match generate.Config with
        | Loaded config -> config.Nodes.Length
        | _ -> 0

    let phaseSpaceCount =
        match generate.Config with
        | Loaded config -> config.PhaseSpaceFiles.Length
        | _ -> 0

    Html.div [
        Html.p "Generate creates TOPAS input files for one simulation batch."
        Html.p [ prop.className "mt-2"; prop.text $"Current seed base: {seedBase}" ]
        Html.p [ prop.className "mt-1"; prop.text $"Configured nodes: {nodeCount}" ]
        Html.p [
            prop.className "mt-1"
            prop.text $"Configured phase-space files: {phaseSpaceCount}"
        ]
    ]

/// Renders the component selection step.
let viewComponents (generate: GenerateModel) (dispatch: Msg -> unit) =
    match generate.TemplateFiles with
    | NotStarted -> Html.p "Template files not loaded."
    | Loading _ -> Html.p "Loading template files..."
    | Loaded files when files.IsEmpty -> Html.p "No template files available."
    | Loaded files ->
        Html.div [
            prop.className "max-h-full min-h-0"
            prop.children [
                viewScrollPanel [
                    Html.div [
                        for groupName, groupedFiles in groupTemplateFiles files do
                            Html.div [
                                prop.className "mb-4"
                                prop.children [
                                    Html.h3 [ prop.className "font-semibold"; prop.text groupName ]
                                    Html.div [
                                        prop.className "mt-2"
                                        prop.children [
                                            for templateFile in groupedFiles do
                                                checkBoxRow
                                                    (generate.SelectedComponents.Contains templateFile.RelativePath)
                                                    templateFile.FileName
                                                    (fun () -> dispatch (ToggleComponent templateFile.RelativePath))
                                        ]
                                    ]
                                ]
                            ]
                    ]
                ]
            ]
        ]

/// Renders the node selection step.
let viewNodes (generate: GenerateModel) (dispatch: Msg -> unit) =
    match generate.Config with
    | Loaded config ->
        Html.div [
            Html.div [
                prop.className "mb-3 join"
                prop.children [
                    Html.button [
                        prop.className "join-item btn btn-outline btn-sm"
                        prop.text "Select all"
                        prop.onClick (fun _ -> dispatch SelectAllNodes)
                    ]
                    Html.button [
                        prop.className "join-item btn btn-outline btn-sm"
                        prop.text "Select none"
                        prop.onClick (fun _ -> dispatch SelectNoNodes)
                    ]
                ]
            ]
            Html.div [
                prop.className "max-h-full min-h-0"
                prop.children [
                    viewScrollPanel [
                        Html.div [
                            prop.className "grid gap-1 md:grid-cols-2"
                            prop.children [
                                for node in config.Nodes |> List.sortBy _.Digit do
                                    checkBoxRow (generate.SelectedNodes.Contains node.Digit) $"{node.Digit} {node.Name}" (fun () ->
                                        dispatch (ToggleNode node.Digit))
                            ]
                        ]
                    ]
                ]
            ]
        ]
    | _ -> Html.p "Loading nodes..."

/// Renders the phase-space selection step.
let viewPhaseSpaceFiles (generate: GenerateModel) (dispatch: Msg -> unit) =
    match generate.Config with
    | Loaded config ->
        Html.div [
            Html.div [
                prop.className "mb-3 join"
                prop.children [
                    Html.button [
                        prop.className "join-item btn btn-outline btn-sm"
                        prop.text "Select all"
                        prop.onClick (fun _ -> dispatch SelectAllPhaseSpaceFiles)
                    ]
                    Html.button [
                        prop.className "join-item btn btn-outline btn-sm"
                        prop.text "Select none"
                        prop.onClick (fun _ -> dispatch SelectNoPhaseSpaceFiles)
                    ]
                ]
            ]
            Html.div [
                prop.className "max-h-full min-h-0"
                prop.children [
                    viewScrollPanel [
                        Html.div [
                            prop.className "grid gap-1 pr-2 md:grid-cols-2"
                            prop.children [
                                for phaseSpaceFile in config.PhaseSpaceFiles |> List.sortBy _.Index do
                                    checkBoxRow
                                        (generate.SelectedPhaseSpaceFiles.Contains phaseSpaceFile.Index)
                                        $"ps{phaseSpaceFile.Index}"
                                        (fun () -> dispatch (TogglePhaseSpaceFile phaseSpaceFile.Index))
                            ]
                        ]
                    ]
                ]
            ]
        ]
    | _ -> Html.p "Loading phase-space files..."

/// Renders the review step.
let viewReview (generate: GenerateModel) =
    let selectedComponents = generate.SelectedComponents |> Seq.sort |> List.ofSeq

    let selectedNodes =
        generate.SelectedNodes
        |> Seq.sort
        |> Seq.map (fun digit -> $"n{digit}")
        |> String.concat ", "

    let selectedPhaseSpaces =
        generate.SelectedPhaseSpaceFiles
        |> Seq.sort
        |> Seq.map (fun index -> $"ps{index}")
        |> String.concat ", "

    Html.div [
        Html.div [
            prop.className "mt-3"
            prop.children [
                Html.p [ prop.className "font-medium"; prop.text "Selected components:" ]
                if selectedComponents.IsEmpty then
                    Html.p [
                        prop.className "mt-1 text-sm text-base-content/70"
                        prop.text "No components selected."
                    ]
                else
                    Html.div [
                        prop.className "mt-1 max-h-[40vh]"
                        prop.children [
                            viewScrollPanel [
                                Html.ul [
                                    prop.className "list-disc pl-6"
                                    prop.children [
                                        for componentPath in selectedComponents do
                                            Html.li [ prop.className "break-all"; prop.text componentPath ]
                                    ]
                                ]
                            ]
                        ]
                    ]
            ]
        ]
        Html.div [
            prop.className "mt-4"
            prop.children [
                Html.p [ prop.className "font-medium"; prop.text "Selected nodes:" ]
                Html.ul [
                    prop.className "mt-1 list-disc pl-6"
                    prop.children [ Html.li selectedNodes ]
                ]
            ]
        ]
        Html.div [
            prop.className "mt-4"
            prop.children [
                Html.p [ prop.className "font-medium"; prop.text "Selected phase-space files:" ]
                Html.ul [
                    prop.className "mt-1 list-disc pl-6"
                    prop.children [ Html.li selectedPhaseSpaces ]
                ]
            ]
        ]
        Html.h4 [ prop.className "mt-4 font-semibold"; prop.text "Preview" ]
        match generate.Preview with
        | NotStarted -> Html.p "No preview available."
        | Loading(Some preview) ->
            Html.div [
                prop.className "mt-2"
                prop.children [ TopasPreview.view preview.StitchedPreviewText ]
            ]
        | Loading _ -> Html.p "Loading preview..."
        | Loaded preview ->
            Html.div [
                prop.children [
                    Html.p [
                        prop.className "mt-2 text-sm"
                        prop.text $"Expected generated count: {preview.ExpectedGeneratedCount}"
                    ]
                    Html.div [
                        prop.className "mt-2"
                        prop.children [ TopasPreview.view preview.StitchedPreviewText ]
                    ]
                ]
            ]
    ]

/// Renders the result step.
let viewResult (generate: GenerateModel) =
    match generate.GenerateResult with
    | NotStarted -> Html.p "No generation result yet."
    | Loading _ -> Html.p "Generating..."
    | Loaded generated ->
        Html.div [
            Html.p [ prop.text $"Seed base: {generated.SeedBase}" ]
            Html.p [
                prop.className "mt-1"
                prop.text $"Generated files: {generated.GeneratedInputCount}"
            ]
            Html.p [ prop.className "mt-1"; prop.text $"Input folder: {generated.InputFolder}" ]
            Html.h4 [ prop.className "mt-4 font-semibold"; prop.text "Generated runs" ]
            if generated.GeneratedRuns.IsEmpty then
                Html.p [ prop.className "mt-2"; prop.text "No generated runs returned yet." ]
            else
                Html.div [
                    prop.className "mt-2 max-h-[45vh]"
                    prop.children [
                        viewScrollPanel [
                            Html.ul [
                                prop.className "list-disc pl-6"
                                prop.children [
                                    for run in generated.GeneratedRuns do
                                        Html.li [
                                            prop.className "break-all"
                                            prop.text $"{run.RunId} | input: {run.InputFilePath} | output: {run.OutputFilePath} | run folder: {run.RunFolder}"
                                        ]
                                ]
                            ]
                        ]
                    ]
                ]
        ]

/// Renders wizard step content.
let viewGenerateStep (generate: GenerateModel) (dispatch: Msg -> unit) =
    match generate.Step with
    | Welcome -> viewWelcome generate
    | SelectComponents -> viewComponents generate dispatch
    | SelectNodes -> viewNodes generate dispatch
    | SelectPhaseSpaceFiles -> viewPhaseSpaceFiles generate dispatch
    | Review -> viewReview generate
    | Result -> viewResult generate

/// Renders wizard navigation controls.
let viewGeneratePage (generate: GenerateModel) (dispatch: Msg -> unit) =
    let steps =
        [
            { Title = "Welcome"; Description = "Review what Generate will create." }
            { Title = "Components"; Description = "Choose TOPAS template components." }
            { Title = "Nodes"; Description = "Choose compute nodes." }
            { Title = "Phase-space"; Description = "Choose phase-space files." }
            { Title = "Review"; Description = "Review one stitched input preview." }
            { Title = "Result"; Description = "Review generated files." }
        ]
    let currentStepIndex =
        generate.Step
        |> function
            | Welcome -> 0
            | SelectComponents -> 1
            | SelectNodes -> 2
            | SelectPhaseSpaceFiles -> 3
            | Review -> 4
            | Result -> 5
    viewWizardShell
        steps
        currentStepIndex
        (viewGenerateStep generate dispatch)
        generate.Error
        (showPreviousButton generate.Step)
        (primaryButtonText generate.Step)
        (disablePrimaryButton generate)
        (fun () -> dispatch CancelGenerateWizard)
        (fun () -> dispatch PreviousGenerateStep)
        (fun () -> onPrimaryClick generate dispatch)
