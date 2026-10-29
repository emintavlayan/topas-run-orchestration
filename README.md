# TopasRunOrchestration

Prepare, run, collect and compare TOPAS Monte Carlo simulations from one browser app.
Built with F# and SAFE for TSEBT commissioning.

## The whole workflow

| Tab | You do | The app produces |
|---|---|---|
| **Generate** | Select templates, nodes and phase-space files; review the input | One batch ID and a generated TOPAS input per node/phase-space pair |
| **Run** | Select that batch, check preflight, submit | Per-node Slurm arrays and job IDs |
| **Collect** | Check completed CSVs/logs; exclude failed nodes or phase spaces if needed | Separate merged dose and uncertainty files for every discovered scorer |
| **Analyse** | Select collected curves, check detected geometry, choose normalization and metrics | PDD/profile overlays, reference differences, R50 and profile metrics; CSV exports |

**Example:** 7 nodes × 22 phase-space files = 154 runs. Each run can produce `_pdd`, `_xprofile`, `_yprofile`, or any future suffix. One Collect action processes each scorer separately. [Walk through a batch →](docs/daily-workflow.md)

## Pick your starting point

| I want to… | Read this |
|---|---|
| Use the installed application | [Daily workflow](docs/daily-workflow.md) |
| Run a development copy | [Local setup](docs/onboarding.md) |
| Configure paths, nodes and TOPAS | [Configuration](docs/configuration.md) |
| Update the Linux application from Windows | [Deployment](docs/how-to-deploy.md) |
| Understand files, units and batch IDs | [Data and file reference](docs/data-reference.md) |
| Compare curves and understand the formulas | [Analysis guide](docs/analysis-guide.md) |
| Diagnose a failed step | [Troubleshooting](docs/troubleshooting.md) |
| Change the code or verify it | [Developer guide](docs/development.md) |

## Already have the repository and server?

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
| `database/` | SQLite orchestration metadata; exact path is configurable |

Keep generated inputs and raw outputs for reproducibility. Collect can run again into a new timestamp folder. [Exact paths, columns and units →](docs/data-reference.md)

## Interpretation at a glance

- Collection requires healthy CSVs for every retained scorer/run and a successful TOPAS timing footer in every retained log.
- Dose is **summed Gy**, not automatically Gy/MU. Uncertainty describes the summed dose.
- Coordinates remain source bin values until Analyse derives or receives a physical mapping.
- Flatness and symmetry default to ratio definitions: **100% is ideal**. Each formula is independently selectable.
- Analyse supports 1D collected scorers. Measurement import, 3D slicing and gamma are future work.

[Browse all documentation and technical references →](docs/index.md)
