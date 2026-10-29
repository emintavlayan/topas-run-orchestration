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
