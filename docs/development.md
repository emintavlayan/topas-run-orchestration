# Developer guide

[README](../README.md) · [All documentation](index.md)

## Responsibility map

| Area | Responsibility | Entry points |
|---|---|---|
| Shared | Serializable contracts and pure dose-curve calculations | [Shared.fs](../src/Shared/Shared.fs), [Analysis.fs](../src/Shared/Analysis.fs) |
| Server | Configuration, files, SQLite, TOPAS/Slurm orchestration | [Server.fs](../src/Server/Server.fs) |
| Client | Elmish wizard state, React analysis workspace, plotting | [Index.fs](../src/Client/Index.fs), [AnalysisViews.fs](../src/Client/AnalysisViews.fs) |
| Collection | Discovery, health, merging and statistics | [CollectPlanning.fs](../src/Server/CollectPlanning.fs), [CollectPreflight.fs](../src/Server/CollectPreflight.fs), [CollectOperation.fs](../src/Server/CollectOperation.fs) |
| Run monitoring | Pure state/ETA parsing, SQLite task identity, bounded Slurm/procfs probes and shared snapshot cache | [RunControlDomain.fs](../src/Server/RunControlDomain.fs), [RunControlOperation.fs](../src/Server/RunControlOperation.fs), [RunControlViews.fs](../src/Client/RunControlViews.fs) |
| Geometry inference | Generated-input parameter parsing and agreement checks | [AnalysisInput.fs](../src/Server/AnalysisInput.fs) |
| Analysis storage | Dataset discovery, CSV reading and sidecar overrides | [AnalysisOperation.fs](../src/Server/AnalysisOperation.fs) |
| Deployment | Windows packaging and Linux service replacement | [deploy-app.ps1](../scripts/deploy-app.ps1), [deploy-app.sh](../scripts/deploy-app.sh) |

Follow the [F# doctrine](safe-light-fsharp-doctrine.md): small named functions, pure calculations, `Result<_, string>`, and FsToolkit.ErrorHandling for sequential fallible work. Every function gets a one-line documentation comment. Keep side effects at the server boundary.

## Local development

Complete [installation and version selection](installation.md), then follow [local setup](onboarding.md). Run build commands from the repository root: `global.json` selects a stable .NET 8 SDK, `.node-version` pins Node 20.20.2. The `PrepareNode` target selects a matching installation or uses fnm to install/select it without shell initialization; npm enforces Node 20/npm 10 in each client project. The regular watcher command is:

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
2. Verify Analyse selection, direct profile overlays, automatic and explicit mappings, outlined Settings actions, each normalization (including 5/10-point central means), all metric equations and exports.
3. Check mobile/narrow layout, themes, long paths and visible error states.
4. For cluster changes, validate real Slurm/TOPAS behavior separately; unit tests do not execute the simulation engine.

## Current validation status

The Import/Run Control integration compiles for both server and client under .NET SDK 8.0.420. All 82 server xUnit Fact methods pass by direct invocation with assertions, including Import, Run Control and Node-selection checks. The combined server starts with an isolated test AppRoot and reports successful startup/listening. Standard VSTest launching and Fable project cracking remain blocked by process-inspection failures in this environment; browser bundle/UI validation remains pending. Run the standard commands above on the normal development computer.

## Documentation maintenance

Keep the README as the workflow map. Put operator steps in task guides and exact algorithms/edge cases in specifications. Link source entry points instead of duplicating code. When changing paths, formulas or API behavior, update the applicable guide, reference and tests together. Do not describe planned features as implemented.

## Reusing Node selection in other SAFE apps

Use `BuildNodeEnvironment.fs` as the build-only environment boundary: pin the full Node version in `.node-version`, read npm's major from the client package engines, and run `PrepareNode` before cleanup/build targets. Run npm/npx through the verified Node installation and let Fable/Vite inherit its PATH. Keep a version manager as a one-time developer prerequisite rather than requiring per-terminal setup. Install Node only on machines that build the browser bundle; published SAFE apps serve their bundled static files without Node. A prebuilt application release is the route for users who should not need build tools at all.

This repository implements the pattern here; other application repositories must adopt it explicitly. A standalone toolchain check is `dotnet run --project Build.fsproj -- PrepareNode`.
