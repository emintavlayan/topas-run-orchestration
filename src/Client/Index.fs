module Index

open Elmish
open GenerateLogic
open GenerateTypes
open RunLogic
open CollectLogic
open SAFE
open Shared

/// Represents the page type used by the Index module.
type Page = GenerateTypes.Page
/// Represents the generate step type used by the Index module.
type GenerateStep = GenerateTypes.GenerateStep
/// Represents the shell theme type used by the Index module.
type ThemeName = GenerateTypes.ThemeName
/// Represents the generate model type used by the Index module.
type GenerateModel = GenerateTypes.GenerateModel
/// Represents the run model type used by the Index module.
type RunModel = GenerateTypes.RunModel
/// Represents the root model type used by the Index module.
type Model = GenerateTypes.Model
/// Represents the message type used by the Index module.
type Msg = GenerateTypes.Msg

let Generate = GenerateTypes.Page.Generate
let Run = GenerateTypes.Page.Run
let Collect = GenerateTypes.Page.Collect

let Welcome = GenerateTypes.GenerateStep.Welcome
let SelectComponents = GenerateTypes.GenerateStep.SelectComponents
let SelectNodes = GenerateTypes.GenerateStep.SelectNodes
let SelectPhaseSpaceFiles = GenerateTypes.GenerateStep.SelectPhaseSpaceFiles
let Review = GenerateTypes.GenerateStep.Review
let Result = GenerateTypes.GenerateStep.Result
let RunWelcome = GenerateTypes.RunStep.RunWelcome
let SelectBatch = GenerateTypes.RunStep.SelectBatch
let PreflightReview = GenerateTypes.RunStep.PreflightReview
let SlurmScriptReview = GenerateTypes.RunStep.SlurmScriptReview
let RunResult = GenerateTypes.RunStep.RunResult
let CollectWelcome = GenerateTypes.CollectStep.CollectWelcome
let CollectSelectBatch = GenerateTypes.CollectStep.CollectSelectBatch
let CollectPreflightReview = GenerateTypes.CollectStep.CollectPreflightReview
let CollectMergeReview = GenerateTypes.CollectStep.CollectMergeReview
let CollectResult = GenerateTypes.CollectStep.CollectResult

let SelectPage = GenerateTypes.Msg.SelectPage
let SelectTheme = GenerateTypes.Msg.SelectTheme
let LoadAppConfig = GenerateTypes.Msg.LoadAppConfig
let LoadTemplateFiles = GenerateTypes.Msg.LoadTemplateFiles
let StartGenerateWizard = GenerateTypes.Msg.StartGenerateWizard
let CancelGenerateWizard = GenerateTypes.Msg.CancelGenerateWizard
let PreviousGenerateStep = GenerateTypes.Msg.PreviousGenerateStep
let NextGenerateStep = GenerateTypes.Msg.NextGenerateStep
let ToggleComponent = GenerateTypes.Msg.ToggleComponent
let ToggleNode = GenerateTypes.Msg.ToggleNode
let TogglePhaseSpaceFile = GenerateTypes.Msg.TogglePhaseSpaceFile
let SelectAllNodes = GenerateTypes.Msg.SelectAllNodes
let SelectNoNodes = GenerateTypes.Msg.SelectNoNodes
let SelectAllPhaseSpaceFiles = GenerateTypes.Msg.SelectAllPhaseSpaceFiles
let SelectNoPhaseSpaceFiles = GenerateTypes.Msg.SelectNoPhaseSpaceFiles
let LoadPreview = GenerateTypes.Msg.LoadPreview
let RunGenerate = GenerateTypes.Msg.RunGenerate
let StartRunWizard = GenerateTypes.Msg.StartRunWizard
let CancelRunWizard = GenerateTypes.Msg.CancelRunWizard
let PreviousRunStep = GenerateTypes.Msg.PreviousRunStep
let NextRunStep = GenerateTypes.Msg.NextRunStep
let SelectRunBatch = GenerateTypes.Msg.SelectRunBatch
let LoadRunBatches = GenerateTypes.Msg.LoadRunBatches
let LoadRunPreview = GenerateTypes.Msg.LoadRunPreview
let SubmitRunBatch = GenerateTypes.Msg.SubmitRunBatch
let StartCollectWizard = GenerateTypes.Msg.StartCollectWizard
let CancelCollectWizard = GenerateTypes.Msg.CancelCollectWizard
let PreviousCollectStep = GenerateTypes.Msg.PreviousCollectStep
let NextCollectStep = GenerateTypes.Msg.NextCollectStep
let SelectCollectBatch = GenerateTypes.Msg.SelectCollectBatch
let ExcludeCollectPhaseSpaces = GenerateTypes.Msg.ExcludeCollectPhaseSpaces
let ExcludeCollectNodes = GenerateTypes.Msg.ExcludeCollectNodes
let LoadCollectBatches = GenerateTypes.Msg.LoadCollectBatches
let LoadCollectPreview = GenerateTypes.Msg.LoadCollectPreview
let RunCollectBatch = GenerateTypes.Msg.RunCollectBatch

/// Creates a proxy for calling server API endpoints.
let topasApi = Api.makeProxy<ITopasApi> ()

/// Returns the initial client model and startup command.
let init () : Model * Cmd<Msg> =
    let model = {
        SelectedPage = Generate
        SelectedTheme = GenerateTypes.ThemeName.Light
        Generate = initialGenerateModel ()
        Run = initialRunModel ()
        Collect = initialCollectModel ()
    }

    let command =
        Cmd.batch [ Cmd.ofMsg (LoadAppConfig(Start())); Cmd.ofMsg (LoadTemplateFiles(Start())) ]

    model, command

/// Loads app configuration through the server API.
let loadAppConfigCmd () : Cmd<Msg> =
    Cmd.OfAsync.perform topasApi.getAppConfig () (Finished >> LoadAppConfig)

/// Loads template files through the server API.
let loadTemplateFilesCmd () : Cmd<Msg> =
    Cmd.OfAsync.perform topasApi.getTemplateFiles () (Finished >> LoadTemplateFiles)

/// Requests a generate preview from the server API.
let loadPreviewCmd (request: GeneratePreviewRequest) : Cmd<Msg> =
    Cmd.OfAsync.perform topasApi.previewGenerate request (Finished >> LoadPreview)

/// Requests generation from the server API.
let runGenerateCmd (request: GenerateRequest) : Cmd<Msg> =
    Cmd.OfAsync.perform topasApi.generate request (Finished >> RunGenerate)

/// Requests run batch list from the server API.
let loadRunBatchesCmd () : Cmd<Msg> =
    Cmd.OfAsync.perform topasApi.getRunBatches () (Finished >> LoadRunBatches)

/// Requests run script preview from the server API.
let loadRunPreviewCmd (seedBase: string) : Cmd<Msg> =
    Cmd.OfAsync.perform topasApi.previewRun seedBase (Finished >> LoadRunPreview)

/// Requests run submission from the server API.
let submitRunBatchCmd (request: SubmitRunRequest) : Cmd<Msg> =
    Cmd.OfAsync.perform topasApi.submitRun request (Finished >> SubmitRunBatch)

/// Requests collect batch list from the server API.
let loadCollectBatchesCmd () : Cmd<Msg> =
    Cmd.OfAsync.perform topasApi.getCollectBatches () (Finished >> LoadCollectBatches)

/// Requests collect preview from the server API.
let loadCollectPreviewCmd (request: CollectPreviewRequest) : Cmd<Msg> =
    Cmd.OfAsync.perform topasApi.previewCollect request (Finished >> LoadCollectPreview)

/// Requests collect execution from the server API.
let runCollectBatchCmd (request: CollectRequest) : Cmd<Msg> =
    Cmd.OfAsync.perform topasApi.collectBatch request (Finished >> RunCollectBatch)

/// Updates model state based on incoming messages.
let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SelectPage page ->
        match page with
        | GenerateTypes.Page.Analyse -> {model with SelectedPage=GenerateTypes.Page.Analyse}, Cmd.none
        | Generate ->
            {
                model with
                    SelectedPage = Generate
                    Generate = initialGenerateModel ()
            },
            Cmd.batch [ Cmd.ofMsg (LoadAppConfig(Start())); Cmd.ofMsg (LoadTemplateFiles(Start())) ]
        | Run ->
            {
                model with
                    SelectedPage = Run
                    Run = initialRunModel ()
            },
            Cmd.ofMsg (LoadRunBatches(Start()))
        | Collect ->
            {
                model with
                    SelectedPage = Collect
                    Collect = initialCollectModel ()
            },
            Cmd.ofMsg (LoadCollectBatches(Start()))
    | SelectTheme theme -> { model with SelectedTheme = theme }, Cmd.none
    | LoadAppConfig call ->
        match call with
        | Start() ->
            {
                model with
                    Generate = {
                        model.Generate with
                            Config = model.Generate.Config.StartLoading()
                            Error = None
                    }
            },
            loadAppConfigCmd ()
        | Finished resultValue ->
            let remoteData, error = setRemoteResult resultValue model.Generate.Config

            {
                model with
                    Generate = {
                        model.Generate with
                            Config = remoteData
                            Error = error
                    }
            },
            Cmd.none
    | LoadTemplateFiles call ->
        match call with
        | Start() ->
            {
                model with
                    Generate = {
                        model.Generate with
                            TemplateFiles = model.Generate.TemplateFiles.StartLoading()
                            Error = None
                    }
            },
            loadTemplateFilesCmd ()
        | Finished resultValue ->
            let remoteData, error = setRemoteResult resultValue model.Generate.TemplateFiles

            {
                model with
                    Generate = {
                        model.Generate with
                            TemplateFiles = remoteData
                            Error = error
                    }
            },
            Cmd.none
    | StartGenerateWizard ->
        {
            model with
                Generate = {
                    model.Generate with
                        Step = SelectComponents
                        Error = None
                }
        },
        Cmd.none
    | CancelGenerateWizard ->
        {
            model with
                Generate = {
                    model.Generate with
                        Step = Welcome
                        Error = None
                }
        },
        Cmd.none
    | PreviousGenerateStep -> handlePreviousStep model
    | NextGenerateStep -> handleNextStep loadPreviewCmd runGenerateCmd model
    | ToggleComponent relativePath ->
        let updated = toggleSelection relativePath model.Generate.SelectedComponents

        {
            model with
                Generate = {
                    model.Generate with
                        SelectedComponents = updated
                        Error = None
                }
        },
        Cmd.none
    | ToggleNode nodeDigit ->
        let updated = toggleSelection nodeDigit model.Generate.SelectedNodes

        {
            model with
                Generate = {
                    model.Generate with
                        SelectedNodes = updated
                        Error = None
                }
        },
        Cmd.none
    | TogglePhaseSpaceFile phaseSpaceIndex ->
        let updated = toggleSelection phaseSpaceIndex model.Generate.SelectedPhaseSpaceFiles

        {
            model with
                Generate = {
                    model.Generate with
                        SelectedPhaseSpaceFiles = updated
                        Error = None
                }
        },
        Cmd.none
    | SelectAllNodes ->
        let allNodes =
            match model.Generate.Config with
            | Loaded config -> config.Nodes |> List.map _.Digit |> Set.ofList
            | _ -> Set.empty

        {
            model with
                Generate = {
                    model.Generate with
                        SelectedNodes = allNodes
                        Error = None
                }
        },
        Cmd.none
    | SelectNoNodes ->
        {
            model with
                Generate = {
                    model.Generate with
                        SelectedNodes = Set.empty
                        Error = None
                }
        },
        Cmd.none
    | SelectAllPhaseSpaceFiles ->
        let allPhaseSpaceFiles =
            match model.Generate.Config with
            | Loaded config -> config.PhaseSpaceFiles |> List.map _.Index |> Set.ofList
            | _ -> Set.empty

        {
            model with
                Generate = {
                    model.Generate with
                        SelectedPhaseSpaceFiles = allPhaseSpaceFiles
                        Error = None
                }
        },
        Cmd.none
    | SelectNoPhaseSpaceFiles ->
        {
            model with
                Generate = {
                    model.Generate with
                        SelectedPhaseSpaceFiles = Set.empty
                        Error = None
                }
        },
        Cmd.none
    | LoadPreview call ->
        match call with
        | Start request ->
            {
                model with
                    Generate = {
                        model.Generate with
                            Preview = model.Generate.Preview.StartLoading()
                            Error = None
                    }
            },
            loadPreviewCmd request
        | Finished resultValue ->
            let remoteData, error = setRemoteResult resultValue model.Generate.Preview

            {
                model with
                    Generate = {
                        model.Generate with
                            Preview = remoteData
                            Error = error
                    }
            },
            Cmd.none
    | RunGenerate call ->
        match call with
        | Start request ->
            {
                model with
                    Generate = {
                        model.Generate with
                            GenerateResult = model.Generate.GenerateResult.StartLoading()
                            Error = None
                    }
            },
            runGenerateCmd request
        | Finished resultValue ->
            match resultValue with
            | Ok generated ->
                {
                    model with
                        Generate = {
                            model.Generate with
                                Step = Result
                                GenerateResult = Loaded generated
                                Error = None
                        }
                },
                Cmd.ofMsg (LoadAppConfig(Start()))
            | Error errorMessage ->
                {
                    model with
                        Generate = {
                            model.Generate with
                                Error = Some errorMessage
                        }
                },
                Cmd.none
    | StartRunWizard ->
        {
            model with
                Run = {
                    model.Run with
                        Step = SelectBatch
                        Error = None
                }
        },
        Cmd.ofMsg (LoadRunBatches(Start()))
    | CancelRunWizard ->
        {
            model with
                Run = initialRunModel ()
        },
        Cmd.none
    | PreviousRunStep ->
        let previousStep =
            match model.Run.Step with
            | RunWelcome -> RunWelcome
            | SelectBatch -> RunWelcome
            | PreflightReview -> SelectBatch
            | SlurmScriptReview -> PreflightReview
            | RunResult -> SlurmScriptReview

        {
            model with
                Run = {
                    model.Run with
                        Step = previousStep
                        Error = None
                }
        },
        Cmd.none
    | NextRunStep ->
        match model.Run.Step with
        | RunWelcome ->
            {
                model with
                    Run = {
                        model.Run with
                            Step = SelectBatch
                            Error = None
                    }
            },
            Cmd.ofMsg (LoadRunBatches(Start()))
        | SelectBatch ->
            match model.Run.SelectedSeedBase with
            | Some seedBase when canProceedBatchSelection model.Run ->
                {
                    model with
                        Run = {
                            model.Run with
                                Step = PreflightReview
                                Preview = model.Run.Preview.StartLoading()
                                Error = None
                        }
                },
                loadRunPreviewCmd seedBase
            | _ ->
                {
                    model with
                        Run = {
                            model.Run with
                                Error = Some "Select one Generated batch before continuing."
                        }
                },
                Cmd.none
        | PreflightReview ->
            if canProceedPreflight model.Run then
                {
                    model with
                        Run = {
                            model.Run with
                                Step = SlurmScriptReview
                                Error = None
                        }
                },
                Cmd.none
            else
                {
                    model with
                        Run = {
                            model.Run with
                                Error = Some "Preflight failed. Submission is blocked."
                        }
                },
                Cmd.none
        | SlurmScriptReview ->
            match model.Run.SelectedSeedBase, model.Run.Preview with
            | Some seedBase, Loaded preview when preview.Preflight.CanSubmit ->
                {
                    model with
                        Run = {
                            model.Run with
                                SubmitResult = model.Run.SubmitResult.StartLoading()
                                Error = None
                        }
                },
                submitRunBatchCmd { SeedBase = seedBase }
            | _ ->
                {
                    model with
                        Run = {
                            model.Run with
                                Error = Some "Cannot submit. Review preflight and batch selection."
                        }
                },
                Cmd.none
        | RunResult ->
            {
                model with
                    Run = initialRunModel ()
            },
            Cmd.ofMsg (LoadRunBatches(Start()))
    | SelectRunBatch seedBase ->
        {
            model with
                Run = {
                    model.Run with
                        SelectedSeedBase = Some seedBase
                        Error = None
                }
        },
        Cmd.none
    | LoadRunBatches call ->
        match call with
        | Start() ->
            {
                model with
                    Run = {
                        model.Run with
                            Batches = model.Run.Batches.StartLoading()
                            Error = None
                    }
            },
            loadRunBatchesCmd ()
        | Finished resultValue ->
            let remoteData, error = setRemoteResult resultValue model.Run.Batches

            {
                model with
                    Run = {
                        model.Run with
                            Batches = remoteData
                            Error = error
                    }
            },
            Cmd.none
    | LoadRunPreview call ->
        match call with
        | Start seedBase ->
            {
                model with
                    Run = {
                        model.Run with
                            SelectedSeedBase = Some seedBase
                            Preview = model.Run.Preview.StartLoading()
                            Error = None
                    }
            },
            loadRunPreviewCmd seedBase
        | Finished resultValue ->
            let remoteData, error = setRemoteResult resultValue model.Run.Preview

            {
                model with
                    Run = {
                        model.Run with
                            Preview = remoteData
                            Error = error
                    }
            },
            Cmd.none
    | SubmitRunBatch call ->
        match call with
        | Start request ->
            {
                model with
                    Run = {
                        model.Run with
                            SubmitResult = model.Run.SubmitResult.StartLoading()
                            Error = None
                    }
            },
            submitRunBatchCmd request
        | Finished resultValue ->
            match resultValue with
            | Ok submitted ->
                {
                    model with
                        Run = {
                            model.Run with
                                Step = RunResult
                                SubmitResult = Loaded submitted
                                Error = None
                        }
                },
                Cmd.ofMsg (LoadRunBatches(Start()))
            | Error errorMessage ->
                {
                    model with
                        Run = {
                            model.Run with
                                Step = RunResult
                                Error = Some errorMessage
                        }
                },
                Cmd.none
    | StartCollectWizard ->
        {
            model with
                Collect = {
                    model.Collect with
                        Step = CollectSelectBatch
                        Error = None
                }
        },
        Cmd.ofMsg (LoadCollectBatches(Start()))
    | CancelCollectWizard ->
        { model with Collect = initialCollectModel () }, Cmd.none
    | PreviousCollectStep ->
        let previousStep =
            match model.Collect.Step with
            | CollectWelcome -> CollectWelcome
            | CollectSelectBatch -> CollectWelcome
            | CollectPreflightReview -> CollectSelectBatch
            | CollectMergeReview -> CollectPreflightReview
            | CollectResult -> CollectMergeReview

        {
            model with
                Collect = {
                    model.Collect with
                        Step = previousStep
                        Error = None
                }
        },
        Cmd.none
    | NextCollectStep ->
        match model.Collect.Step with
        | CollectWelcome ->
            {
                model with
                    Collect = {
                        model.Collect with
                            Step = CollectSelectBatch
                            Error = None
                    }
            },
            Cmd.ofMsg (LoadCollectBatches(Start()))
        | CollectSelectBatch ->
            match model.Collect.SelectedSeedBase with
            | Some seedBase when canProceedCollectBatchSelection model.Collect ->
                {
                    model with
                        Collect = {
                            model.Collect with
                                Step = CollectPreflightReview
                                Preview = model.Collect.Preview.StartLoading()
                                Error = None
                        }
                },
                loadCollectPreviewCmd {
                    SeedBase = seedBase
                    ExcludedPhaseSpaceIndexes = model.Collect.ExcludedPhaseSpaceIndexes
                    ExcludedNodeDigits = model.Collect.ExcludedNodeDigits
                }
            | _ ->
                {
                    model with
                        Collect = {
                            model.Collect with
                                Error = Some "Select a batch before continuing."
                        }
                },
                Cmd.none
        | CollectPreflightReview ->
            if canProceedCollectPreflight model.Collect then
                {
                    model with
                        Collect = {
                            model.Collect with
                                Step = CollectMergeReview
                                Error = None
                        }
                },
                Cmd.none
            else
                {
                    model with
                        Collect = {
                            model.Collect with
                                Error = Some "Preflight failed. Collect is blocked."
                        }
                },
                Cmd.none
        | CollectMergeReview ->
            match model.Collect.CollectResult with
            | Loading _ ->
                {
                    model with
                        Collect = {
                            model.Collect with
                                Error = Some "Collect is already running."
                        }
                },
                Cmd.none
            | _ ->
                match model.Collect.SelectedSeedBase, model.Collect.Preview with
                | Some seedBase, Loaded preview when preview.Preflight.CanCollect ->
                    {
                        model with
                            Collect = {
                                model.Collect with
                                    CollectResult = model.Collect.CollectResult.StartLoading()
                                    Error = None
                            }
                    },
                    runCollectBatchCmd {
                        SeedBase = seedBase
                        ExcludedPhaseSpaceIndexes = model.Collect.ExcludedPhaseSpaceIndexes
                        ExcludedNodeDigits = model.Collect.ExcludedNodeDigits
                    }
                | _ ->
                    {
                        model with
                            Collect = {
                                model.Collect with
                                    Error = Some "Cannot collect. Review preflight and selection."
                            }
                    },
                    Cmd.none
        | CollectResult ->
            { model with Collect = initialCollectModel () }, Cmd.ofMsg (LoadCollectBatches(Start()))
    | SelectCollectBatch seedBase ->
        {
            model with
                Collect = {
                    model.Collect with
                        SelectedSeedBase = Some seedBase
                        ExcludedPhaseSpaceIndexes = []
                        ExcludedNodeDigits = []
                        CollectResult = NotStarted
                        Error = None
                }
        },
        Cmd.none
    | ExcludeCollectPhaseSpaces phaseSpaceIndexes ->
        match model.Collect.SelectedSeedBase with
        | Some seedBase ->
            {
                model with
                    Collect = {
                        model.Collect with
                            ExcludedPhaseSpaceIndexes = phaseSpaceIndexes
                            ExcludedNodeDigits = []
                            Preview = model.Collect.Preview.StartLoading()
                            CollectResult = NotStarted
                            Error = None
                    }
            },
            loadCollectPreviewCmd {
                SeedBase = seedBase
                ExcludedPhaseSpaceIndexes = phaseSpaceIndexes
                ExcludedNodeDigits = []
            }
        | None ->
            {
                model with
                    Collect = {
                        model.Collect with
                            Error = Some "Select a batch before setting exclusions."
                    }
            },
            Cmd.none
    | ExcludeCollectNodes nodeDigits ->
        match model.Collect.SelectedSeedBase with
        | Some seedBase ->
            {
                model with
                    Collect = {
                        model.Collect with
                            ExcludedPhaseSpaceIndexes = []
                            ExcludedNodeDigits = nodeDigits
                            Preview = model.Collect.Preview.StartLoading()
                            CollectResult = NotStarted
                            Error = None
                    }
            },
            loadCollectPreviewCmd {
                SeedBase = seedBase
                ExcludedPhaseSpaceIndexes = []
                ExcludedNodeDigits = nodeDigits
            }
        | None ->
            {
                model with
                    Collect = {
                        model.Collect with
                            Error = Some "Select a batch before setting exclusions."
                    }
            },
            Cmd.none
    | LoadCollectBatches call ->
        match call with
        | Start() ->
            {
                model with
                    Collect = {
                        model.Collect with
                            Batches = model.Collect.Batches.StartLoading()
                            Error = None
                    }
            },
            loadCollectBatchesCmd ()
        | Finished resultValue ->
            let remoteData, error = setRemoteResult resultValue model.Collect.Batches

            {
                model with
                    Collect = {
                        model.Collect with
                            Batches = remoteData
                            Error = error
                    }
            },
            Cmd.none
    | LoadCollectPreview call ->
        match call with
        | Start request ->
            {
                model with
                    Collect = {
                        model.Collect with
                            SelectedSeedBase = Some request.SeedBase
                            Preview = model.Collect.Preview.StartLoading()
                            Error = None
                    }
            },
            loadCollectPreviewCmd request
        | Finished resultValue ->
            let remoteData, error = setRemoteResult resultValue model.Collect.Preview

            {
                model with
                    Collect = {
                        model.Collect with
                            Preview = remoteData
                            Error = error
                    }
            },
            Cmd.none
    | RunCollectBatch call ->
        match call with
        | Start request ->
            match model.Collect.CollectResult with
            | Loading _ -> model, Cmd.none
            | _ ->
                {
                    model with
                        Collect = {
                            model.Collect with
                                CollectResult = model.Collect.CollectResult.StartLoading()
                                Error = None
                        }
                },
                runCollectBatchCmd request
        | Finished resultValue ->
            match resultValue with
            | Ok collected ->
                {
                    model with
                        Collect = {
                            model.Collect with
                                Step = CollectResult
                                CollectResult = Loaded collected
                                Error = None
                        }
                },
                Cmd.ofMsg (LoadCollectBatches(Start()))
            | Error errorMessage ->
                {
                    model with
                        Collect = {
                            model.Collect with
                                Step = CollectResult
                                Error = Some errorMessage
                        }
                },
                Cmd.none

