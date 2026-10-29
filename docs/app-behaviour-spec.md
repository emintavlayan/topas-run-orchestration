# Application behavior and architecture

[README](../README.md) · [All documentation](index.md)

## Implemented workflows

| Workflow | Entry points | Responsibility |
|---|---|---|
| Generate | [GeneratePreview](../src/Server/GeneratePreview.fs), [GenerateOperation](../src/Server/GenerateOperation.fs) | Stitch selected templates, expand node/phase-space pairs, check collisions, write inputs and metadata |
| Run | [RunOperation](../src/Server/RunOperation.fs) | Plan per-node manifests/scripts, run preflight, submit arrays and record accepted job IDs |
| Collect | [CollectOperation](../src/Server/CollectOperation.fs) | Discover independent scorer families, check retained runs, merge and calculate raw-batch uncertainty |
| Analyse | [AnalysisOperation](../src/Server/AnalysisOperation.fs), [AnalysisInput](../src/Server/AnalysisInput.fs), [Analysis](../src/Shared/Analysis.fs) | Load 1D collected curves, derive/override geometry, normalize and calculate metrics |

The [daily workflow](daily-workflow.md) describes operator actions. Individual [behavior specifications](index.md#inspect-exact-behavior) contain precise algorithms and limitations.

## Runtime boundaries

`Shared` owns API contracts and pure curve calculations. `Server` owns file, SQLite and process effects. `Client` owns the Elmish wizards and the separate React analysis workspace. Browser selection does not move raw simulation data to the workstation.

[Server.buildConfiguration](../src/Server/Server.fs) loads the binary directory's `appsettings.json` and environment variables. Startup loads validated settings, creates required AppRoot directories and initializes SQLite. Environment-specific JSON files are not automatically loaded. [Configuration details](configuration.md)

Templates are configured external inputs, not automatically managed by this repo. Phase-space source files remain external as well. Shared run outputs are read directly by Collect; no node copy-back is performed.

## Identity and persistence

A batch is identified by `seedBase`. Seeds concatenate batch ID and node digit; run IDs add the phase-space index. The next batch ID is read from SQLite rather than written back to JSON.

SQLite stores generated batch/run metadata, submission fields, latest collect metadata and collection history. Large simulation outputs remain under AppRoot. Analysis mapping overrides are JSON sidecars beside the selected collected CSV. Metric preferences are browser-local; selected curves are workspace state and are reset when the workspace is remounted.

## Guarantees and limits

| Operation | Implemented guarantee | Boundary |
|---|---|---|
| Generate | Collision validation precedes writes/inserts | Later file/DB effects are not enclosed in one rollback transaction; IO/DB failure can leave partial artifacts |
| Run | Metadata and path checks block ordinary duplicate submission | Submissions happen sequentially per node; earlier accepted jobs are not cancelled if a later submission fails |
| Collect | Missing/unhealthy retained sources and unbalanced rows block processing; each scorer has independent numeric processing | Preflight does not validate every numeric row or the whole CSV geometry; merge/uncertainty stages perform further checks |
| Collect status | Scorer-stage failure does not advance the previous successful latest pointers | Marker writing, latest DB update and history update occur sequentially after output generation, not as one filesystem/DB transaction |
| Analyse | Dataset IDs are resolved against discovered files; physical metrics require confirmed mappings | File discovery is not filtered by collection DB status; partial-failure folders can appear if the expected CSV exists |

Retain inputs, raw outputs, manifests and mappings for reproducibility. They are not protected from manual editing by the application.

## UI and operations

Generate, Run and Collect use the shared wizard shell. Analyse is a separate workspace. Themes are Light, Dark, Corporate, Night and Cyberpunk. The Run UI displays submission metadata; the app does not implement scheduler polling that automatically promotes jobs to Running/Completed.

Deployment is an external operator action using [deploy-app.ps1](../scripts/deploy-app.ps1); it briefly stops the web service, and interrupts web operations such as Collect. It does not cancel independent Slurm jobs.

## Explicitly outside the current implementation

Automatic Slurm status tracking/rerun management, arbitrary-folder manual collection, measured-data import, 3D analysis slicing, gamma, calibrated Gy/MU output, and derived-metric uncertainty propagation are not implemented.
