<#
.SYNOPSIS
    Starts the Backlog Aspire AppHost using its HTTP-only launch profile.

.DESCRIPTION
    Running the AppHost via a plain `dotnet run` / `aspire run` normally serves the
    Aspire dashboard and OTLP endpoints over HTTPS, which requires a trusted local
    dev certificate. In sandboxed/non-interactive hosts such as the GitHub Copilot
    App session runner, the interactive certificate-trust dialog is not available
    (Windows blocks Import-Certificate with "UI is not allowed in this operation"),
    so the dashboard never loads and browsers show NET::ERR_CERT_AUTHORITY_INVALID.

    This script launches the AppHost using the "http" launch profile defined in
    src/Backlog.AppHost/Properties/launchSettings.json, which binds the dashboard,
    OTLP/gRPC, and resource-service endpoints to plain HTTP ports and sets
    ASPIRE_ALLOW_UNSECURED_TRANSPORT=true — no certificate trust required.

    It also frees any stale processes left bound to those fixed ports from a
    previous, ungracefully-terminated run (a common issue when a prior session's
    output pipe was closed early) before starting a fresh instance.

.PARAMETER LogPath
    Path to write AppHost console output to. Defaults to a timestamped file under
    $env:TEMP.

.PARAMETER Wait
    If set, the script blocks in the foreground (Ctrl+C to stop) instead of
    launching the AppHost as a detached background process.

.EXAMPLE
    pwsh -File scripts/start-aspire.ps1
    Starts the AppHost in the background and prints the dashboard URL once ready.

.EXAMPLE
    pwsh -File scripts/start-aspire.ps1 -Wait
    Starts the AppHost in the foreground so you can see live log output.
#>
[CmdletBinding()]
param(
    [string]$LogPath = (Join-Path $env:TEMP "backlog-aspire-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"),
    [switch]$Wait
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$appHostProject = Join-Path $repoRoot 'src\Backlog.AppHost\Backlog.AppHost.csproj'
$launchSettingsPath = Join-Path $repoRoot 'src\Backlog.AppHost\Properties\launchSettings.json'

if (-not (Test-Path $appHostProject)) {
    throw "AppHost project not found at '$appHostProject'. Run this script from a Backlog checkout."
}

if (-not (Test-Path $launchSettingsPath)) {
    throw "Missing '$launchSettingsPath'. The HTTP-only launch profile is required to avoid dev-cert trust prompts."
}

# Ports declared in the "http" launch profile (Properties/launchSettings.json).
$fixedPorts = 15282, 19183, 20183

Write-Host "Freeing any stale processes on Aspire ports ($($fixedPorts -join ', '))..." -ForegroundColor DarkGray
foreach ($port in $fixedPorts) {
    $conns = Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue
    foreach ($conn in $conns) {
        $procId = $conn.OwningProcess
        # Skip System/Idle PIDs (0, 4) and TIME_WAIT sockets that have no real owner.
        if ($procId -le 4) { continue }
        $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
        if ($proc) {
            Write-Host "  Stopping stale process $($proc.ProcessName) (PID $procId) on port $port" -ForegroundColor DarkGray
            Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
        }
    }
}
Start-Sleep -Seconds 1

Write-Host "Starting Aspire for GitHub Copilot App (Backlog.AppHost, http profile)..." -ForegroundColor Cyan
Write-Host "Log file: $LogPath" -ForegroundColor DarkGray

$runArgs = @('run', '--project', $appHostProject, '--launch-profile', 'http')

if ($Wait) {
    & dotnet @runArgs
    return
}

$process = Start-Process -FilePath 'dotnet' -ArgumentList $runArgs -WorkingDirectory $repoRoot `
    -RedirectStandardOutput $LogPath -RedirectStandardError "$LogPath.err" -PassThru -WindowStyle Hidden

Write-Host "AppHost starting in background (PID $($process.Id))..." -ForegroundColor DarkGray

$dashboardUrl = $null
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline -and -not $dashboardUrl) {
    Start-Sleep -Seconds 1
    if (Test-Path $LogPath) {
        $match = Select-String -Path $LogPath -Pattern 'Login to the dashboard at\s+(\S+)' -ErrorAction SilentlyContinue |
            Select-Object -Last 1
        if ($match) {
            $dashboardUrl = $match.Matches[0].Groups[1].Value
        }
    }
    if ($process.HasExited) {
        Write-Host "AppHost process exited early (exit code $($process.ExitCode)). Check log: $LogPath" -ForegroundColor Red
        Get-Content $LogPath -Tail 40 -ErrorAction SilentlyContinue
        return
    }
}

if ($dashboardUrl) {
    Write-Host ""
    Write-Host "Aspire dashboard ready:" -ForegroundColor Green
    Write-Host "  $dashboardUrl" -ForegroundColor Green
    Write-Host ""
    Write-Host "Stop it with: Stop-Process -Id $($process.Id)" -ForegroundColor DarkGray
}
else {
    Write-Host "Timed out waiting for the dashboard to report ready. Check log: $LogPath" -ForegroundColor Yellow
}
