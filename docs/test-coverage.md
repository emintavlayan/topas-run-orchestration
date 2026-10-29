# Server Test Coverage Checklist

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

Notes:

- This is workflow-level behavioral coverage, not line/branch coverage.
- Tests use temporary folders and fake CSV/process output fixtures.
- Real Slurm execution and real TOPAS execution remain manual smoke-test responsibilities.


Deployment helper checks run independently on Linux:

```bash
bash tests/Deployment/deploy-app-tests.sh
```

These use temporary application folders and mock service commands to verify success, production-config preservation, startup-failure rollback, HTTP-failure rollback, and rejection of a corrupted archive before stopping the service. They do not deploy to a server.
