module GenerateLogic

open Elmish
open GenerateTypes
open SAFE
open Shared

/// Creates the initial wizard model.
let initialGenerateModel () : GenerateModel = {
    Step = Welcome
    Config = NotStarted
    TemplateFiles = NotStarted
    SelectedComponents = Set.empty
    SelectedNodes = Set.empty
    SelectedPhaseSpaceFiles = Set.empty
    Preview = NotStarted
    GenerateResult = NotStarted
    Error = None
}

/// Maps a Result value into remote data and error state.
let setRemoteResult (resultValue: Result<'a, string>) (loadingState: RemoteData<'a>) : RemoteData<'a> * string option =
    match resultValue with
    | Ok value -> Loaded value, None
    | Error errorMessage -> loadingState, Some errorMessage

/// Returns the first selected node digit for preview.
let firstSelectedNode (generate: GenerateModel) : string option =
    generate.SelectedNodes |> Seq.sort |> Seq.tryHead

/// Returns the first selected phase-space index for preview.
let firstSelectedPhaseSpaceIndex (generate: GenerateModel) : string option =
    generate.SelectedPhaseSpaceFiles |> Seq.sort |> Seq.tryHead

/// Builds a preview request from current wizard selection.
let buildPreviewRequest (generate: GenerateModel) : Result<GeneratePreviewRequest, string> =
    match firstSelectedNode generate, firstSelectedPhaseSpaceIndex generate with
    | Some _, Some _ ->
        Ok {
            SelectedTemplatePaths = generate.SelectedComponents |> Seq.sort |> List.ofSeq
            SelectedNodeDigits = generate.SelectedNodes |> Seq.sort |> List.ofSeq
            SelectedPhaseSpaceIndexes = generate.SelectedPhaseSpaceFiles |> Seq.sort |> List.ofSeq
        }
    | _ -> Error "Select at least one node and one phase-space file before preview."

/// Builds a generate request from current wizard selection.
let buildGenerateRequest (generate: GenerateModel) : GenerateRequest = {
    SelectedTemplatePaths = generate.SelectedComponents |> Seq.sort |> List.ofSeq
    SelectedNodeDigits = generate.SelectedNodes |> Seq.sort |> List.ofSeq
    SelectedPhaseSpaceIndexes = generate.SelectedPhaseSpaceFiles |> Seq.sort |> List.ofSeq
}

/// Returns true when component selection is valid for next step.
let canProceedComponents (generate: GenerateModel) : bool = not generate.SelectedComponents.IsEmpty

/// Returns true when node selection is valid for next step.
let canProceedNodes (generate: GenerateModel) : bool = not generate.SelectedNodes.IsEmpty

/// Returns true when phase-space selection is valid for next step.
let canProceedPhaseSpaceFiles (generate: GenerateModel) : bool =
    not generate.SelectedPhaseSpaceFiles.IsEmpty

/// Handles transitions when the next wizard button is pressed.
let handleNextStep
    (loadPreviewCmd: GeneratePreviewRequest -> Cmd<Msg>)
    (runGenerateCmd: GenerateRequest -> Cmd<Msg>)
    (model: Model)
    : Model * Cmd<Msg> =
    match model.Generate.Step with
    | Welcome ->
        {
            model with
                Generate = {
                    model.Generate with
                        Step = SelectComponents
                        Error = None
                }
        },
        Cmd.none
    | SelectComponents when canProceedComponents model.Generate ->
        {
            model with
                Generate = {
                    model.Generate with
                        Step = SelectNodes
                        Error = None
                }
        },
        Cmd.none
    | SelectComponents ->
        {
            model with
                Generate = {
                    model.Generate with
                        Error = Some "Select at least one component."
                }
        },
        Cmd.none
    | SelectNodes when canProceedNodes model.Generate ->
        {
            model with
                Generate = {
                    model.Generate with
                        Step = SelectPhaseSpaceFiles
                        Error = None
                }
        },
        Cmd.none
    | SelectNodes ->
        {
            model with
                Generate = {
                    model.Generate with
                        Error = Some "Select at least one node."
                }
        },
        Cmd.none
    | SelectPhaseSpaceFiles when canProceedPhaseSpaceFiles model.Generate ->
        match buildPreviewRequest model.Generate with
        | Ok previewRequest ->
            let updatedModel = {
                model with
                    Generate = {
                        model.Generate with
                            Step = Review
                            Preview = model.Generate.Preview.StartLoading()
                            Error = None
                    }
            }

            updatedModel, loadPreviewCmd previewRequest
        | Error errorMessage ->
            {
                model with
                    Generate = {
                        model.Generate with
                            Error = Some errorMessage
                    }
            },
            Cmd.none
    | SelectPhaseSpaceFiles ->
        {
            model with
                Generate = {
                    model.Generate with
                        Error = Some "Select at least one phase-space file."
                }
        },
        Cmd.none
    | Review ->
        let request = buildGenerateRequest model.Generate

        let updatedModel = {
            model with
                Generate = {
                    model.Generate with
                        GenerateResult = model.Generate.GenerateResult.StartLoading()
                        Error = None
                }
        }

        updatedModel, runGenerateCmd request
    | Result -> model, Cmd.none

/// Handles transitions when the previous wizard button is pressed.
let handlePreviousStep (model: Model) : Model * Cmd<Msg> =
    let previousStep =
        match model.Generate.Step with
        | Welcome -> Welcome
        | SelectComponents -> Welcome
        | SelectNodes -> SelectComponents
        | SelectPhaseSpaceFiles -> SelectNodes
        | Review -> SelectPhaseSpaceFiles
        | Result -> Review

    {
        model with
            Generate = {
                model.Generate with
                    Step = previousStep
                    Error = None
            }
    },
    Cmd.none

/// Toggles a selected value in a set.
let toggleSelection (value: string) (selection: Set<string>) : Set<string> =
    if selection.Contains value then
        selection.Remove value
    else
        selection.Add value

/// Returns display text for a top-level page tab.
let pageLabel (page: Page) : string =
    match page with
    | Generate -> "Generate"
    | Run -> "Run"
    | Collect -> "Collect"
    | Analyse -> "Analyse"

/// Returns display text for a wizard step heading.
let stepLabel (step: GenerateStep) : string =
    match step with
    | Welcome -> "Welcome"
    | SelectComponents -> "Select TOPAS Components"
    | SelectNodes -> "Select Nodes"
    | SelectPhaseSpaceFiles -> "Select Phase-Space Files"
    | Review -> "Review"
    | Result -> "Result"

/// Returns true when previous navigation should be shown.
let showPreviousButton (step: GenerateStep) : bool =
    match step with
    | Welcome -> false
    | _ -> true

/// Returns text for the primary navigation button.
let primaryButtonText (step: GenerateStep) : string =
    match step with
    | Welcome -> "Start"
    | Review -> "Generate"
    | Result -> "Back to Generate"
    | _ -> "Next"

/// Returns true when primary button should be disabled.
let disablePrimaryButton (generate: GenerateModel) : bool =
    match generate.Step with
    | SelectComponents -> not (canProceedComponents generate)
    | SelectNodes -> not (canProceedNodes generate)
    | SelectPhaseSpaceFiles -> not (canProceedPhaseSpaceFiles generate)
    | Review ->
        match generate.Preview with
        | Loaded _ -> false
        | _ -> true
    | _ -> false

/// Handles primary button click behavior.
let onPrimaryClick (generate: GenerateModel) (dispatch: Msg -> unit) =
    match generate.Step with
    | Result -> dispatch CancelGenerateWizard
    | _ -> dispatch NextGenerateStep
