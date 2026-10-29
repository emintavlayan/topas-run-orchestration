# Collect Behaviour Specification

[README](../README.md) · [All documentation](index.md)

## Purpose

`Collect` consolidates TOPAS run outputs for one generated batch (`seedBase`) with an independent serial pipeline for each discovered scorer:

1. merge over nodes into `merged-over-nodes/`
2. merge over phase-space files into `merged-over-phsp/dose_merged.csv`
3. compute raw-batch uncertainty into `dose_with_uncertainty.csv`

Current cluster model:

- run outputs are read from shared `runs/{seedBase}` under AppRoot
- no node-local copy-back step is required in the current implementation
- phase-space source files are not managed by Collect

## Input path rules

Batch id:

```text
seedBase
```

Run id rule:

```text
seed{seed}_phsp{phaseSpaceIndex}
```

Discovered TOPAS CSVs per run:

```text
{OutputFilePath}{suffix}.csv
```

- Use each generated run's stored `OutputFilePath`; do not reconstruct it from run IDs.
- Discover suffixes from the filesystem with no scorer-name whitelist.
- `_pdd`, `_x`, `_y`, `_xprofile`, `_yprofile`, and future suffixes all use the same pipeline.
- A suffix-less CSV belongs to `default`, including temporary copies left by the old manual collection workflow.
- Remove exactly one leading underscore to name the scorer folder; otherwise preserve the suffix.
- Different suffixes that resolve to the same folder (including case-only differences) block preflight.
- Reject unsafe folder names rather than sanitizing or merging unrelated families.
- Match literal stems; when generated stems overlap, the longest matching stem owns the CSV.
- Ignore files that do not match any generated output stem and files owned only by excluded runs.
- This discovery changes file grouping, not the existing dose CSV schema or aggregation algorithms.

Expected TOPAS log per run:

```text
{AppRoot}/{Paths.Runs}/{seedBase}/seed{seed}_phsp{phaseSpaceIndex}.log
```

## Output folder and files

Collect output folder:

```text
{AppRoot}/{Paths.Outputs}/{seedBase}/{timestamp}
```

Files produced:

```text
{AppRoot}/{Paths.Outputs}/{seedBase}/latest_collection.txt
{AppRoot}/{Paths.Outputs}/{seedBase}/{timestamp}/collect_manifest.tsv
{AppRoot}/{Paths.Outputs}/{seedBase}/{timestamp}/{scorer}/collect_manifest.tsv
{AppRoot}/{Paths.Outputs}/{seedBase}/{timestamp}/{scorer}/merged-over-nodes/phsp{phaseSpaceIndex}_merged.csv (one per phase-space index)
{AppRoot}/{Paths.Outputs}/{seedBase}/{timestamp}/{scorer}/merged-over-phsp/dose_merged.csv
{AppRoot}/{Paths.Outputs}/{seedBase}/{timestamp}/{scorer}/dose_with_uncertainty.csv
```

Timestamp rules:

- `{timestamp}` uses sortable UTC folder names in `yyyyMMddTHHmmss` format
- if the timestamp folder already exists, Collect appends a safe numeric suffix
- `latest_collection.txt` stores the latest collection folder path for portability

## Recollection policy

- `runs/{seedBase}/` is the source folder for Collect; the operator should preserve its contents for reproducibility
- each timestamped output folder is one collection attempt
- recollection is allowed and does not overwrite prior outputs
- previous collection folders are retained for audit and comparison
- recollection is expected when merge logic or uncertainty logic changes

## Preflight behavior

Collect preflight reads `generated_runs` for the selected `seedBase` and checks:

- generated runs exist
- run folder exists
- expected CSV files exist
- expected CSV files are non-empty
- expected CSV files contain at least one numeric TOPAS scorer row
- expected log files exist
- expected logs contain TOPAS completion timing footer markers

Required TOPAS completion markers:

- `Elapsed times:`
- `Parameter Reading`
- `Initialization:`
- `Execution:`
- `Finalization:`
- `Total:`

Important rule:

- Geant4/TOPAS warning-like lines (for example `G4Exception`, `ERROR`, `Exception`) do not fail collect if completion footer markers exist.
- Missing completion footer blocks collect.

## Exclusion behavior

Collect supports exclusion-based recovery from failed runs:

- Exclude phase-space indexes (`ExcludedPhaseSpaceIndexes`)
- Exclude node digits (`ExcludedNodeDigits`)

Both are applied before scorer discovery, health and balance checks.
The union of scorer suffixes found in retained runs is required in every retained run.
Missing, empty or non-numeric CSVs for any scorer block the entire collection.
CSV counts refer to run/scorer files; log counts remain one per run.
If no CSV exists, preflight reports the legacy expected CSV paths as missing.
A scorer absent from every retained run cannot be inferred from files alone.
Current UI allows one mode at a time (phase-space or node exclusion).

After exclusions, collect requires balanced remaining rows:

- each remaining phase-space index must have the same remaining node set
- otherwise collect is blocked with:
  `Remaining collect set is unbalanced. Exclude a full phase-space or full node.`

## Merge behavior

Run all three steps separately within each `{scorer}/` folder. Scorers may have different grids and row counts.

Step 1: merge over nodes

Per phase-space index:

- group generated runs by `phase_space_index`
- read one CSV per node in the group
- require compatible row and column counts
- preserve non-dose columns from the first file
- resolve a recognized dose column from available headers, falling back to the last numeric column when there is no usable header, and sum it across node files
- do not change the existing numeric node-merge behavior
- write `merged-over-nodes/phspXX_merged.csv`

Step 2: merge over phase-space files

Across all files in `merged-over-nodes/`:

- require compatible row and column counts
- require matching voxel coordinates row-by-row
- sum `dose_sum_Gy` voxel-by-voxel across phase-space files
- preserve `x,y,z`
- write `merged-over-phsp/dose_merged.csv`
- output columns:
  - `x`
  - `y`
  - `z`
  - `dose_to_medium_Gy`

## Raw-batch uncertainty behavior

Step 3: compute uncertainty from the independent raw node/phase-space CSV batches.

Important rule:

- uncertainty is computed from the original raw batch CSV files
- the current expected batch count is `7 * 22 = 154`, but the implementation uses the actual raw file count `m`
- uncertainty is not computed from `merged-over-nodes/*.csv`
- uncertainty is not computed from `merged-over-phsp/dose_merged.csv`

For each voxel and raw batch dose value `D_i`:

- `m = number of raw batch files`
- `S1 = sum of D_i`
- `S2 = sum of D_i^2`
- `dose_to_medium_Gy = S1`
- `sample_variance_batch = (S2 - (S1 * S1 / m)) / (m - 1)`
- `batch_standard_deviation = sqrt(max(0.0, sample_variance_batch))`
- `standard_uncertainty_Gy = sqrt(m) * batch_standard_deviation`
- `relative_uncertainty_percent = 100.0 * standard_uncertainty_Gy / S1`

Equivalent relative-uncertainty form:

- `relative_uncertainty_percent = 100.0 * sqrt((m / (m - 1)) * (S2 - S1*S1/m)) / S1`

Edge handling:

- if `S1 <= 0.0`, `relative_uncertainty_percent` is written empty
- if `m < 2`, collect fails because batch uncertainty cannot be estimated
- tiny negative sample variance from floating-point roundoff is clamped to `0.0`
- raw CSV files must keep row order, row count, and voxel coordinates aligned

`dose_with_uncertainty.csv` columns:

- `x`
- `y`
- `z`
- `dose_to_medium_Gy`
- `batch_count`
- `mean_batch_dose_Gy`
- `batch_standard_deviation_Gy`
- `standard_uncertainty_Gy`
- `relative_uncertainty_percent`

Interpretation:

- `dose_to_medium_Gy` remains the summed dose `S1`
- `standard_uncertainty_Gy` is the one-sigma uncertainty of the summed dose, not of the batch mean
- `relative_uncertainty_percent` is relative to the final summed `dose_to_medium_Gy`, not to the batch mean
- equivalently: `relative_uncertainty_percent = 100.0 * standard_uncertainty_Gy / dose_to_medium_Gy`

## Summed dose versus mean dose

Current output semantics:

- the final dose distribution is the summed dose over the independent batch results
- therefore the uncertainty target is the summed dose
- the correct Type A uncertainty is `u(D_sum) = sqrt(m) * s`
- the correct relative uncertainty is `100.0 * u(D_sum) / D_sum`

If output semantics were later changed to arithmetic mean dose instead:

- the dose target would become `D_mean = S1 / m`
- the correct Type A uncertainty would become `u(D_mean) = s / sqrt(m)`
- the relative uncertainty would remain numerically the same only if both numerator and denominator were changed consistently

This distinction matters because using `s / sqrt(m)` while still reporting `D_sum = S1` would understate the uncertainty of the summed dose by a factor of `m`.

## Uncertainty scope

The reported uncertainty is:

- Type A statistical uncertainty from the independent downstream simulations used in Collect

The reported uncertainty is not a full estimate of:

- Type B or model uncertainties from geometry, source model, transport settings, or cross sections
- latent variance risk from finite pre-simulated Varian phase-space source files

Because the source is a pre-simulated phase-space file, latent variance can remain even when downstream statistical uncertainty appears small.

## Collect operation behavior

`collectBatch`:

1. Runs preflight.
2. Rejects when CSV/log/row-balance preflight fails.
3. Creates a new timestamped output folder under `outputs/{seedBase}/`.
4. Rejects output collisions before writing files.
5. For each scorer, merges raw node CSV files into `merged-over-nodes/phspXX_merged.csv`.
6. Merges `merged-over-nodes/*.csv` into `merged-over-phsp/dose_merged.csv`.
7. Computes `dose_with_uncertainty.csv` from the raw batch CSV files.
8. For each scorer, validates that `dose_with_uncertainty.csv` `dose_to_medium_Gy` matches `merged-over-phsp/dose_merged.csv` within floating-point tolerance.
9. Writes a per-scorer `collect_manifest.tsv` and a root manifest containing all scorers, with a `scorer` column. All rows retain the original shared run-level `.log` path.
10. Writes `latest_collection.txt`.
11. Updates `generated_batches` latest collect metadata and appends one `generated_batch_collections` history row.

All-or-nothing for status:

- `collect_status` is set to `Collected` only after every scorer succeeds.
- On scorer failure, the previous successful latest marker and batch metadata remain unchanged; the one attempt history row records the failure.
- Preview and result return `Scorers` with all output, summary, uncertainty and manifest paths. The UI displays every scorer.
- For compatibility with the existing scalar API and database fields, `FinalSummaryPath`, `SummaryPath` and stored summary pointers identify the first scorer in stable suffix order. `OutputFolder` and `latest_collection.txt` identify the common timestamp root.
- `MergedPhaseSpaceCount` remains the number of distinct retained phase spaces; `MergedFiles` includes outputs across all scorers.
- partial files are not auto-deleted on failure in current version.

## SQLite metadata

`generated_batches` collect columns:

- `collect_status` (`NotCollected` default, `Collected` after success)
- `collected_at`
- `collect_output_folder`
- `collect_summary_path`
- `collect_csv_found_count`
- `collect_csv_missing_count`
- `collect_log_found_count`
- `collect_log_missing_count`

`generated_batch_collections` history rows:

- one row per collect execution attempt; read-only preview does not create a history row
- stores `seed_base`, status, timestamps, output folder, summary path, file counts, and error message
- preserves collect history even though `generated_batches` stores only the latest collect metadata

## Current limitations

- CSV parser assumes compatible row/column structure across files being merged.
- When no usable dose header is available, the merge fallback assumes the last numeric column is dose-like.
- Missing/empty/non-numeric CSV files block collect.
- Missing TOPAS completion timing footer blocks collect.
- No manual "collect into an arbitrary existing run folder" workflow yet.


## Implementation boundaries and source links

- [CollectPlanning.fs](../src/Server/CollectPlanning.fs): scans each generated output directory, accepts the exact lower-case `.csv` extension, matches literal stems by longest owner, and sorts suffixes using the normal string order. One leading underscore is removed for the folder name. Upper/lower-case folder collisions are rejected.
- [CollectPreflight.fs](../src/Server/CollectPreflight.fs): the health scan looks for at least one numeric-looking row, not full-file consistency. Found/missing counts actually represent healthy/problematic expected files: empty or malformed files contribute to the reported missing/issue count even if present on disk. FileIssues can include excluded runs for diagnosis; effective counts and collect eligibility exclude them. Footer marker checks are case-insensitive token presence, not a parser proving execution order.
- [CollectCsvMerge.fs](../src/Server/CollectCsvMerge.fs): recognized dose headers take precedence, with last-numeric-column fallback when no usable header exists. Node merging checks shape; it does not independently compare every source coordinate.
- [CollectStatistics.fs](../src/Server/CollectStatistics.fs): subsequent phase-space merging and raw-batch uncertainty calculations perform additional row/coordinate checks. Normal successful preflight is not sufficient to guarantee successful merging.
- [CollectOperation.fs](../src/Server/CollectOperation.fs): creates the history attempt before preflight and processes scorer families sequentially. Failure can leave a timestamp folder with some complete scorer files and some incomplete outputs.
- After all scorer outputs and the root manifest succeed, the implementation writes `latest_collection.txt`, updates latest batch metadata, then marks the history row successful. These effects are sequential and are not rolled back together if a later marker/DB/history operation fails. The previous successful pointers are preserved for failures occurring in scorer processing, not guaranteed for every possible finalization failure.
- Collect does not convert bin coordinates into mm or dose into Gy/MU. Analyse performs the supported geometry mapping separately.

For operators: [daily workflow](daily-workflow.md#3-collect-the-results). For output columns and units: [data reference](data-reference.md).
