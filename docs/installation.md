# Install and run

[README](../README.md) · [All documentation](index.md)

Keep newer .NET and Node installations. Install the required versions alongside them; this repository selects its own build tools.

## Choose your route

| Goal | What you need | Next step |
|---|---|---|
| Use an already installed app | A browser and its URL | [Daily workflow](daily-workflow.md) |
| Run or build the source on Windows/Linux | Git, .NET 8 SDK, Node 20 and npm 10 | Follow this page, then [local setup](onboarding.md) |
| Keep the app running on Ubuntu after logout/reboot | A build machine; ASP.NET Core 8 runtime on the server | [Linux installation and systemd service](systemd-deployment.md) |
| Update the existing Linux service from Windows | Build tools, SSH/SCP and tar | [Deployment script](how-to-deploy.md) |
| Run a published app on Windows | ASP.NET Core 8 runtime; a built Windows package | [Windows deployment](windows-deployment.md) |

Node is a **build-time dependency**. A server receiving a published package needs neither Node nor the .NET SDK. TOPAS, Slurm, phase-space files and CAD assets are separate prerequisites for real simulation execution; they are not installed by this app. Local UI development and analysis of existing files do not require Slurm.

## 1. Install the build tools once

### Windows — PowerShell

```powershell
winget install --id Git.Git -e
winget install --id Microsoft.DotNet.SDK.8 -e
winget install --id Schniz.fnm -e
```

Close and reopen the terminal, including VS Code terminals. If WinGet is unavailable, use the [Git installer](https://git-scm.com/downloads), [.NET 8 SDK installer](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) and [fnm installation instructions](https://github.com/Schniz/fnm#installation).

### Ubuntu 24.04 — Bash

```bash
sudo apt-get update
sudo apt-get install -y git curl unzip dotnet-sdk-8.0
curl -fsSL https://fnm.vercel.app/install | bash
```

Open a new terminal after installing fnm. Other Linux distributions should use Microsoft's [distribution-specific .NET instructions](https://learn.microsoft.com/en-us/dotnet/core/install/linux). On macOS, install the .NET 8 SDK and fnm using their linked installers/Homebrew; the remaining repository commands are the same.

## 2. Get the source

```text
git clone https://github.com/emintavlayan/topas-run-orchestration.git
cd topas-run-orchestration
```

Use the branch containing the application version you want. The Analyse changes described here currently live on:

```text
git switch feat/collect-multiple-scorers
```

A pull updates the current branch; it does not copy unmerged features into `main`.

## 3. Enable project-specific Node selection

Use **fnm** if you do not already have a Node version manager. If you already use another manager, select Node 20 there instead of installing a second manager.

For automatic switching, add the appropriate initialization line to your shell configuration:

| Shell | Configuration file | Line to add once |
|---|---|---|
| PowerShell | `$PROFILE` | `fnm env --use-on-cd --shell powershell \| Out-String \| Invoke-Expression` |
| Bash | `~/.bashrc` | `eval "$(fnm env --use-on-cd --shell bash)"` |
| Zsh | `~/.zshrc` | `eval "$(fnm env --use-on-cd --shell zsh)"` |
| Fish | `~/.config/fish/conf.d/fnm.fish` | `fnm env --use-on-cd --shell fish \| source` |

To create/open the PowerShell profile safely:

```powershell
if (-not (Test-Path $PROFILE)) { New-Item $PROFILE -Force | Out-Null }
notepad $PROFILE
```

Paste the PowerShell initialization line from the table, save, then open a new terminal and return to the repository. The Linux installer may already have added the initialization line; check before adding another copy. Create the Fish configuration directory if it does not exist.

From the repository root, on any supported shell:

```text
fnm install 20
fnm use 20
node --version
npm --version
```

Expected: `v20.x.x` and `10.x.x`. npm is supplied with Node. After this setup, fnm reads this repository's `.node-version` when you enter it. You can also run `fnm use` from the repository root to select its requested version explicitly.

## 4. Verify .NET selection and restore

From the repository root:

```text
dotnet --list-sdks
dotnet --version
dotnet tool restore
dotnet paket restore
```

`dotnet --version` must report **8.0.420 or a later stable 8.x SDK**. Installing the latest .NET 8 SDK satisfies this; having only .NET 9/10 does not. No global SDK switch is needed.

<details>
<summary>Ubuntu installed an older 8.0.1xx/3xx SDK?</summary>

Some distribution packages use an older SDK feature band. In Bash, install Microsoft's current 8.0 SDK alongside it:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/topas-dotnet-install.sh
bash /tmp/topas-dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet --version
```

Keep those exports in the terminal used for this checkout; another terminal can continue using system-wide .NET. Repeat the exports when using this SDK in a new build terminal. For another shell, follow Microsoft's [install-script environment setup](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script). The script installs the SDK; the Ubuntu package installation above supplies its OS dependencies.

</details>

| Repository file | What it controls |
|---|---|
| [`global.json`](../global.json) | Selects an installed .NET 8 SDK: minimum `8.0.420`, `rollForward: latestMinor`; does not roll into .NET 9 |
| [`.node-version`](../.node-version) | Requests Node `20` from a configured version manager; does not install or switch Node by itself |
| Client `package.json` files | Require Node `20.x` and npm `10.x` |
| Client `.npmrc` files | Reject incompatible Node/npm versions when dependencies are installed |
| `.config/dotnet-tools.json`, `paket.lock`, npm lockfiles | Pin the project tools and dependency versions |

Run build commands **inside the repository**, so `global.json` is found. The published application separately targets `net8.0` and requires the ASP.NET Core 8 runtime on its host.

## 5. Start locally or install on a server

- **Local development:** [set the data root and start the app](onboarding.md). UI: `http://localhost:8080`; API: port `5000`.
- **First Linux deployment:** [prepare the runtime, package and systemd service](systemd-deployment.md).
- **Later Linux updates from Windows:** run `git pull --ff-only`, then `./scripts/deploy-app.ps1` in PowerShell. The script builds, publishes for `linux-x64`, transfers, preserves server configuration, backs up, restarts and checks health. [Parameters and recovery](how-to-deploy.md).
- **Windows hosting:** [build a Windows package and run it directly or host it with IIS](windows-deployment.md).

## If setup stops

| Symptom | Action |
|---|---|
| `node --version` still reports 22/24 or `EBADENGINE` | Return to the repository, run `fnm use 20`, check `node --version` and `npm --version` in that same terminal, then retry |
| `fnm use` reports that the shell environment is missing | Add/evaluate the initialization line for your actual shell, then retry |
| `fnm`/`dotnet` is not found after installation | Reopen the terminal/VS Code; verify the installer added the executable to PATH |
| Requested .NET SDK is missing | Install the latest **8.0 SDK**, not only the runtime; check `dotnet --list-sdks` |
| Published app says a framework is missing | Install **ASP.NET Core Runtime 8**; check that `dotnet --list-runtimes` includes `Microsoft.AspNetCore.App 8.0.x` |
| PowerShell blocks profile loading | Run `fnm env --shell powershell \| Out-String \| Invoke-Expression` in the current terminal, then `fnm use 20`; this does not require changing the profile execution policy |
| PowerShell blocks the deployment script | [Deployment](how-to-deploy.md#windows-powershell) includes a per-invocation option |
| Empty template list or no analysis curves | The app starts with an empty data root. Supply templates/results in that configured root; [configuration](configuration.md) explains the paths |

This documents the current compatibility target. Node 20 is now [upstream end-of-life](https://nodejs.org/en/about/previous-releases); it is retained here for the established build toolchain, not as a claim of current LTS support.

Sources: [Microsoft SDK selection](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json), [fnm shell setup](https://github.com/Schniz/fnm#shell-setup), [Windows .NET installation](https://learn.microsoft.com/en-us/dotnet/core/install/windows), [Ubuntu .NET installation](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install).
