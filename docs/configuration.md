# Configuration

[README](../README.md) · [All documentation](index.md)

## Two separate locations

| Location | Holds | Example |
|---|---|---|
| Application directory | Published DLLs, browser bundle and `appsettings.json` | `/home/fysiker/WebApps/TopasRunOrchestration` |
| AppRoot | Templates, simulation inputs/results and SQLite metadata | `/srv/cluster/tsebt` |

Deployment replaces the application directory. Simulation data belongs under AppRoot.

## What the server actually loads

[Server.buildConfiguration](../src/Server/Server.fs) loads `appsettings.json` from the server binary directory, then environment variables. Environment variables override JSON values; nested keys use `__`.

The current loader does **not** automatically load `appsettings.Development.json` or `appsettings.Production.json`. The deployment script preserves these files if present, but preservation does not mean the loader uses them.

Example PowerShell development override:

```powershell
$env:Tsebt__AppRoot = 'C:\Dev\tsebt-local-root'
$env:Tsebt__Paths__Database = 'database/app.db'
```

For systemd, set overrides as `Environment=Tsebt__AppRoot=/srv/cluster/tsebt` in the service or an override file. Unit changes require `daemon-reload`; ordinary application updates do not.

## Settings reference

The complete example is [appsettings.json](../src/Server/appsettings.json); validation is implemented in [TsebtConfig.fs](../src/Server/TsebtConfig.fs).

| JSON key under `Tsebt` | Meaning |
|---|---|
| `AppRoot` | Absolute root for application data |
| `Paths.Templates` | Template folder relative to AppRoot |
| `Paths.Inputs` | Generated-input folder |
| `Paths.Runs` | Raw output, log and Slurm folder |
| `Paths.Outputs` | Collected result folder |
| `Paths.Database` | SQLite file relative to AppRoot; use OS-appropriate separators |
| `Paths.Logs` | Application log folder |
| `Placeholders.PhaseSpaceFile` | Token replaced with the chosen phase-space value |
| `Placeholders.OutputFile` | Token replaced with the run output stem |
| `Placeholders.Seed` | Token replaced with the generated seed |
| `Seed.CurrentBase` | Initial numeric batch ID when SQLite has no batches |
| `Topas.Executable` | TOPAS executable or wrapper used by Slurm scripts |
| `Slurm.Partition` | Slurm partition |
| `Slurm.CpusPerTask` | CPUs allocated to each task |
| `Slurm.Account` | Cluster account; omitted from the script when empty |
| `Nodes` | Actual Slurm node names and unique digits |
| `PhaseSpaceFiles` | Unique indices and phase-space values to insert into inputs |

The runtime next batch ID comes from the maximum stored batch ID plus one. The application does not increment the JSON seed setting on disk.

## Cluster requirements

The service account must read/write AppRoot. Compute nodes must resolve input/output paths and phase-space locations. Raw results are written to shared `runs/{batch}`; Collect reads them on the server without SSH copy-back. Source phase spaces are not copied or managed by this application.

Template and CAD deployment is a separate operation from updating this web application. Generated inputs retain the settings used for a batch and must be kept for Analyse geometry detection.
