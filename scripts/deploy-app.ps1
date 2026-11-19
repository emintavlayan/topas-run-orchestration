<#
.SYNOPSIS
Builds and deploys the orchestration app from Windows to its Linux systemd service.
.DESCRIPTION
Use -SkipBuild after tests and Bundle have passed for the current source revision.
Linux publishing always runs. Production configuration is preserved on the server.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [string]$SshTarget = 'fysiker@172.31.245.206',
    [string]$DeployPath = '/home/fysiker/WebApps/TopasRunOrchestration',
    [string]$ServiceName = 'topas-run-orchestration.service',
    [string]$HealthUrl = 'http://127.0.0.1:5000/'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# /// Runs one native command and stops deployment when it fails.
function Invoke-CheckedCommand {
    param([string]$Executable, [string[]]$CommandArguments)
    & $Executable @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE. Deployment stopped."
    }
}

# /// Quotes one argument for the remote POSIX shell.
function ConvertTo-ShellArgument {
    param([string]$Value)
    return "'" + $Value.Replace("'", "'\''") + "'"
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('topas-app-' + [Guid]::NewGuid().ToString('N'))
$publishFolder = Join-Path $temporaryRoot 'publish'
$archivePath = Join-Path $temporaryRoot 'application.tar.gz'
$remoteFolder = '/tmp/topas-app-' + [Guid]::NewGuid().ToString('N')
$remoteScript = Join-Path $PSScriptRoot 'deploy-app.sh'

Push-Location $repositoryRoot
try {
    foreach ($command in @('dotnet', 'tar', 'ssh', 'scp')) {
        Get-Command $command -ErrorAction Stop | Out-Null
    }
    if (-not $SkipBuild) {
        Write-Host 'Testing server and building the complete browser/server bundle...'
        Invoke-CheckedCommand 'dotnet' @('test', 'tests/Server.Tests/Server.Tests.fsproj')
        Invoke-CheckedCommand 'dotnet' @('run', '--project', 'Build.fsproj', '--', 'Bundle')
    }

    $publicFolder = Join-Path $repositoryRoot 'deploy/public'
    if (-not (Test-Path (Join-Path $publicFolder 'index.html') -PathType Leaf)) {
        throw 'Browser bundle is missing. Run dotnet run --project .\Build.fsproj -- Bundle first, or omit -SkipBuild.'
    }

    New-Item -ItemType Directory -Path $publishFolder -Force | Out-Null
    Write-Host 'Publishing Linux server...'
    Invoke-CheckedCommand 'dotnet' @(
        'publish', 'src/Server/Server.fsproj', '-c', 'Release', '-r', 'linux-x64',
        '--self-contained', 'false', '-p:UseAppHost=false', '-o', $publishFolder
    )
    Copy-Item -LiteralPath $publicFolder -Destination (Join-Path $publishFolder 'public') -Recurse -Force
    Get-ChildItem -LiteralPath $publishFolder -Filter 'appsettings*.json' -File | Remove-Item -Force

    Invoke-CheckedCommand 'tar' @('-czf', $archivePath, '-C', $publishFolder, '.')
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()

    Write-Host "Uploading application to $SshTarget..."
    $createFolderCommand = 'mkdir -m 700 -- ' + (ConvertTo-ShellArgument $remoteFolder)
    Invoke-CheckedCommand 'ssh' @($SshTarget, $createFolderCommand)
    Invoke-CheckedCommand 'scp' @($archivePath, $remoteScript, ($SshTarget + ':' + $remoteFolder + '/'))

    # Windows Git may check out the shell script with CRLF; strip carriage returns before Bash reads it.
    $scriptPath = ConvertTo-ShellArgument ($remoteFolder + '/deploy-app.sh')
    $remoteCommand = "sed -i 's/\r$//' $scriptPath && bash $scriptPath"
    foreach ($argument in @($DeployPath, $ServiceName, $HealthUrl, $archiveHash)) {
        $remoteCommand += ' ' + (ConvertTo-ShellArgument $argument)
    }
    Write-Host 'Deploying and restarting the service. Enter your server sudo password if requested.'
    Invoke-CheckedCommand 'ssh' @('-t', $SshTarget, $remoteCommand)
    Invoke-CheckedCommand 'ssh' @($SshTarget, ('rm -rf -- ' + (ConvertTo-ShellArgument $remoteFolder)))
    Write-Host 'Deployment completed. Refresh the browser with Ctrl+F5.'
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
