# Daily workflow

[README](../README.md) · [All documentation](index.md)

Use the four tabs in order: **Generate → Run → Collect → Analyse**. For an already collected batch, start at Analyse.

## Before you start

Open the installed web app. The current server is [http://172.31.245.206:5000](http://172.31.245.206:5000) when reachable from your network. For local development, use [http://localhost:8080](http://localhost:8080) after [starting both development processes](onboarding.md).

The server needs configured templates, node names, phase-space files, TOPAS and Slurm. See [configuration](configuration.md). Phase-space source files stay where TOPAS expects them; the app does not distribute them.

## 1. Generate a batch

1. Open **Generate**, then start the wizard.
2. Select the TOPAS components needed for your simulation, including the required world, physics and run-control components. The app stitches selected components; do not assume a file is included merely because of its folder name.
3. Select nodes and phase-space files.
4. Review the stitched input and file counts. Check geometry, scoring and transport settings as well as placeholder replacement.
5. Generate and record the returned batch ID (`seedBase`).

**You should see:** one generated input for each node/phase-space pair. Seven nodes and 22 phase spaces produce 154 inputs. The next batch ID is derived from SQLite, starting from the configured initial value when the database is empty.

Syntax highlighting helps inspect text; it is not TOPAS geometry or physics validation. [Naming and collision rules →](generate-behaviour-spec.md)

## 2. Submit to Slurm

1. Open **Run** and select the generated batch.
2. Resolve failed preflight checks.
3. Review the Slurm script preview, then submit once.
4. Record the returned job IDs.

The server writes one manifest and script per selected node and calls `sbatch` for each node. Scripts invoke the configured TOPAS wrapper directly; they do not use `srun`.

**Submission is not completion.** Check the cluster scheduler and run logs for execution progress. On the server:

```fish
squeue -u fysiker
```

The app blocks ordinary double submission. If submission failed partway through, inspect Slurm before retrying: earlier node submissions may already have succeeded. [Submission behavior →](run-behaviour-spec.md)

## 3. Collect the results

1. Open **Collect**, select the batch, and inspect preflight.
2. Check the discovered scorer names, CSV/log counts and file issue table.
3. If needed, use the failed-phase-space or failed-node exclusion action. The remaining node set must be balanced across phase spaces.
4. Review all planned scorer outputs, then collect.

**You should see:** one new timestamp folder, containing a separate folder per scorer. Each has node merges, a final summed-dose CSV, an uncertainty CSV and a manifest.

| Raw suffix | Collected scorer folder |
|---|---|
| `_pdd.csv` | `pdd/` |
| `_xprofile.csv` | `xprofile/` |
| `_yprofile.csv` | `yprofile/` |
| `_custom.csv` | `custom/` |
| `.csv` without a suffix | `default/` |

Missing, empty or non-numeric CSVs block collection. Missing or incomplete logs also block it. Do not rename files to collect individual scorers. Old suffix-less copies are included as `default`; remove only copies you deliberately no longer want, while preserving original raw results.

Recollection reads the same raw outputs and creates another timestamp folder. Prior collections are retained. [Files and uncertainty meaning →](data-reference.md) · [Exact collection rules →](collect-behaviour-spec.md)

## 4. Analyse and export

1. Open **Analyse** and refresh the available outputs.
2. Select several collected scorer datasets.
3. Read each geometry-source message. Supported inputs are resolved automatically from the batch's generated input files.
4. For unresolved geometry, inspect the proposed mapping, edit it and explicitly confirm/save before interpreting distances.
5. Choose normalization, metric definitions, evaluation fraction and a reference curve.
6. Compare the separate PDD, X and Y plots; export curves or metrics as CSV.

**You should see:** overlaid curves, optional uncertainty bands, metrics and a difference plot when the selected reference has a matching role. Hover points for values and use physical plot limits to inspect a region.

[Analysis conventions and examples →](analysis-guide.md)

## If a step fails

Keep the batch ID, exact error and relevant paths. Use the [troubleshooting table](troubleshooting.md). Do not discard raw simulation data to clear a preflight error.
