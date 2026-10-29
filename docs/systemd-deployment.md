# Initial systemd installation

[README](../README.md) · [All documentation](index.md)

For ordinary updates to the existing server, use [deployment](how-to-deploy.md). This guide is for creating the service the first time.

## Check the target

The current installation uses user/group `fysiker`, application directory `/home/fysiker/WebApps/TopasRunOrchestration`, AppRoot `/srv/cluster/tsebt`, and port 5000. Change these consistently for another installation.

The host needs the .NET 8 ASP.NET Core runtime, the service account, writable AppRoot, and cluster tools for actual Run execution. Inspect installed runtimes:

```fish
dotnet --list-runtimes
```

## 1. Prepare a Linux package on Windows

From the repository root in PowerShell, after configuring the intended branch:

```powershell
dotnet run --project .\\Build.fsproj -- Bundle
dotnet publish .\\src\\Server\\Server.fsproj -c Release -r linux-x64 --self-contained false -p:UseAppHost=false -o .\\deploy-linux
Copy-Item .\\deploy\\public .\\deploy-linux\\public -Recurse -Force
tar -czf topas-initial.tar.gz -C .\\deploy-linux .
scp .\\topas-initial.tar.gz fysiker@172.31.245.206:/home/fysiker/
```

Use a fresh `deploy-linux` output folder for this initial package. These are generated artifacts, not source files to commit. Stop if a build or transfer command fails. Unlike update packaging, this first package includes a base `appsettings.json`; inspect it before startup.

## 2. Install the application files

Connect from PowerShell:

```powershell
ssh fysiker@172.31.245.206
```

On the server, in Fish or Bash:

```fish
mkdir -p /home/fysiker/WebApps/TopasRunOrchestration
tar -xzf /home/fysiker/topas-initial.tar.gz -C /home/fysiker/WebApps/TopasRunOrchestration
nano /home/fysiker/WebApps/TopasRunOrchestration/appsettings.json
```

Verify node names, phase-space values, TOPAS executable and Slurm settings against this cluster. Set Linux-compatible relative paths. The example unit below supplies the AppRoot/database overrides explicitly. [All configuration fields](configuration.md)

## 3. Create the unit

```fish
sudoedit /etc/systemd/system/topas-run-orchestration.service
```

```ini
[Unit]
Description=TopasRunOrchestration SAFE web app
After=network-online.target remote-fs.target slurmctld.service slurmd.service
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
Restart=on-failure
RestartSec=5
KillSignal=SIGINT
TimeoutStopSec=30
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
```

Check that `/usr/bin/dotnet` is the actual runtime location on your host. Ensure the service user has access to both directories and that required mounts are available. Do not run a second manual server on the same port.

## 4. Start and verify

```fish
sudo systemctl daemon-reload
sudo systemctl enable --now topas-run-orchestration.service
systemctl status topas-run-orchestration.service --no-pager
curl -I http://127.0.0.1:5000/
journalctl -u topas-run-orchestration.service -n 50 --no-pager
```

Open the server's address at port 5000 from a reachable browser. Check all four navigation tabs. Reachability also depends on local network/firewall policy; HTTP success on localhost alone does not establish remote access.

## Later operations

| Action | Command on server |
|---|---|
| Stop | `sudo systemctl stop topas-run-orchestration.service` |
| Start | `sudo systemctl start topas-run-orchestration.service` |
| Restart after a configuration edit | `sudo systemctl restart topas-run-orchestration.service` |
| Follow logs | `journalctl -u topas-run-orchestration.service -f` |
| Reload a changed unit | `sudo systemctl daemon-reload` followed by restart |

Use the [deployment script](how-to-deploy.md) for subsequent application updates. No unit reload is needed when only application files change.
