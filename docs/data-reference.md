# Data and file reference

[README](../README.md) · [All documentation](index.md)

## Identity: batch, run, collection, scorer

| Term | Example | Meaning |
|---|---|---|
| Batch / `seedBase` | `1113` | One generated simulation set |
| Seed | `11137` | Batch ID with node digit `7` appended |
| Run ID | `seed11137_phsp21` | One node/phase-space input and output stem |
| Collection | `20261029T120000` | One processing attempt; recollection creates another |
| Scorer | `pdd`, `xprofile`, `yprofile` | One CSV suffix family, processed independently |

Do not compare collection timestamps as if they were separate simulations: recollections can process the same raw data.

## Files to keep and files to open

All paths below are relative to AppRoot. `1113` and the timestamp are examples.

| Path | Purpose |
|---|---|
| `inputs/1113/seed11137_phsp21.txt` | Generated TOPAS input; source for geometry inference |
| `runs/1113/seed11137_phsp21_pdd.csv` | Original TOPAS scorer output |
| `runs/1113/seed11137_phsp21.log` | Shared run-level TOPAS log |
| `runs/1113/run_manifest_monte-carlo-07.tsv` | Per-node Slurm task manifest |
| `runs/1113/run_batch_monte-carlo-07.slurm` | Per-node submission script |
| `outputs/1113/latest_collection.txt` | Pointer to the latest successfully processed timestamp root |
| `outputs/1113/{timestamp}/collect_manifest.tsv` | Collection-wide source manifest |
| `outputs/1113/{timestamp}/pdd/collect_manifest.tsv` | Sources contributing to this scorer |
| `outputs/1113/{timestamp}/pdd/merged-over-nodes/phsp21_merged.csv` | Node-summed result for one phase space |
| `outputs/1113/{timestamp}/pdd/merged-over-phsp/dose_merged.csv` | Final dose summed across retained nodes and phase spaces |
| `outputs/1113/{timestamp}/pdd/dose_with_uncertainty.csv` | Final summed dose plus raw-batch statistical uncertainty; Analyse reads this |
| `outputs/1113/{timestamp}/pdd/dose_with_uncertainty.csv.analysis.json` | Explicit saved analysis mapping override, if created |

The same collected layout applies independently to every scorer. Legacy suffix-less CSVs use `default/` in new collections; older collections can have final files directly at their timestamp root.

## Final CSV columns

`dose_merged.csv` contains `x,y,z,dose_to_medium_Gy`.

`dose_with_uncertainty.csv` contains:

| Column | Interpretation |
|---|---|
| `x`, `y`, `z` | Original coordinate/bin values, preserved by Collect; not implicitly millimetres |
| `dose_to_medium_Gy` | Sum of retained raw-batch doses |
| `batch_count` | Number of raw node/phase-space CSVs contributing to this scorer |
| `mean_batch_dose_Gy` | Summed dose divided by batch count |
| `batch_standard_deviation_Gy` | Sample SD of the raw-batch dose values |
| `standard_uncertainty_Gy` | Type A one-sigma uncertainty of the summed dose |
| `relative_uncertainty_percent` | `100 * standard_uncertainty_Gy / dose_to_medium_Gy`; empty for non-positive sum |

Collection computes uncertainty from original raw batches, not from node-merged files. At least two raw batches are required. The sum and its uncertainty describe the retained simulation batches; there is no automatic Gy/MU calibration or histories/weight correction between different datasets. [Equations and scope →](collect-behaviour-spec.md#raw-batch-uncertainty-behavior)

## Coordinate conversion

Analyse follows the scorer manifest to the generated inputs for the same batch. Supported geometry gives bin-centre positions in mm. For the current templates, 25 PDD bins span 50 mm with centres 1–49 mm; 300 profile bins span 600 mm with centres −299 to +299 mm.

Those values are examples derived from the current template geometry, not universal conversions. Inspect the displayed source or confirm an explicit mapping when detection cannot resolve an old/custom output. [Inference rules →](analyse-behaviour-spec.md#geometry-detection)

## Provenance and preservation

Keep SQLite metadata, generated inputs, raw CSVs/logs, collection manifests and the selected collection timestamp together. Retain relevant mapping sidecars for repeatable analysis. A scorer-processing failure may leave partial files and does not advance the previous successful latest pointers. Final marker/DB/history updates are sequential, so a later finalization failure can leave them inconsistent. Review failed attempts before treating their files as completed collections.
