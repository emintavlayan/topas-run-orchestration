module GenerateTypes

open SAFE
open Shared

/// Represents the available top-level pages in the client.
type Page =
    | Generate
    | Run
    | Collect
    | Analyse

/// Represents the visual theme selected for the client shell.
type ThemeName =
    | Light
    | Dark
    | Corporate
    | Night
    | Cyberpunk

/// Represents the steps in the generate wizard flow.
type GenerateStep =
    | Welcome
    | SelectComponents
    | SelectNodes
    | SelectPhaseSpaceFiles
    | Review
    | Result

/// Represents the steps in the run wizard flow.
type RunStep =
    | RunWelcome
    | SelectBatch
    | PreflightReview
    | SlurmScriptReview
    | RunResult

/// Represents the steps in the collect wizard flow.
type CollectStep =
    | CollectWelcome
    | CollectSelectBatch
    | CollectPreflightReview
    | CollectMergeReview
    | CollectResult

/// Represents the state of the generate wizard.
type GenerateModel = {
    Step: GenerateStep
    Config: RemoteData<AppConfigView>
    TemplateFiles: RemoteData<TemplateFileInfo list>
    SelectedComponents: Set<string>
    SelectedNodes: Set<string>
    SelectedPhaseSpaceFiles: Set<string>
    Preview: RemoteData<GeneratePreviewResult>
    GenerateResult: RemoteData<GenerateResult>
    Error: string option
}

/// Represents the state of the run wizard.
type RunModel = {
    Step: RunStep
    Batches: RemoteData<RunBatchSummary list>
    SelectedSeedBase: string option
    Preview: RemoteData<RunScriptPreview>
    SubmitResult: RemoteData<SubmitRunResult>
    Error: string option
}

/// Represents the state of the collect wizard.
type CollectModel = {
    Step: CollectStep
    Batches: RemoteData<CollectBatchSummary list>
    SelectedSeedBase: string option
    ExcludedPhaseSpaceIndexes: string list
    ExcludedNodeDigits: string list
    Preview: RemoteData<CollectPreviewResult>
    CollectResult: RemoteData<CollectResult>
    Error: string option
}

/// Represents the root client model.
type Model = {
    SelectedPage: Page
    SelectedTheme: ThemeName
    Generate: GenerateModel
    Run: RunModel
    Collect: CollectModel
}

/// Represents messages that can update the client model.
type Msg =
    | SelectPage of Page
    | SelectTheme of ThemeName
    | LoadAppConfig of ApiCall<unit, Result<AppConfigView, string>>
    | LoadTemplateFiles of ApiCall<unit, Result<TemplateFileInfo list, string>>
    | StartGenerateWizard
    | CancelGenerateWizard
    | PreviousGenerateStep
    | NextGenerateStep
    | ToggleComponent of string
    | ToggleNode of string
    | TogglePhaseSpaceFile of string
    | SelectAllNodes
    | SelectNoNodes
    | SelectAllPhaseSpaceFiles
    | SelectNoPhaseSpaceFiles
    | LoadPreview of ApiCall<GeneratePreviewRequest, Result<GeneratePreviewResult, string>>
    | RunGenerate of ApiCall<GenerateRequest, Result<GenerateResult, string>>
    | StartRunWizard
    | CancelRunWizard
    | PreviousRunStep
    | NextRunStep
    | SelectRunBatch of string
    | LoadRunBatches of ApiCall<unit, Result<RunBatchSummary list, string>>
    | LoadRunPreview of ApiCall<string, Result<RunScriptPreview, string>>
    | SubmitRunBatch of ApiCall<SubmitRunRequest, Result<SubmitRunResult, string>>
    | StartCollectWizard
    | CancelCollectWizard
    | PreviousCollectStep
    | NextCollectStep
    | SelectCollectBatch of string
    | ExcludeCollectPhaseSpaces of string list
    | ExcludeCollectNodes of string list
    | LoadCollectBatches of ApiCall<unit, Result<CollectBatchSummary list, string>>
    | LoadCollectPreview of ApiCall<CollectPreviewRequest, Result<CollectPreviewResult, string>>
    | RunCollectBatch of ApiCall<CollectRequest, Result<CollectResult, string>>

