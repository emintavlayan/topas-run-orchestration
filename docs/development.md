# Developer guide

[README](../README.md) · [All documentation](index.md)

## Responsibility map

| Area | Responsibility | Entry points |
|---|---|---|
| Shared | Serializable contracts and pure dose-curve calculations | [Shared.fs](../src/Shared/Shared.fs), [Analysis.fs](../src/Shared/Analysis.fs) |
| Server | Configuration, files, SQLite, TOPAS/Slurm orchestration | [Server.fs](../src/Server/Server.fs) |
| Client | Elmish wizard state, React analysis workspace, plotting | [Index.fs](../src/Client/Index.fs), [AnalysisViews.fs](../src/Client/AnalysisViews.fs) |
| Collection | Discovery, health, merging and statistics | [CollectPlanning.fs](../src/Server/CollectPlanning.fs), [CollectPreflight.fs](../src/Server/CollectPreflight.fs), [CollectOperation.fs](../src/Server/CollectOperation.fs) |
| Geometry inference | Generated-input parameter parsing and agreement checks | [AnalysisInput.fs](../src/Server/AnalysisInput.fs) |
| Analysis storage | Dataset discovery, CSV reading and sidecar overrides | [AnalysisOperation.fs](../src/Server/AnalysisOperation.fs) |
| Deployment | Windows packaging and Linux service replacement | [deploy-app.ps1](../scripts/deploy-app.ps1), [deploy-app.sh](../scripts/deploy-app.sh) |

Follow the [F# doctrine](safe-light-fsharp-doctrine.md): small named functions, pure calculations, `Result<_, string>`, and FsToolkit.ErrorHandling for sequential fallible work. Every function gets a one-line documentation comment. Keep side effects at the server boundary.

## Local development

Follow [local setup](onboarding.md). The regular watcher command is:

```powershell
dotnet run --project .\Build.fsproj
```

## Validate a change

From the repository root:

```powershell
dotnet test .\tests\Server.Tests\Server.Tests.fsproj
dotnet run --project .\Build.fsproj -- Bundle
git diff --check
```

The server test project contains xUnit tests. Some other test projects use different tooling; the dedicated server command above is the supported route for these workflow checks. A successful .NET client build alone does not verify Fable translation or the browser bundle.

For deployment-helper changes, on Linux:

```bash
bash -n scripts/deploy-app.sh tests/Deployment/deploy-app-tests.sh
bash tests/Deployment/deploy-app-tests.sh
```

Those tests mock service commands and use temporary folders. They do not contact production. [Coverage map →](test-coverage.md)

## Browser checks

After tests and Bundle:

1. Verify Generate, Run and Collect navigation/previews with representative data.
2. Verify Analyse selection, automatic and explicit mappings, each normalization, both metric definitions, reference differences and exports.
3. Check mobile/narrow layout, themes, long paths and visible error states.
4. For cluster changes, validate real Slurm/TOPAS behavior separately; unit tests do not execute the simulation engine.

## Current validation status

The analysis implementation passed client/server .NET builds and 64 compiled server test cases by direct invocation in the implementation environment. Standard VSTest launching and Fable project cracking were blocked there by process-inspection failures, so that result is not a claim of browser validation. Run the standard commands above in the normal development environment.

## Documentation maintenance

Keep the README as the workflow map. Put operator steps in task guides and exact algorithms/edge cases in specifications. Link source entry points instead of duplicating code. When changing paths, formulas or API behavior, update the applicable guide, reference and tests together. Do not describe planned features as implemented.
