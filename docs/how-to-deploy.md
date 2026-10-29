# Deploying TopasRunOrchestration

## Existing production service

- SSH target: `fysiker@172.31.245.206` (`monte-carlo-01`)
- Application directory: `/home/fysiker/WebApps/TopasRunOrchestration`
- Service: `topas-run-orchestration.service`
- Entrypoint: `/usr/bin/dotnet /home/fysiker/WebApps/TopasRunOrchestration/Server.dll`
- HTTP port: `5000`

The deployment script requires this service and production `appsettings.json` to exist already.
The production service user must be able to read the deployment directory.
Run deployment when no collection operation is in progress; the web service is stopped briefly.

## Windows PowerShell

From the repository root, after pulling the desired branch:

```powershell
.\scripts\deploy-app.ps1
```

The script runs server tests and `Bundle`, then publishes for Linux and deploys the application.
It stops immediately if a native command fails.

If tests and `dotnet run --project .\Build.fsproj -- Bundle` already passed for the current source revision:

```powershell
.\scripts\deploy-app.ps1 -SkipBuild
```

`-SkipBuild` reuses the existing browser bundle in `deploy/public`.
It always republishes the server for `linux-x64`, framework-dependent, using the server's installed .NET runtime.
Do not use `-SkipBuild` after changing client or shared source without rebuilding the bundle.

If Windows blocks local PowerShell script execution, run this invocation:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy-app.ps1 -SkipBuild
```

Prerequisites on Windows: .NET SDK, Node/npm for Bundle, OpenSSH (`ssh`, `scp`), and `tar`.
SSH or sudo may request your existing passwords interactively; the script stores no credentials.

## Deployment behavior

1. Publish into a fresh local temporary folder and include the browser bundle.
2. Remove development `appsettings*.json` from the package.
3. Create a tar archive and compute its SHA-256 hash.
4. Upload the archive and the deployment helper into a unique temporary server directory.
5. Verify the archive hash and required application files before stopping the service.
6. Confirm the service's `WorkingDirectory` matches the deployment target.
7. Copy the existing server `appsettings*.json` into the staged application.
8. Stop the service and rename the existing app directory to a timestamped backup.
9. Move the staged application into place and start the service.
10. Check service activity and HTTP responsiveness for up to 30 attempts.
11. On failure after stopping, restore the previous directory and attempt to restart it. Keep a failed replacement for inspection when one was installed.

Successful deployments retain the backup next to the application directory:

```text
/home/fysiker/WebApps/TopasRunOrchestration.backup-{timestamp}-{processId}
```

Simulation inputs, raw outputs, collected results and SQLite data remain under the configured AppRoot.
The script replaces only the application directory. It does not edit or reload the systemd unit.
A service restart interrupts any in-progress web operation, including Collect.

## Verification

After the script reports `Deployment verified`, refresh the browser with Ctrl+F5.
For multi-scorer collection, inspect a batch preview and verify the discovered scorer names.

Additional checks from PowerShell:

```powershell
ssh fysiker@172.31.245.206 "systemctl status topas-run-orchestration.service --no-pager"
ssh fysiker@172.31.245.206 "journalctl -u topas-run-orchestration.service -n 50 --no-pager"
```

## Other installations

Override the target explicitly:

```powershell
.\scripts\deploy-app.ps1 -SshTarget 'user@server' -DeployPath '/home/user/WebApps/TopasRunOrchestration' -ServiceName 'topas-run-orchestration.service' -HealthUrl 'http://127.0.0.1:5000/'
```

For initial service creation, see `systemd-deployment.md`. Use the actual installation path consistently in `WorkingDirectory` and `ExecStart`.
