# Run locally

[README](../README.md) · [Installation](installation.md) · [All documentation](index.md)

First time here? Complete [installation and version selection](installation.md) first. The steps below work on Windows and Linux; use the shell block matching your terminal.

## 1. Choose the data directory

Use an absolute, writable folder outside the repository. Set these variables in the **same terminal** that will launch the app.

**Windows — PowerShell**

```powershell
$env:Tsebt__AppRoot = 'C:\Dev\tsebt-local-root'
$env:Tsebt__Paths__Database = 'database/app.db'
```

**Linux/macOS — Bash or Zsh**

```bash
export Tsebt__AppRoot="$HOME/tsebt-local-root"
export Tsebt__Paths__Database='database/app.db'
```

**Fish**

```fish
set -gx Tsebt__AppRoot "$HOME/tsebt-local-root"
set -gx Tsebt__Paths__Database 'database/app.db'
```

The server creates the root subfolders and SQLite database at startup. Copy your TOPAS component files into `AppRoot/templates`, retaining their subfolders. Example components are available in the separate [TSEBT templates repository](https://github.com/emintavlayan/tsebt-monte-carlo-notes-and-decisions/tree/main/05-topas-templates); adapt their referenced phase-space/CAD paths for your environment. Those external assets are not included in this app.

[Configuration](configuration.md) explains the remaining paths and cluster settings. Base `src/Server/appsettings.json` contains an example cluster, not universal machine settings. Environment variables override it; environment-specific JSON files are not loaded by the current settings loader.

## 2. Start the whole app

From the repository root:

```text
dotnet run --project Build.fsproj
```

The first start restores client dependencies, builds the solution and starts the server and Fable/Vite watchers. Open **[http://localhost:8080](http://localhost:8080)**. The development API runs on port **5000**.

Keep the terminal open. Ctrl+C stops the development processes. Running only `src/Server/Server.fsproj` does not start the Vite UI.

## 3. Check the setup

- The workflow tabs open and the configured AppRoot is shown.
- Generate lists the templates you copied.
- Analyse finds results only after collected outputs and their archived inputs are available under AppRoot.
- Run submission requires Slurm/TOPAS on the host executing the server; starting this UI on Windows does not submit through SSH to a Linux cluster.

An empty data root is expected on a first installation. No simulations are bundled with the application. [First batch walkthrough](daily-workflow.md) · [Troubleshooting](troubleshooting.md)

## 4. Build a production bundle

```text
dotnet run --project Build.fsproj -- Bundle
```

The `deploy` directory contains the published server plus `public/index.html` and browser assets. Keep both parts together. On a production host the UI is available at `/index.html` on the server's port; there is no Vite process or Node requirement.

Choose a hosting route: [Linux/systemd](systemd-deployment.md), [Windows](windows-deployment.md), or [update the existing Linux service](how-to-deploy.md). For tests and code changes, see the [developer guide](development.md).
