# Import and saved datasets

[README](../README.md) · [Analysis](analysis-guide.md) · [All documentation](index.md)

Collect, Import and Analyse use a common saved dataset containing named 1D curves. A dataset records Measurement/Simulation source, acquisition date when known, saved timestamp and original files. Each curve retains original samples, declared units, geometry, scan identity, parser version and manual corrections.

## Import files

1. Open **Import** and choose one or several files from your computer.
2. Review detected curves. Uncheck scans you do not want to import. Expand each scan to edit its name, X/Y/PDD role, units, depth, SSD, field size and scan date. Each expanded curve shows compact metadata inputs beside its own raw-sample plot. The layout stacks on narrow screens. Choose **Accept curve** after reviewing it; changing its metadata clears only that curve’s acceptance.
3. Enter a dataset name and source. Missing dates and optional geometry stay explicitly unknown. Units and role are required. Accept each included curve to confirm its dose quantity and reviewed metadata. The accepted count shows review progress.
4. Once all included curves are accepted, choose **Import selected scans**. Open **Analyse** and select the complete run or measurement dataset.

Normalization belongs to Analyse. Import does not smooth curves, average scans, normalize dose, convert ionization to dose or guess missing units. Coordinate transforms use mm; original values remain unchanged. Raw cGy is converted to Gy during analysis. Relative and absolute raw units cannot share a plot without normalization.

## Supported files in this implementation

- UTF-8 text/CSV with two columns: position and dose. An optional third column represents **absolute uncertainty in the same units as dose**. All rows must have the same column count.
- Delimiters: commas, semicolons, or whitespace. Numeric values use a decimal point. Headerless files require explicit role and unit selection.
- Optional position headers: `position`, `position_mm`, `position_cm`, `depth_mm`, `x_mm`, `y_mm`.
- Optional value headers: `dose`, `value`, `dose_Gy`, `dose_cGy`, `dose_percent`, `relative_dose`.
- An optional uncertainty header is `uncertainty`, or `standard_uncertainty_Gy` / `standard_uncertainty_cGy` matching the dose unit.
- MCC-style `BEGIN_SCAN n` / `END_SCAN n` blocks, each containing exactly one `BEGIN_DATA` / `END_DATA` block with **two numeric columns**. CC-Export V1.9 geometry is read in mm; dose quantity/units still require confirmation. This conservative structural reader has tests against the supplied scan structure. Additional columns are rejected because their meaning has not been established.
- SNC tab-delimited exports: each complete scan must have one `BEGIN DOSE TABLE` with X/Y/Z in cm and Relative Dose (%). Geometry is converted to mm for analysis; original coordinates and dose values remain untouched. Depth scans require explicit `Is PDI to PDD=True`.

For example, this CSV has known coordinates and dose units; review still requires a role:

```csv
position_mm,dose_Gy,standard_uncertainty_Gy
-10,0.5,0.01
0,1.0,0.02
10,0.5,0.01
```

Duplicate coordinates, non-finite numbers, negative dose/uncertainty, incomplete scan blocks and unsupported columns cause an explicit error. Limits: 20 files, 10 MB total original bytes, 200 scans, 100,000 samples per curve. These bounds apply to uploaded files; historical collection registration uses the existing 1D scorer reader.

## Collect and historical outputs

New **Collect** operations attempt automatic dataset registration after merging completes. Each timestamp folder is a separate version. Registration is idempotent for the exact collection key; recollecting into a new folder creates another saved version. A registration failure is shown separately from successful merging. Retry from **Import → Existing simulation collections** without rerunning Collect.

For existing results, expand **Existing simulation collections** and choose **Register collection**. The server reads only discovered output identities, snapshots original CSVs and available manifests/analysis sidecars, and resolves mapping from the archived generated inputs or existing overrides. Missing geometry stays unconfirmed until manually saved under Analyse Settings. Multidimensional scorers cannot be registered as 1D curves; their collection remains available on disk and the registration error is reported.

A saved dataset remains usable if original output files are moved or removed. Existing collected files and legacy `.analysis.json` sidecars are not modified by Analyse. **Restore imported geometry** restores the mapping snapshot recorded at registration; it does not reread current templates or generated inputs. An initially unresolved snapshot cannot be activated by restoring it.

## Manage saved versions

- **Edit / originals** loads curve metadata for editing. Samples and parser-owned provenance remain unchanged. Every changed curve metadata field is appended to its corrections.
- **Retrieve original**, then **Download**, returns the exact original bytes. Originals are stored in SQLite alongside curves. Collection originals include CSVs and available manifests/sidecars.
- **Delete**, then **Confirm delete**, removes only that saved dataset, its samples and archived originals. Collected outputs on disk remain available for reimport.
- Metadata edits and deletion require the revision loaded by the editor. A concurrent edit produces an error and requires a reload. Geometry overrides also advance the revision and append a correction.

The configured SQLite database now contains original files and samples as well as orchestration metadata. Include it in backups. New `saved_*` tables are additive; existing generated batches, run history and collection history remain usable.

## Local validation

Server and client compile under .NET SDK 8.0.420. All 74 server xUnit Fact methods pass when invoked directly with their assertions; the standard VSTest runner crashes in the execution environment. Fable JavaScript compilation is also blocked by environment process inspection, so browser validation is pending. On your development computer:

```powershell
git pull --ff-only
dotnet test tests/Server.Tests/Server.Tests.fsproj
```

Start the application with your normal development command. Register an existing collection, import a measurement CSV, compare X/Y/PDD, change normalization, export curves/metrics, edit metadata, download an original and delete a test version. The dataset tests cover byte preservation, server reparsing, failed review, scan boundaries, revision conflicts, deletion and historical snapshots. Vendor metadata remains reviewable; filenames never override geometry.

SNC scan dates use the export's month/day/year format; MCC measurement dates use day-month-name-year. File creation/export dates are not substituted for acquisition dates. SNC centering, symmetrizing, normalization and detector offsets are not reapplied. MCC files have no declared dose unit; the reviewer must identify the quantity. In the supplied examples both MCC filenames report 200 × 200 mm fields and zero scan depth in their headers; these values are retained for review. Statistics reports are not treated as sampled curves.
