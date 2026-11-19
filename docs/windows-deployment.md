# Run a published app on Windows

[README](../README.md) · [Installation](installation.md) · [All documentation](index.md)

For editing code with live reload, use [local setup](onboarding.md). This page runs the production browser bundle and server together, without Vite.

## 1. Build a Windows x64 package

On the build machine, complete [tool installation and version selection](installation.md), then run from the repository root in PowerShell:

```powershell
dotnet run --project .\Build.fsproj -- Bundle
dotnet publish .\src\Server\Server.fsproj -c Release -r win-x64 --self-contained false -p:UseAppHost=false -o .\publish-windows
Copy-Item .\deploy\public .\publish-windows\public -Recurse -Force
```

Use a fresh `publish-windows` folder. Stop if any command fails. The package includes `Server.dll`, its dependencies/configuration, and `public/index.html`. The Web SDK also generates `web.config` for IIS hosting. For an ARM64 host, publish for `win-arm64` instead.

## 2. Install and run

On a Windows machine that only runs the package, install [ASP.NET Core Runtime **8**](https://dotnet.microsoft.com/en-us/download/dotnet/8.0). A machine with the .NET 8 SDK already has the runtime. Node is not needed here.

Copy the **contents** of `publish-windows` to a dedicated application directory, for example `C:\Apps\TopasRunOrchestration`. Keep simulation data in a separate directory such as `C:\Tsebt`.

In PowerShell on that machine:

```powershell
Set-Location C:\Apps\TopasRunOrchestration
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5000'
$env:Tsebt__AppRoot = 'C:\Tsebt'
$env:Tsebt__Paths__Database = 'database/app.db'
dotnet .\Server.dll
```

Open **[http://localhost:5000/index.html](http://localhost:5000/index.html)**. `/` may show the server health/status response. Keep this terminal open; Ctrl+C stops the app. These environment settings apply to this terminal and its child process.

The app creates its data subfolders and SQLite database. Populate templates and existing results as needed, and adapt the cluster settings in the installed `appsettings.json`. [Configuration](configuration.md) explains the fields. Starting the web app on Windows does not provide Slurm: real Run submission still needs the Linux cluster environment. Generate, Collect and Analyse can use data accessible to the Windows process.

## 3. Persistent Windows hosting

For an always-on Windows installation, **IIS** is the standard manual hosting route:

1. Enable IIS, then install the **.NET 8 Hosting Bundle** from the same download page. If the bundle was installed before IIS, repair/re-run it afterward.
2. Create an IIS site pointing to the application directory. Retain the published `web.config` and `public` folder. Use a dedicated application pool with **No Managed Code** and **Enable 32-Bit Applications: False** for the x64 package above.
3. Set the real Windows AppRoot/database paths in the installed **base `appsettings.json`**, or configure process environment variables through IIS. A PowerShell session's overrides do not configure IIS. Grant the application-pool identity read access to app files and modify access to AppRoot.
4. Configure the site binding, start the site and open `/index.html`. Follow Microsoft's [IIS publishing guide](https://learn.microsoft.com/en-us/aspnet/core/tutorials/publish-to-iis) for host-specific setup.

The current application has no native Windows Service integration or Windows update helper. The automated deployment/rollback script targets Linux systemd; IIS is a manual hosting route, not a deployment path covered by that script's checks.

## Updating a Windows installation

Build a fresh package. Stop the running app/site, back up the installed application, copy the replacement files while **preserving the installed `appsettings.json`**, then restart and check `/index.html`. Keep AppRoot outside the application directory so replacing app files does not replace data. This route has no automatic rollback; retain the backup until verification succeeds.
