# Generate Behaviour Specification

[README](../README.md) · [All documentation](index.md)

## Purpose

`Generate` creates TOPAS input files for one simulation batch.

Deterministic workflow:

```text
selected TOPAS component files
+ selected nodes
+ selected phase-space files
+ runtime seed base
+ configured placeholder tokens
= generated TOPAS input files + SQLite metadata
```

## Runtime root and folders

Generate uses configured `AppRoot` and relative paths:

```text
templates/   source TOPAS component files
inputs/      generated TOPAS input files
runs/        run folders referenced by generated TOPAS input files
database/    SQLite app memory
logs/        app/server logs
```

## Runtime seed-base rule

Seed base shown/used is resolved from SQLite:

```text
if generated_batches is empty:
    use configured seed base
else:
    use max(cast(seed_base as integer)) + 1
```

`appsettings.json` is never edited at runtime.

## Expansion rule

Generate expands selected values over:

```text
selected nodes x selected phase-space files
```

Example:

```text
7 selected nodes x 22 selected phase-space files = 154 generated input files
```

## Seed rule

Seed is built by suffixing node digit to seed base:

```text
seed = seedBase + nodeDigit
```

Examples:

```text
seedBase=1001, node 1 -> 10011
seedBase=1001, node 7 -> 10017
```

The same node seed pattern repeats across phase-space files.

## RunId rule

```text
runId = seed{seed}_phsp{phaseSpaceIndex}
```

Example:

```text
seed10011_phsp01
```

## Generated input file naming

```text
seed{seed}_phsp{phaseSpaceIndex}.txt
```

Example:

```text
seed10011_phsp01.txt
```

Written under:

```text
{AppRoot}/inputs/{seedBase}/
```

## Output placeholder rule

`OutputFile` placeholder is replaced with:

```text
{AppRoot}/runs/{seedBase}/seed{seed}_phsp{phaseSpaceIndex}
```

## Placeholder replacement

Configured placeholders are replaced for each generated file:

- phase-space placeholder -> configured phase-space value
- output placeholder -> `{AppRoot}/runs/{seedBase}/seed{seed}_phsp{phaseSpaceIndex}`
- seed placeholder -> constructed seed

## Collision safety

Generate performs preflight collision checks before any file writes or SQLite inserts.

Checks:

1. Input batch folder collision:
   - target: `{AppRoot}/{Paths.Inputs}/{seedBase}`
   - if folder exists and contains files -> error

2. Planned generated input file collisions:
   - if any planned generated input path already exists -> error

3. Run batch folder collision:
   - target: `{AppRoot}/{Paths.Runs}/{seedBase}`
   - if folder exists and contains files -> error

4. Planned output base path collisions:
   - target: `{AppRoot}/{Paths.Runs}/{seedBase}/seed{seed}_phsp{phaseSpaceIndex}`
   - if `path`, `path.csv`, or `path.log` already exists -> error

5. Planned SQLite duplicate collisions:
   - if any planned `run_id` already exists in `generated_runs` -> error

All-or-nothing guarantee:

- if any collision is found, no files are written and no SQLite rows are inserted.

## SQLite persistence

On success:

- one `generated_batches` row is inserted with used `seed_base`
- one `generated_runs` row per generated file is inserted with run metadata

Initial run status:

```text
Generated
```

## Server result

Generate returns structured result including:

- `SeedBase`
- `GeneratedInputCount`
- `NodeCount`
- `PhaseSpaceCount`
- `InputFolder`
- `GeneratedRuns`

## Current test coverage

Automated tests currently cover:

- seed construction
- runId construction
- generated filename construction
- run/output path planning
- placeholder replacement
- stitch ordering
- planning expansion semantics
- collision preflight rejection for existing input folder content



## Implementation boundaries

Source: [GeneratePreview.fs](../src/Server/GeneratePreview.fs), [GeneratePlanning.fs](../src/Server/GeneratePlanning.fs), [GenerateOperation.fs](../src/Server/GenerateOperation.fs), [SeedBaseStore.fs](../src/Server/SeedBaseStore.fs).

- Template text is stitched in request-list order with blank lines between components. No mandatory world/physics/run-control file is automatically inserted.
- Preview expands only the first selected node/phase-space pair and reports the full expected count; it is not a validation of every resulting input.
- Placeholder replacement is literal text replacement, not TOPAS parsing, unit validation or geometry validation.
- Generation plans phase spaces in the outer loop and nodes in the inner loop.
- Output-base collision checks recognize the base, `.csv` and `.log`; the non-empty run-folder check is broader and also blocks other files at generation time.
- The collision-failure guarantee is not a general IO/DB rollback guarantee. After preflight, files are written, a batch row is inserted, then run rows are inserted without a transaction spanning all effects. Later failure can leave artifacts.
- Non-empty selections are enforced by the ordinary wizard. The generate API itself does not independently reject all empty selection combinations; do not treat the UI constraints as a server API invariant.
