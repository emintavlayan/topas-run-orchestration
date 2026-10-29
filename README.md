# TopasRunOrchestration

TopasRunOrchestration is a SAFE Stack F# web application that prepares, submits, and collects TOPAS Monte Carlo simulation batches for TSEBT workflows.

## What it does

The application currently supports three end-to-end workflows:

1. Generate
2. Run
3. Collect

At a high level:

- `Generate` builds TOPAS input files from selected templates, nodes, and phase-space files.
- `Run` prepares Slurm manifest/script files and submits the batch with `sbatch`.
- `Collect` reads TOPAS CSV/log outputs, merges node outputs per phase-space, merges the phase-space totals into a final summed dose grid, and computes Type A statistical uncertainty on that summed dose.

## Runtime model

The app runs as a server with browser-based UI clients.

- Server owns file system, SQLite, and orchestration logic.
- Client owns wizard UI state and API calls.
- Shared project owns DTOs and API contracts.

## UI model

- Client UI is styled with Tailwind CSS + daisyUI.
- Generate, Run, and Collect use a shared vertical wizard shell in `src/Client/WizardShell.fs`.
- Generate Review syntax-highlights TOPAS comments, parameter types and paths, values, units, and unresolved placeholders without changing the stitched input text.
- The UI uses CSS-only components (no JavaScript UI library wrappers).

## AppRoot folder structure

The app is driven by one configured root path (`Tsebt:AppRoot`).

```text
templates/   TOPAS source template/component files
inputs/      generated TOPAS input files at inputs/{seedBase}/
runs/        run artifacts and TOPAS run outputs at runs/{seedBase}/
outputs/     collect outputs at outputs/{seedBase}/{timestamp}/ plus latest_collection.txt
database/    SQLite database
logs/        application/runtime logs
```

Notes:

- Phase-space files are not copied or managed by this app.
- Collect reads from shared `runs/{seedBase}` in the current model.
- `runs/{seedBase}` is the immutable source folder for recollection.

## Generate summary

- Seed rule: `seed = seedBase + nodeDigit`
- RunId rule: `seed{seed}_phsp{phaseSpaceIndex}`
- Generated input file: `seed{seed}_phsp{phaseSpaceIndex}.txt`
- Output placeholder target: `{AppRoot}/runs/{seedBase}/{runId}`

Generate is collision-safe and all-or-nothing.

## Run summary

- Runs against one generated batch (`seedBase`).
- Writes:

```text
runs/{seedBase}/run_manifest.tsv
runs/{seedBase}/run_batch.slurm
```

- Manifest columns:
  - task id
  - node name
  - run id
  - input file path
  - log file path

- Slurm execution line:

```bash
srun --nodes=1 --ntasks=1 --nodelist="$NODE_NAME" "$TOPAS" "$INPUT_FILE" > "$LOG_FILE" 2>&1
```

- Double-submit is blocked when already submitted.

## Collect summary

- Discover all CSV scorer suffixes from generated output stems without a fixed scorer-name list.
- Preflight requires the same healthy scorer set for every retained run, with one shared log per run.
- Each scorer is merged and assigned uncertainty independently; preview and result show every scorer.
- Suffix-less CSVs are collected into `default/`.
- Missing CSV blocks collect.
- Missing log is warning-only in current behavior.
- Produces:

```text
outputs/{seedBase}/latest_collection.txt
outputs/{seedBase}/{timestamp}/collect_manifest.tsv
outputs/{seedBase}/{timestamp}/{scorer}/collect_manifest.tsv
outputs/{seedBase}/{timestamp}/{scorer}/merged-over-nodes/phsp{phaseSpaceIndex}_merged.csv
outputs/{seedBase}/{timestamp}/{scorer}/merged-over-phsp/dose_merged.csv
outputs/{seedBase}/{timestamp}/{scorer}/dose_with_uncertainty.csv
```

- Primary final outputs are `merged-over-phsp/dose_merged.csv` and `dose_with_uncertainty.csv`.
- `dose_with_uncertainty.csv` keeps the final summed `dose_to_medium_Gy` and reports `relative_uncertainty_percent` relative to that summed dose:
  `100 * standard_uncertainty_Gy / dose_to_medium_Gy`.
- This uncertainty is for the summed dose, not the standard error of an arithmetic mean dose.

## Recollection policy

- Collect always reads from the existing `runs/{seedBase}/` folder.
- Each collect attempt writes to a new timestamped folder: `outputs/{seedBase}/{timestamp}/`.
- Previous collection folders are retained for audit and comparison and are never overwritten.
- `outputs/{seedBase}/latest_collection.txt` points to the most recent collection folder.
- Recollection is expected when merge logic or uncertainty logic changes.

## Build, run, test

Build:

```powershell
dotnet build Application.sln
```

Run server:

```powershell
dotnet run --project .\src\Server
```

Run tests:

```powershell
dotnet test Application.sln
```

## Deploy from Windows

```powershell
.\scripts\deploy-app.ps1
```

After tests and Bundle have already passed for the current revision:

```powershell
.\scripts\deploy-app.ps1 -SkipBuild
```

Publishes for Linux, uploads, preserves server configuration, backs up the previous application, restarts systemd, and verifies HTTP health. Startup failure restores the previous application. See [deployment instructions](docs/how-to-deploy.md).

## Testing

Server tests are xUnit-based and run via `dotnet test Application.sln`.

Automated tests:

- do not require TOPAS
- do not require Slurm
- use temporary folders and fake CSV/process output fixtures

Manual smoke testing is still required for:

- real Slurm scheduling behavior
- real TOPAS execution runtime behavior
- clinical interpretation of real dose outputs

## Documentation map

Start here for onboarding:

- `docs/onboarding.md`

Workflow behaviour:

- `docs/app-behaviour-spec.md`
- `docs/generate-behaviour-spec.md`
- `docs/run-behaviour-spec.md`
- `docs/collect-behaviour-spec.md`

UX and client flow:

- `docs/wizard-ux-flow-spec.md`

Engineering style:

- `docs/safe-light-fsharp-doctrine.md`

Operations and deployment:

- `docs/how-to-deploy.md`
- `docs/systemd-deployment.md`

Testing:

- `docs/test-coverage.md`

