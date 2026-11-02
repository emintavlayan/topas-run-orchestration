# Server Test Coverage Checklist

[README](../README.md) · [All documentation](index.md)

This checklist tracks practical behavioral coverage for implemented server workflows using the xUnit suite in `tests/Server.Tests`.

| Workflow area | Status | Test files |
|---|---|---|
| Config validation | Covered | `ConfigTests.fs` |
| Bootstrap folders | Covered | `ConfigTests.fs` |
| Generate planning | Covered | `GenerateTests.fs` |
| Generate collision checks | Covered | `GenerateTests.fs` |
| Generate filesystem operation | Covered | `GenerateTests.fs` |
| Run manifest and script planning | Covered | `RunTests.fs` |
| Run submission guards | Covered | `RunTests.fs` |
| Run Control state, ETA, telemetry parsing and recovery | Covered | `RunControlTests.fs` |
| Collect preflight | Covered | `CollectTests.fs` |
| Collect preflight log footer classification | Covered | `CollectTests.fs` |
| Collect preflight CSV numeric-row validation | Covered | `CollectTests.fs` |
| Collect exclusion behavior | Covered | `CollectTests.fs` |
| Collect CSV merge | Covered | `CollectTests.fs` |
| Collect statistics | Covered | `CollectTests.fs` |
| Collect operation | Covered | `CollectTests.fs` |
| Arbitrary scorer suffixes, mixed legacy CSVs, distinct scorer grids | Covered | `CollectTests.fs` |
| Per-scorer health, exclusions, independent uncertainty and shared log counts | Covered | `CollectTests.fs` |
| Longest-stem discovery, unsafe folder names and naming collisions | Covered | `CollectTests.fs` |
| Multi-scorer failure and successful latest/history preservation | Covered | `CollectTests.fs` |
| Analytic ranges, ratio/difference metrics and independent conventions | Covered | `AnalysisTests.fs` |
| Normalization and fixed-factor uncertainty scaling | Covered | `AnalysisTests.fs` |
| Current generated-input geometry, invalid grids and rotations | Covered | `AnalysisTests.fs` |
| Dataset discovery, input lookup, saved overrides and path isolation | Covered | `AnalysisTests.fs` |

Notes:

- This is workflow-level behavioral coverage, not line/branch coverage.
- Tests use temporary folders and fake CSV/process output fixtures.
- Real Slurm execution and real TOPAS execution remain manual smoke-test responsibilities.


Deployment helper checks run independently on Linux:

```bash
bash tests/Deployment/deploy-app-tests.sh
```

These use temporary application folders and mock service commands to verify success, production-config preservation, startup-failure rollback, HTTP-failure rollback, and rejection of a corrupted archive before stopping the service. They do not deploy to a server.

## Run the checks

```powershell
dotnet test .\tests\Server.Tests\Server.Tests.fsproj
dotnet run --project .\Build.fsproj -- Bundle
```

The first command runs server xUnit tests; the second checks the production server/browser build. They verify different things. See [developer validation](development.md#validate-a-change) and [current validation status](development.md#current-validation-status).

Test source: [CollectTests.fs](../tests/Server.Tests/CollectTests.fs), [AnalysisTests.fs](../tests/Server.Tests/AnalysisTests.fs), [deployment checks](../tests/Deployment/deploy-app-tests.sh).

## Run Control

`RunControlTests.fs` covers expanded Slurm identities and malformed records, live-state precedence over old logs, scheduler and TOPAS completion agreement, real-time parsing, serial/concurrent ETA and missing-evidence cases, CPU counter deltas and guest exclusion, persistent partial submissions, out-of-order scheduler rows, completed elapsed-time freezing, log fallback during scheduler outages, and bounded log access restricted to a stored batch/run pair. Scheduler responses are injected; live cluster access and SSH credentials require verification on the deployed server.

## Build environment

`BuildNodeEnvironmentTests.fs` covers matching installations without fnm, inherited Node 24 replacement, direct use of the selected npm, missing-version installation, WinGet discovery without PATH entries, installation/npm-version failures, and exact repository pins. The preparation target is a dependency of client build/development paths, and deployment invokes it before server tests.

Live overview regression coverage checks current/upcoming runs on configured nodes and verifies that Slurm responses are shared across runs. Browser smoke checks cover progressive task disclosure, single-PHSP history, modal size and keyboard dismissal, polling and switching between live/history views.

## Saved datasets and import


`DatasetTests.fs` covers exact original bytes, server-side reparsing rather than trusting preview samples, required metadata and confirmation, invalid scans, independent imports, metadata revision conflicts, cascading deletion, bounded MCC scan identity and historical collection snapshots. Server and client compilation pass under .NET SDK 8.0.420. All 74 server Fact methods, including the dataset tests, pass through direct invocation with xUnit assertions. VSTest crashes in this environment; Fable compilation is blocked by subprocess process inspection. Run `dotnet test tests/Server.Tests/Server.Tests.fsproj` locally for standard-runner verification. Native MCC/SNC compatibility still requires actual measurement exports.
