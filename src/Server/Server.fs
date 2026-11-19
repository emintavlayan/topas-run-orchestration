module Server

open System
open Microsoft.Extensions.Configuration
open Microsoft.AspNetCore.Http
open FsToolkit.ErrorHandling
open SAFE
open Saturn
open Giraffe
open Shared

/// Builds AppConfigView from Tsebt settings.
let private toAppConfigView (settings: TsebtConfig.TsebtSettings) (seedBase: string) : AppConfigView = {
    AppRoot = settings.AppRoot
    SeedBase = seedBase
    Nodes =
        settings.Nodes
        |> List.map (fun node -> { Name = node.Name; Digit = node.Digit })
    PhaseSpaceFiles =
        settings.PhaseSpaceFiles
        |> List.map (fun file -> {
            Index = file.Index
            Value = file.Value
        })
    Placeholders = [
        settings.Placeholders.PhaseSpaceFile
        settings.Placeholders.OutputFile
        settings.Placeholders.Seed
    ]
}

/// Builds IConfiguration from appsettings.json plus environment variables.
let buildConfiguration () : IConfiguration =
    let basePath = AppContext.BaseDirectory

    ConfigurationBuilder()
        .SetBasePath(basePath)
        .AddJsonFile("appsettings.json", false, false)
        .AddEnvironmentVariables()
        .Build()

/// Loads Tsebt settings from configuration.
let private loadSettings () : Result<TsebtConfig.TsebtSettings, string> =
    buildConfiguration () |> TsebtConfig.load

/// Loads Tsebt settings and resolves the runtime next seed base.
let private loadSettingsWithNextSeedBase () : Result<TsebtConfig.TsebtSettings * string, string> = result {
    let! settings = loadSettings ()
    let! seedBase = SeedBaseStore.getNextSeedBase settings
    return settings, seedBase
}

/// Returns configured app info for the generate flow.
let private getAppConfigHandler () : Async<Result<AppConfigView, string>> = async {
    return result {
        let! settings, seedBase = loadSettingsWithNextSeedBase ()
        return toAppConfigView settings seedBase
    }
}

/// Returns template file metadata from the configured templates root.
let private getTemplateFilesHandler () : Async<Result<TemplateFileInfo list, string>> = async {
    return result {
        let! settings = loadSettings ()
        return! TemplateCatalog.listTemplateFiles settings
    }
}

/// Returns a real generate preview using selected templates and configuration placeholders.
let private previewGenerateHandler (request: GeneratePreviewRequest) : Async<Result<GeneratePreviewResult, string>> = async {
    return result {
        let! settings, seedBase = loadSettingsWithNextSeedBase ()
        return! GeneratePreview.createPreview settings seedBase request
    }
}

/// Executes real generation and returns persisted generated run metadata.
let private generateHandler (request: GenerateRequest) : Async<Result<GenerateResult, string>> = async {
    return result {
        let! settings, seedBase = loadSettingsWithNextSeedBase ()
        return! GenerateOperation.generate settings seedBase request
    }
}

/// Returns run batch summaries from persisted generated metadata.
let private getRunBatchesHandler () : Async<Result<RunBatchSummary list, string>> = async {
    return result {
        let! settings = loadSettings ()
        return! RunOperation.getRunBatches settings
    }
}

/// Returns detailed run batch data by seed base identifier.
let private getRunBatchDetailsHandler (seedBase: string) : Async<Result<RunBatchDetails, string>> = async {
    return result {
        let! settings = loadSettings ()
        return! RunOperation.getRunBatchDetails settings seedBase
    }
}

/// Returns run script preview and preflight for a seed base.
let private previewRunHandler (seedBase: string) : Async<Result<RunScriptPreview, string>> = async {
    return result {
        let! settings = loadSettings ()
        return! RunOperation.previewRun settings seedBase
    }
}

/// Stores submit metadata foundation without Slurm orchestration.
let private submitRunHandler (request: SubmitRunRequest) : Async<Result<SubmitRunResult, string>> = async {
    return result {
        let! settings = loadSettings ()
        return! RunOperation.submitRun settings request
    }
}

/// Returns collect batch summaries from persisted generated metadata.
let private getCollectBatchesHandler () : Async<Result<CollectBatchSummary list, string>> = async {
    return result {
        let! settings = loadSettings ()
        return! CollectOperation.getCollectBatches settings
    }
}

/// Returns detailed collect batch data by seed base identifier.
let private getCollectBatchDetailsHandler (seedBase: string) : Async<Result<CollectBatchDetails, string>> = async {
    return result {
        let! settings = loadSettings ()
        return! CollectOperation.getCollectBatchDetails settings seedBase
    }
}

/// Returns collect preview for one generated batch.
let private previewCollectHandler (request: CollectPreviewRequest) : Async<Result<CollectPreviewResult, string>> = async {
    return result {
        let! settings = loadSettings ()
        return! CollectOperation.previewCollect settings request
    }
}

/// Executes collect workflow for one generated batch.
let private collectBatchHandler (request: CollectRequest) : Async<Result<CollectResult, string>> = async {
    return result {
        let! settings = loadSettings ()
        return! CollectOperation.collectBatch settings request
    }
}

/// Exposes the temporary TSEBT API skeleton.
let topasApi (_: HttpContext) : ITopasApi = {
    listSceneInputs = fun () -> async { return loadSettings () |> Result.bind SceneOperation.listInputs }
    loadScene = fun id -> async { return loadSettings () |> Result.bind (fun settings -> SceneOperation.load settings id) }
    loadSceneAsset = fun request -> async { return loadSettings () |> Result.bind (fun settings -> SceneOperation.loadAsset settings request) }
    getRunControlOverview = fun () -> async {
        match loadSettings () with
        | Error error -> return Error error
        | Ok settings ->
            let enabled = not (String.Equals(buildConfiguration().["Tsebt:Monitoring:NodeTelemetry"],"false",StringComparison.OrdinalIgnoreCase))
            return! RunControlOperation.getOverview settings enabled
    }
    getRunControlBatches = fun () -> async {
        return result {
            let! settings = loadSettings ()
            return! RunControlOperation.listBatches settings
        }
    }
    getRunControl = fun seedBase -> async {
        match loadSettings () with
        | Error error -> return Error error
        | Ok settings ->
            let enabled = not (String.Equals(buildConfiguration().["Tsebt:Monitoring:NodeTelemetry"],"false",StringComparison.OrdinalIgnoreCase))
            return! RunControlOperation.getSnapshot settings enabled seedBase
    }
    getRunControlLog = fun request -> async {
        return result {
            let! settings = loadSettings ()
            return! RunControlOperation.getLog settings request
        }
    }
    getSavedDatasets = fun () -> async { return loadSettings () |> Result.bind DatasetStore.list }
    getSavedDataset = fun id -> async { return loadSettings () |> Result.bind (fun settings -> DatasetStore.load settings id) }
    previewImport = fun files -> async { return CurveImport.preview files }
    saveImportedDataset = fun request -> async { return loadSettings () |> Result.bind (fun settings -> DatasetOperation.import settings request) }
    updateSavedDataset = fun request -> async { return loadSettings () |> Result.bind (fun settings -> DatasetOperation.update settings request) }
    deleteSavedDataset = fun request -> async { return loadSettings () |> Result.bind (fun settings -> DatasetStore.delete settings request) }
    getDatasetOriginal = fun request -> async { return loadSettings () |> Result.bind (fun settings -> DatasetStore.original settings request) }
    listCollectedDatasets = fun () -> async { return loadSettings () |> Result.bind DatasetOperation.listCollections }
    importCollectedDataset = fun key -> async { return loadSettings () |> Result.bind (fun settings ->
        try DatasetOperation.registerCollection settings key with ex -> Error ex.Message) }
    listAnalysisDatasets = fun () -> async { return loadSettings () |> Result.bind DatasetOperation.listAnalysisDatasets }
    loadAnalysisCurve = fun id -> async { return loadSettings () |> Result.bind (fun settings -> DatasetStore.loadAnalysisCurve settings id) }
    saveAnalysisMapping = fun request -> async { return loadSettings () |> Result.bind (fun settings -> DatasetStore.saveMapping settings request) }
    resetAnalysisMapping = fun id -> async { return loadSettings () |> Result.bind (fun settings -> DatasetStore.resetMapping settings id) }
    getAppConfig = getAppConfigHandler
    getTemplateFiles = getTemplateFilesHandler
    previewGenerate = previewGenerateHandler
    generate = generateHandler
    getRunBatches = getRunBatchesHandler
    getRunBatchDetails = getRunBatchDetailsHandler
    previewRun = previewRunHandler
    submitRun = submitRunHandler
    getCollectBatches = getCollectBatchesHandler
    getCollectBatchDetails = getCollectBatchDetailsHandler
    previewCollect = previewCollectHandler
    collectBatch = collectBatchHandler
}

/// Creates the remoting web app for TSEBT API endpoints.
let webApp = Api.make topasApi

/// Returns a minimal server status endpoint.
let healthHandler: HttpHandler = text "Server is running."

/// Creates the Saturn application with only basic middleware and routes.
let app = application {
    use_router (choose [ route "/" >=> healthHandler; webApp ])
    memory_cache
    use_static "public"
    use_gzip
}

/// Runs configuration load, folder bootstrap, and SQLite initialization.
let initializeStartup () : Result<unit, string> = result {
    let! settings = loadSettings ()
    do! Bootstrap.ensureRootFolders settings
    let! _ = SqliteInit.initialize settings
    return ()
}

/// Starts the web server after startup initialization succeeds.
[<EntryPoint>]
let main _ =
    match initializeStartup () with
    | Ok() ->
        run app
        0
    | Error errorMessage ->
        eprintfn "%s" errorMessage
        1

