# Wizard UX Flow Specification

## Purpose

This document defines the current client wizard UX and behavior for:

- Generate
- Run
- Collect

It reflects the shared app shell, shared wizard shell, and current step logic implemented in client code.

## Shared app shell

- Single-row top navbar:
  - title: `Topas Run Orchestration`
  - workflow buttons: `Generate`, `Run`, `Collect`
  - theme selector
- Active page button is primary; inactive buttons are outlined.
- Theme selector options:
  - `Light`
  - `Dark`
  - `Corporate`
  - `Night`
  - `Cyberpunk`

## Shared wizard shell

All workflows use one shared shell with:

- left vertical stepper (all step titles visible)
- right content area (scrollable)
- fixed footer action bar (always visible)
- footer actions:
  - left: `Cancel`
  - right: `Previous` (when applicable), then primary action

Layout safeguards:

- wizard card is viewport-bounded
- content body scrolls
- long tables/lists/scripts use bounded internal scroll containers
- footer is `shrink-0` and does not move offscreen on long content

## Generate flow

Steps:

1. Welcome
2. Components
3. Nodes
4. Phase-space
5. Review
6. Result

Primary actions:

- Welcome: `Start`
- Components/Nodes/Phase-space: `Next`
- Review: `Generate`
- Result: `Back to Generate`

Behavior:

- Component step requires at least one selected component.
- Node and phase-space steps support `Select all`/`Select none`.
- Review requests preview before final execution.
- The stitched TOPAS preview uses theme-aware syntax highlighting and visually flags unresolved placeholders. This is a review aid, not TOPAS semantic or physics validation.
- Result shows generated run summary.
- Cancel resets Generate wizard state.

## Run flow

Steps:

1. Welcome
2. Batch
3. Preflight
4. Script
5. Result

Primary actions:

- Welcome: `Start`
- Batch/Preflight: `Next`
- Script: `Submit`
- Result: `Back to Run`

Behavior:

- Batch step allows selecting only non-submitted generated batches.
- Script preview displays only the first node preview from `NodeScriptPreviews`.
- Script content is internally scrollable.
- Submit is blocked when preflight is not `CanSubmit`.
- Cancel resets Run wizard state.

## Collect flow

Steps:

1. Welcome
2. Batch
3. Preflight
4. Review
5. Result

Primary actions:

- Welcome: `Start`
- Batch/Preflight: `Next`
- Review: `Collect` or `Collect again` (or `Collecting...` while loading)
- Result: `Back to Collect`

Behavior:

- Batch step allows selecting any batch with generated runs, including batches collected before.
- Preflight shows:
  - checks table
  - file issue table (run, phase-space, node, kind, problem, message)
  - active exclusions
- Exclusion actions:
  - `Skip failed phase-space files`
  - `Skip failed nodes`
- Selecting one exclusion mode clears the other mode.
- Review and collect run against effective rows after exclusions.
- Review shows a neutral info block when the batch has already been collected, including the latest collection folder and a note that recollection creates a new timestamped folder under `outputs/{seedBase}/`.
- Collect action is disabled when `CanCollect = false`.
- Duplicate collect submissions are blocked while loading.
- Cancel resets Collect wizard state.

## Server API usage by workflow

Generate:

- `getAppConfig`
- `getTemplateFiles`
- `previewGenerate`
- `generate`

Run:

- `getRunBatches`
- `previewRun`
- `submitRun`

Collect:

- `getCollectBatches`
- `previewCollect`
- `collectBatch`

## Error display behavior

- All workflows show server/client errors in wizard footer error alert area.
- Long error text is scroll-constrained.
