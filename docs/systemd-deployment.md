# Ubuntu: run the app as a service

[README](../README.md) · [All documentation](index.md)

For ordinary updates to the existing server, use [deployment](how-to-deploy.md). This guide creates the service the first time. Prepare the **build machine** using [installation](installation.md); the **Linux runtime host** only needs the runtime below, not Node or an SDK.

**Result:** systemd starts the web app at boot, keeps it running after SSH logout, and restarts it if the process exits. You do not need to leave a terminal open. This manages the web app; TOPAS jobs remain managed by Slurm.

## Check the target

Use an existing non-root SSH/service account. The examples use the current installation; substitute these values consistently for another server:

| Setting | Example |
|---|---|
| Service user and group | `fysiker` |
| Application files | `/home/fysiker/WebApps/TopasRunOrchestration` |
| Simulation data / AppRoot | `/srv/cluster/tsebt` |
| SSH destination | `fysiker@172.31.245.206` |
| HTTP port | `5000` |

The account needs read access to app files, write access to AppRoot and sudo access for service installation/updates. Real simulation execution additionally needs the configured TOPAS/Slurm environment and shared paths.

On **Ubuntu 24.04**, install the runtime if it is missing:

```bash
sudo apt-get update
sudo apt-get install -y aspnetcore-runtime-8.0
```

For other distributions use [Microsoft's Linux instructions](https://learn.microsoft.com/en-us/dotnet/core/install/linux). Verify that the output includes **Microsoft.AspNetCore.App 8.0.x** as well as Microsoft.NETCore.App:

```fish
dotnet --list-runtimes
command -v dotnet
```

Use the absolute `dotnet` path printed by the second command in `ExecStart` below.

## 1. Prepare a Linux package on Windows

From the repository root in PowerShell, after configuring the intended branch:

```powershell
dotnet run --project .\Build.fsproj -- Bundle
dotnet publish .\src\Server\Server.fsproj -c Release -r linux-x64 --self-contained false -p:UseAppHost=false -o .\deploy-linux
Copy-Item .\deploy\public .\deploy-linux\public -Recurse -Force
tar -czf topas-initial.tar.gz -C .\deploy-linux .
scp .\topas-initial.tar.gz fysiker@172.31.245.206:/home/fysiker/
```

Use a fresh `deploy-linux` output folder for this initial package. These are generated artifacts, not source files to commit. Stop if a build or transfer command fails. Unlike update packaging, this first package includes a base `appsettings.json`; inspect it before startup.

<details>
<summary>Build the first package on Linux instead</summary>

With the SDK/Node setup complete, from the repository root in Bash:

```bash
dotnet run --project Build.fsproj -- Bundle
dotnet publish src/Server/Server.fsproj -c Release -r linux-x64 --self-contained false -p:UseAppHost=false -o deploy-linux
cp -a deploy/public deploy-linux/public
tar -czf topas-initial.tar.gz -C deploy-linux .
scp topas-initial.tar.gz fysiker@172.31.245.206:/home/fysiker/
```

Use a fresh output folder and stop if a command fails. Both package routes target Linux x64; change the runtime identifier for a different server architecture.

</details>

## 2. Install the application files

Connect from PowerShell:

```powershell
ssh fysiker@172.31.245.206
```

On the server, in Fish or Bash:

```fish
mkdir -p /home/fysiker/WebApps/TopasRunOrchestration
tar -xzf /home/fysiker/topas-initial.tar.gz -C /home/fysiker/WebApps/TopasRunOrchestration
sudo install -d -o fysiker -g fysiker /srv/cluster/tsebt
nano /home/fysiker/WebApps/TopasRunOrchestration/appsettings.json
```

Verify node names, phase-space values, TOPAS executable and Slurm settings against this cluster. Set Linux-compatible relative paths. The example unit below supplies the AppRoot/database overrides explicitly. [All configuration fields](configuration.md)

## 3. Create the systemd service

On the Ubuntu server, open this file and paste the unit below, adjusting the example values:

```fish
sudoedit /etc/systemd/system/topas-run-orchestration.service
```

```ini
[Unit]
Description=TopasRunOrchestration SAFE web app
After=network-online.target remote-fs.target
Wants=network-online.target

[Service]
Type=simple
User=fysiker
Group=fysiker
WorkingDirectory=/home/fysiker/WebApps/TopasRunOrchestration
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
Environment=Tsebt__AppRoot=/srv/cluster/tsebt
Environment=Tsebt__Paths__Database=database/app.db
ExecStart=/usr/bin/dotnet /home/fysiker/WebApps/TopasRunOrchestration/Server.dll
Restart=always
RestartSec=5
KillSignal=SIGINT
TimeoutStopSec=30
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
```

Check that `/usr/bin/dotnet` is the actual runtime location on your host. Ensure the service user has access to both directories and that required mounts are available. Do not run a second manual server on the same port.

`WorkingDirectory` is the application folder containing `public`; `ExecStart` launches its server DLL. `Restart=always` retries after an exit, with a five-second delay. An explicit `systemctl stop` still stops the app normally.

## 4. Enable startup at boot and start now

```fish
sudo systemctl daemon-reload
sudo systemctl enable --now topas-run-orchestration.service
systemctl status topas-run-orchestration.service --no-pager
curl -I http://127.0.0.1:5000/
sudo journalctl -u topas-run-orchestration.service -n 50 --no-pager
```

Expected: status **active (running)** and a successful HTTP response. `enable --now` both enables future boot startup and starts the service immediately. No reboot is required. Confirm boot startup is enabled with:

```bash
systemctl is-enabled topas-run-orchestration.service
```

Open `http://server-address:5000/index.html` from a reachable browser. The root URL `/` is the health/status check; `/index.html` explicitly opens the bundled UI. Check all four navigation tabs. Reachability also depends on local network/firewall policy; HTTP success on localhost alone does not establish remote access.

## Later operations

| Action | Command on server |
|---|---|
| Stop | `sudo systemctl stop topas-run-orchestration.service` |
| Start | `sudo systemctl start topas-run-orchestration.service` |
| Restart after a configuration edit | `sudo systemctl restart topas-run-orchestration.service` |
| Follow logs | `sudo journalctl -u topas-run-orchestration.service -f` |
| Reload a changed unit | `sudo systemctl daemon-reload` followed by restart |

Use the [deployment script](how-to-deploy.md) for subsequent application updates. No unit reload is needed when only application files change.

If startup fails, read the journal first: common causes are a wrong `dotnet`/DLL path, a missing ASP.NET Core 8 runtime, an unwritable AppRoot or a port already in use. After correcting the cause, run `sudo systemctl restart topas-run-orchestration.service`; if the unit was edited, run `daemon-reload` first. [Other troubleshooting](troubleshooting.md)

Reference: [systemd service behavior](https://www.freedesktop.org/software/systemd/man/latest/systemd.service.html).
