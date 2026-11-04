# TopasRunOrchestration

Prepare, run, collect and compare TOPAS Monte Carlo simulations from one browser app.
Built with F# and SAFE for TSEBT commissioning.

## The whole workflow

| Tab | You do | The app produces |
|---|---|---|
| **Generate** | Select templates, nodes and phase-space files; review the input | One batch ID and a generated TOPAS input per node/phase-space pair |
| **Run** | Select that batch, check preflight, submit | Per-node Slurm arrays and job IDs |
| **Run Control** | Follow nodes, tasks, utilization and progress | Live Slurm states, TOPAS logs and approximate remaining time |
| **Collect** | Check completed CSVs/logs; exclude failed nodes or phase spaces if needed | Separate merged dose/uncertainty files and an automatically registered 1D dataset version |
| **Import** | Choose measurement files or an existing collection, review metadata, save | Named datasets with original files and independent curves |
| **Analyse** | Select saved datasets/curves, check geometry, choose normalization and metrics | PDD/profile overlays, R50 and profile metrics; CSV exports |

**Example:** 7 nodes × 22 phase-space files = 154 runs. Each run can produce `_pdd`, `_xprofile`, `_yprofile`, or any future suffix. One Collect action processes each scorer separately. [Walk through a batch →](docs/daily-workflow.md)

## Pick your starting point

| I want to… | Read this |
|---|---|
| Use the installed application | [Daily workflow](docs/daily-workflow.md) |
| Install required versions and choose local/server hosting | [Installation manual](docs/installation.md) |
| Run a development copy on Windows or Linux | [Local setup](docs/onboarding.md) |
| Configure paths, nodes and TOPAS | [Configuration](docs/configuration.md) |
| Update the Linux application from Windows | [Deployment](docs/how-to-deploy.md) |
| Keep the app running on Ubuntu after logout/reboot | [systemd service setup](docs/systemd-deployment.md) |
| Host the published app on Windows | [Windows deployment](docs/windows-deployment.md) |
| Understand files, units and batch IDs | [Data and file reference](docs/data-reference.md) |
| Monitor running simulations and configure CPU/RAM probes | [Run Control guide](docs/run-control-guide.md) |
| Import measurements or manage saved datasets | [Import guide](docs/import-guide.md) |
| Compare curves and understand the formulas | [Analysis guide](docs/analysis-guide.md) |
| Diagnose a failed step | [Troubleshooting](docs/troubleshooting.md) |
| Change the code or verify it | [Developer guide](docs/development.md) |

## Already have the repository and server?

Build tools: **.NET 8 SDK + Node 20/npm 10**. `global.json` selects .NET 8. The build prepares the pinned Node 20.20.2/npm 10 environment automatically, using fnm when needed. Newer installations can coexist; shell profile setup is unnecessary. [One-time setup →](docs/installation.md)

In **Windows PowerShell**, from the repository root on the branch containing the changes:

```powershell
git pull --ff-only
.\scripts\deploy-app.ps1
```

The script tests, bundles, publishes for Linux, uploads, preserves production configuration, backs up the installed app, restarts systemd and checks HTTP health. It attempts rollback on startup failure. [Details and `-SkipBuild` usage →](docs/how-to-deploy.md)

## Where the data lives

The web app runs on the server and reads the configured **AppRoot**. Browsers do not need local simulation files.

| Location under AppRoot | Purpose |
|---|---|
| `templates/` | TOPAS source components |
| `inputs/{batch}/` | Generated inputs; also the source for analysis geometry detection |
| `runs/{batch}/` | Raw scorer CSVs, run logs and Slurm files |
| `outputs/{batch}/{timestamp}/{scorer}/` | Independent collected results per scorer |
| `database/` | SQLite orchestration metadata, saved datasets, samples and original-file bytes; back up this database |

Keep generated inputs and raw outputs for reproducibility. Collect can run again into a new timestamp folder. [Exact paths, columns and units →](docs/data-reference.md)

## Interpretation at a glance

- Collection requires healthy CSVs for every retained scorer/run and a successful TOPAS timing footer in every retained log.
- Dose is **summed Gy**, not automatically Gy/MU. Uncertainty describes the summed dose.
- Coordinates remain source bin values until Analyse derives or receives a physical mapping.
- Flatness and symmetry default to ratio definitions: **100% is ideal**. Each formula is independently selectable.
- Analyse compares saved measurement and simulation curves. Import supports simple dose-column text/CSV and conservative MCC scan blocks; native SNC parsing awaits actual examples. [Formats and limitations →](docs/import-guide.md)
- 3D slicing and gamma remain future work.

[Browse all documentation and technical references →](docs/index.md)

### Geometry inspection

The **Geometry** tab previews generated run inputs in 3D, including geometry ownership, STL components, source frames and scorer volumes. See the [geometry viewer guide](docs/geometry-viewer.md).
