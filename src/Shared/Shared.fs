namespace Shared

type NodeInfo = { Name: string; Digit: string }

type PhaseSpaceFileInfo = { Index: string; Value: string }

type TemplateFileInfo = {
    Group: string
    FileName: string
    RelativePath: string
}

type GeneratePreviewRequest = {
    SelectedTemplatePaths: string list
    SelectedNodeDigits: string list
    SelectedPhaseSpaceIndexes: string list
}

type GeneratePreviewResult = {
    RunId: string
    Seed: string
    InputFileName: string
    OutputFilePath: string
    StitchedPreviewText: string
    ExpectedGeneratedCount: int
}

type GenerateRequest = {
    SelectedTemplatePaths: string list
    SelectedNodeDigits: string list
    SelectedPhaseSpaceIndexes: string list
}

type GeneratedRunInfo = {
    RunId: string
    InputFilePath: string
    OutputFilePath: string
    RunFolder: string
    Seed: string
    NodeDigit: string
    PhaseSpaceIndex: string
}

type GenerateResult = {
    SeedBase: string
    GeneratedInputCount: int
    NodeCount: int
    PhaseSpaceCount: int
    InputFolder: string
    GeneratedRuns: GeneratedRunInfo list
}

type RunBatchSummary = {
    SeedBase: string
    CreatedAt: string
    GeneratedInputCount: int
    NodeCount: int
    PhaseSpaceCount: int
    RunStatus: string
    SlurmJobId: string option
}

type RunManifestRow = {
    RunId: string
    InputFilePath: string
    OutputFilePath: string
    RunFolder: string
    Seed: string
    NodeDigit: string
    PhaseSpaceIndex: string
}

type RunManifestPreviewRow = {
    TaskId: int
    NodeName: string
    RunId: string
    InputFilePath: string
    LogFilePath: string
}

type RunBatchDetails = {
    SeedBase: string
    CreatedAt: string
    GeneratedInputCount: int
    NodeCount: int
    PhaseSpaceCount: int
    RunStatus: string
    SlurmJobId: string option
    ManifestPath: string option
    ScriptPath: string option
    SubmittedAt: string option
    Rows: RunManifestRow list
}

type RunPreflightCheck = {
    Name: string
    Ok: bool
    Message: string option
}

type RunPreflightResult = {
    SeedBase: string
    CanSubmit: bool
    Checks: RunPreflightCheck list
}

type RunNodeScriptPreview = {
    NodeName: string
    ManifestPath: string
    ScriptPath: string
    TaskCount: int
    ManifestRowsPreview: RunManifestPreviewRow list
    ScriptText: string
}

type RunScriptPreview = {
    SeedBase: string
    ManifestPath: string
    ScriptPath: string
    ScriptText: string
    RunCount: int
    ManifestRowsPreview: RunManifestPreviewRow list
    NodeScriptPreviews: RunNodeScriptPreview list
    Preflight: RunPreflightResult
}

type SubmitRunRequest = { SeedBase: string }

type SubmitRunResult = {
    SeedBase: string
    RunStatus: string
    SlurmJobId: string
    SlurmJobIds: string list
    SubmittedRunCount: int
    ManifestPath: string
    ScriptPath: string
    SbatchOutput: string
    SubmittedAt: string
}

type CollectBatchSummary = {
    SeedBase: string
    CreatedAt: string
    GeneratedRunCount: int
    NodeCount: int
    PhaseSpaceCount: int
    RunStatus: string option
    CollectStatus: string
    CollectSummaryPath: string option
    HasCollectedBefore: bool
    LatestCollectionFolder: string option
}

type CollectBatchDetails = {
    SeedBase: string
    CreatedAt: string
    GeneratedRunCount: int
    NodeCount: int
    PhaseSpaceCount: int
    RunStatus: string option
    CollectStatus: string
    CollectedAt: string option
    CollectOutputFolder: string option
    CollectSummaryPath: string option
    CollectCsvFoundCount: int option
    CollectCsvMissingCount: int option
    CollectLogFoundCount: int option
    CollectLogMissingCount: int option
    HasCollectedBefore: bool
    LatestCollectionFolder: string option
}

type CollectPreflightCheck = {
    Name: string
    Ok: bool
    Message: string option
}

type MissingCollectFile = {
    FileKind: string
    Path: string
}

type CollectFileIssue = {
    RunId: string
    PhaseSpaceIndex: string
    NodeDigit: string
    FileKind: string
    Path: string
    Problem: string
    Message: string option
}

type CollectPreflightResult = {
    SeedBase: string
    Scorers: string list
    CanCollect: bool
    ExpectedRunCount: int
    EffectiveRunCount: int
    EffectivePhaseSpaceCount: int
    EffectiveNodeCount: int
    FoundCsvCount: int
    MissingCsvCount: int
    FoundLogCount: int
    MissingLogCount: int
    ExcludedPhaseSpaceIndexes: string list
    ExcludedNodeDigits: string list
    Checks: CollectPreflightCheck list
    MissingFiles: MissingCollectFile list
    FileIssues: CollectFileIssue list
}

type CollectPreviewRequest = {
    SeedBase: string
    ExcludedPhaseSpaceIndexes: string list
    ExcludedNodeDigits: string list
}

type CollectScorerPaths = {
    Scorer: string
    OutputFolder: string
    SummaryPath: string
    UncertaintyPath: string
    ManifestPath: string
}

type CollectPreviewResult = {
    SeedBase: string
    Scorers: CollectScorerPaths list
    ExpectedRunCount: int
    PhaseSpaceCount: int
    NodeCount: int
    OutputFolder: string
    PlannedMergedFiles: string list
    FinalSummaryPath: string
    ManifestPath: string
    Preflight: CollectPreflightResult
    HasCollectedBefore: bool
    LatestCollectionFolder: string option
}

type CollectRequest = {
    SeedBase: string
    ExcludedPhaseSpaceIndexes: string list
    ExcludedNodeDigits: string list
}

type CollectedPhaseSpaceResult = {
    PhaseSpaceIndex: string
    MergedFilePath: string
    SourceCsvCount: int
}

type CollectResult = {
    SeedBase: string
    Scorers: CollectScorerPaths list
    ExpectedRunCount: int
    CsvReadCount: int
    LogFoundCount: int
    MergedPhaseSpaceCount: int
    OutputFolder: string
    SummaryPath: string
    MergedFiles: CollectedPhaseSpaceResult list
    ManifestPath: string
    Status: string
    DatasetRegistration: Result<string, string>
}

type AppConfigView = {
    AppRoot: string
    SeedBase: string
    Nodes: NodeInfo list
    PhaseSpaceFiles: PhaseSpaceFileInfo list
    Placeholders: string list
}

/// Represents one collected scorer available for analysis.
type AnalysisDataset = { Id: string; Label: string }
/// Represents an explicitly confirmed physical coordinate mapping.
type AnalysisMapping = { Role: string; Axis: int; StepMm: float; OriginMm: float; CaxMm: float; Confirmed: bool }
/// Represents a sampled dose and its absolute statistical uncertainty.
type AnalysisPoint = { Position: float; Dose: float; Uncertainty: float }
/// Represents a collected curve with its saved coordinate mapping.
type AnalysisCurve = { Dataset: AnalysisDataset; Mapping: AnalysisMapping option; MappingSource: string; Points: AnalysisPoint list; ValueUnit: string; HasUncertainty: bool }
/// Represents a request to save a physical mapping beside a collected curve.
type SaveAnalysisMapping = { Id: string; Mapping: AnalysisMapping }

/// Represents editable acquisition metadata; empty optional fields mean unknown.
type CurveMetadata = {
    Name: string
    Role: string
    PositionUnit: string
    ValueUnit: string
    DepthMm: string
    SsdMm: string
    FieldXmm: string
    FieldYmm: string
    AcquisitionDate: string
}

/// Represents the original scan identity and recorded manual corrections.
type CurveProvenance = {
    FileIndex: int
    OriginalFile: string
    ScanId: string
    ParserVersion: string
    Corrections: string list
}

/// Represents original numeric samples independently of vendor file structure.
type DatasetCurve = {
    Id: string
    Metadata: CurveMetadata
    Provenance: CurveProvenance
    Mapping: AnalysisMapping
    MappingSource: string
    HasUncertainty: bool
    Points: AnalysisPoint list
}

/// Represents an original uploaded file preserved byte for byte.
type ImportFile = { Name: string; ContentBase64: string }

/// Represents parsed curves awaiting explicit metadata review.
type ImportPreview = { Files: ImportFile list; Curves: DatasetCurve list }

/// Represents a saved dataset version and its curve identities.
type SavedDatasetSummary = {
    Id: string
    Name: string
    Source: string
    AcquisitionDate: string
    SavedAt: string
    CollectionKey: string
    Revision: int
    Curves: AnalysisDataset list
}

/// Represents a dataset loaded for metadata review.
type SavedDataset = { Summary: SavedDatasetSummary; Curves: DatasetCurve list }

/// Represents a reviewed import; the server reparses originals before saving samples.
type SaveDatasetRequest = { Name: string; Source: string; AcquisitionDate: string; Confirmed: bool; Preview: ImportPreview }

/// Represents a metadata edit guarded by the dataset revision.
type UpdateDatasetRequest = {
    Id: string
    Revision: int
    Name: string
    Source: string
    AcquisitionDate: string
    Curves: (string * CurveMetadata) list
}

/// Identifies a dataset version for guarded deletion.
type DeleteDatasetRequest = { Id: string; Revision: int }

/// Identifies an archived original for download.
type DatasetFileRequest = { DatasetId: string; FileIndex: int }

/// Represents the observed state of a single PHSP task.
[<RequireQualifiedAccess>]
type RunTaskState = NotSubmitted | Pending | Running | Suspended | Completed | Failed | Unknown

/// Represents one manifest task and its latest scheduler or log evidence.
type RunControlTask = {
    RunId: string
    PhaseSpaceIndex: string
    NodeName: string
    TaskId: int option
    SlurmTaskId: string option
    State: RunTaskState
    Reason: string option
    StartedAt: string option
    FinishedAt: string option
    ElapsedSeconds: float option
    LogAvailable: bool
}

/// Represents node utilization and progress for the selected batch.
type RunControlNode = {
    NodeName: string
    Tasks: RunControlTask list
    CpuPercent: float option
    MemoryPercent: float option
    MemoryUsedGb: float option
    MemoryTotalGb: float option
    TelemetryAt: string option
    TelemetryError: string option
    CompletedCount: int
    RunningCount: int
    FailedCount: int
    ElapsedSeconds: float option
    RemainingSeconds: float option
    EstimateNote: string
}

/// Represents a cached read-only view of one simulation batch.
type RunControlSnapshot = {
    SeedBase: string
    CapturedAt: string
    Nodes: RunControlNode list
    TotalCount: int
    CompletedCount: int
    RunningCount: int
    FailedCount: int
    RemainingSeconds: float option
    EstimatedFinishAt: string option
    Warnings: string list
}

/// Identifies a generated run whose log tail may be inspected.
type RunControlLogRequest = { SeedBase: string; RunId: string }

/// Identifies a run currently visible in the scheduler on a configured node.
type RunControlActivity = { SeedBase: string; NodeName: string; ArrayIds: string list; State: RunTaskState }

/// Represents the live cluster overview independently of a selected historical run.
type RunControlOverview = {
    CapturedAt: string
    Nodes: RunControlNode list
    Runs: RunControlSnapshot list
    Activities: RunControlActivity list
    Warnings: string list
}

type ITopasApi = {
    getSavedDatasets: unit -> Async<Result<SavedDatasetSummary list, string>>
    getSavedDataset: string -> Async<Result<SavedDataset, string>>
    previewImport: ImportFile list -> Async<Result<ImportPreview, string>>
    saveImportedDataset: SaveDatasetRequest -> Async<Result<SavedDatasetSummary, string>>
    updateSavedDataset: UpdateDatasetRequest -> Async<Result<unit, string>>
    deleteSavedDataset: DeleteDatasetRequest -> Async<Result<unit, string>>
    getDatasetOriginal: DatasetFileRequest -> Async<Result<ImportFile, string>>
    listCollectedDatasets: unit -> Async<Result<AnalysisDataset list, string>>
    importCollectedDataset: string -> Async<Result<SavedDatasetSummary, string>>
    getRunControlOverview: unit -> Async<Result<RunControlOverview, string>>
    getRunControlBatches: unit -> Async<Result<RunBatchSummary list, string>>
    getRunControl: string -> Async<Result<RunControlSnapshot, string>>
    getRunControlLog: RunControlLogRequest -> Async<Result<string, string>>
    listAnalysisDatasets: unit -> Async<Result<AnalysisDataset list, string>>
    loadAnalysisCurve: string -> Async<Result<AnalysisCurve, string>>
    saveAnalysisMapping: SaveAnalysisMapping -> Async<Result<unit, string>>
    resetAnalysisMapping: string -> Async<Result<AnalysisCurve, string>>
    getAppConfig: unit -> Async<Result<AppConfigView, string>>
    getTemplateFiles: unit -> Async<Result<TemplateFileInfo list, string>>
    previewGenerate: GeneratePreviewRequest -> Async<Result<GeneratePreviewResult, string>>
    generate: GenerateRequest -> Async<Result<GenerateResult, string>>
    getRunBatches: unit -> Async<Result<RunBatchSummary list, string>>
    getRunBatchDetails: string -> Async<Result<RunBatchDetails, string>>
    previewRun: string -> Async<Result<RunScriptPreview, string>>
    submitRun: SubmitRunRequest -> Async<Result<SubmitRunResult, string>>
    getCollectBatches: unit -> Async<Result<CollectBatchSummary list, string>>
    getCollectBatchDetails: string -> Async<Result<CollectBatchDetails, string>>
    previewCollect: CollectPreviewRequest -> Async<Result<CollectPreviewResult, string>>
    collectBatch: CollectRequest -> Async<Result<CollectResult, string>>
}

