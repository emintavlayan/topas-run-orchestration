# Local setup

[README](../README.md) · [All documentation](index.md)

This guide starts a development copy on Windows. To update an installed Linux service, use [deployment](how-to-deploy.md).

## Prerequisites

- Git, .NET 8 SDK compatible with [global.json](../global.json), Node.js and npm.
- Node 20 is the established development environment; package versions are locked in the client lockfile and Paket lockfile.
- A writable local folder for AppRoot.
- TOPAS and Slurm are needed for real simulation execution, not for automated server tests or UI development.

## 1. Get the source

```powershell
git clone https://github.com/emintavlayan/topas-run-orchestration.git
cd topas-run-orchestration
dotnet tool restore
dotnet paket restore
```

Check out the branch containing the features you intend to use. `git pull --ff-only` updates the current tracking branch; it does not bring an unmerged feature branch into `main`.

## 2. Configure a local data root

In the PowerShell session that will launch the app:

```powershell
$env:Tsebt__AppRoot = 'C:\Dev\tsebt-local-root'
```

The server creates its required root folders at startup. Copy your TOPAS template components into that root's `templates` directory. Configure the remaining settings in [src/Server/appsettings.json](../src/Server/appsettings.json), or override them with environment variables. Use platform-native relative paths, including `database/app.db` on Linux. [Configuration reference →](configuration.md)

## 3. Start the complete development app

```powershell
dotnet run --project .\Build.fsproj
```

The default build target restores client npm dependencies, builds the solution, and starts the server and Fable/Vite watchers. Open [http://localhost:8080](http://localhost:8080) for the browser UI. The development API listens on port **5000**.

Keep this terminal open. Stop the development processes with Ctrl+C. A server-only `dotnet run --project .\src\Server` does not start the Vite browser UI.

## 4. Verify without submitting simulations

```powershell
dotnet test .\tests\Server.Tests\Server.Tests.fsproj
```

Open all four tabs. Generate preview requires configured templates; Analyse needs existing collected outputs. Real Run submission requires the app to execute on a host with the configured Slurm/TOPAS tools and shared paths.

## 5. Build a production bundle

```powershell
dotnet run --project .\Build.fsproj -- Bundle
```

This produces published server files and the browser bundle under `deploy`. For Windows-to-Linux deployment, use the deployment helper rather than copying Windows publish output directly. [Next: deploy the application →](how-to-deploy.md)
