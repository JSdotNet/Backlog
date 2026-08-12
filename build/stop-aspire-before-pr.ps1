<#
.SYNOPSIS
    Stops Aspire processes for this checkout before creating a pull request.

.DESCRIPTION
    The PR workflow should not leave a local Aspire AppHost or its development
    control-plane processes running after validation. This script first asks the
    Aspire CLI to stop the current checkout, then force-stops any remaining
    Aspire/AppHost processes whose command line is scoped to this repository.

.PARAMETER RepositoryRoot
    Repository checkout to clean up. Defaults to the parent of this script's
    directory.

.PARAMETER AppHostProjectPath
    AppHost project path, absolute or relative to RepositoryRoot. Defaults to
    aspire.config.json when present, otherwise the Backlog AppHost path.

.PARAMETER SkipAspireCli
    Skip the Aspire CLI stop attempt and only perform process cleanup. Intended
    for script validation or constrained environments.

.PARAMETER ListOnly
    List matching repository-scoped Aspire processes without stopping them.
.EXAMPLE
    ./build/stop-aspire-before-pr.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$RepositoryRoot,
    [string]$AppHostProjectPath,
    [switch]$SkipAspireCli,
    [switch]$ListOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-FullPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$BasePath
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

if (-not $RepositoryRoot) {
    $RepositoryRoot = Join-Path $PSScriptRoot '..'
}

$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path

if (-not $AppHostProjectPath) {
    $aspireConfigPath = Join-Path $RepositoryRoot 'aspire.config.json'
    if (Test-Path -LiteralPath $aspireConfigPath) {
        $aspireConfig = Get-Content -LiteralPath $aspireConfigPath -Raw | ConvertFrom-Json
        $AppHostProjectPath = $aspireConfig.appHost.path
    }
    else {
        $AppHostProjectPath = 'src/Aspire/Backlog.Aspire.AppHost/Backlog.Aspire.AppHost.csproj'
    }
}

$appHostFullPath = Resolve-FullPath -Path $AppHostProjectPath -BasePath $RepositoryRoot
$appHostProjectName = [System.IO.Path]::GetFileNameWithoutExtension($appHostFullPath)

if (-not (Test-Path -LiteralPath $appHostFullPath)) {
    throw "AppHost project not found: $appHostFullPath"
}

if (-not $ListOnly) {
    Push-Location -LiteralPath $RepositoryRoot
    try {
        if (-not $SkipAspireCli) {
            $aspireCommand = Get-Command aspire -ErrorAction SilentlyContinue
            if ($aspireCommand) {
                Write-Output 'Stopping Aspire through the Aspire CLI...'
                $aspireStopOutput = & $aspireCommand.Source stop --apphost $appHostFullPath --non-interactive --nologo 2>&1
                $aspireStopExitCode = $LASTEXITCODE
                $aspireStopOutput | ForEach-Object { Write-Output $_ }

                if ($aspireStopExitCode -ne 0) {
                    $combinedOutput = $aspireStopOutput -join [Environment]::NewLine
                    if ($combinedOutput -match 'No running AppHosts found') {
                        Write-Output 'No running Aspire AppHost was found for this checkout.'
                    }
                    else {
                        throw "aspire stop failed with exit code $aspireStopExitCode."
                    }
                }
            }
            else {
                Write-Warning 'Aspire CLI not found; falling back to scoped process cleanup.'
            }
        }
    }
    finally {
        Pop-Location
    }
}

$escapedRepositoryRoot = [regex]::Escape($RepositoryRoot)
$escapedAppHostFullPath = [regex]::Escape($appHostFullPath)
$escapedAppHostProjectName = [regex]::Escape($appHostProjectName)
$aspireRunPattern = '(?i)\baspire(\.exe)?\s+(run|start)\b'
$dcpPattern = '(?i)(\\|/)dcp(\.exe)?(\s|$)'
$aspireWorkingDataPattern = '(?i)(\\|/)\.aspire(\\|/)'

$candidateProcesses = @(Get-CimInstance Win32_Process | Where-Object {
    if (-not $_.CommandLine -or $_.ProcessId -eq $PID) {
        return $false
    }

    $commandLine = $_.CommandLine
    $isScopedToRepository = $commandLine -match $escapedRepositoryRoot
    $isAppHostProcess = $commandLine -match $escapedAppHostFullPath -or (
        $isScopedToRepository -and $commandLine -match $escapedAppHostProjectName
    )
    $isAspireProcess = $isScopedToRepository -and (
        $commandLine -match $aspireRunPattern -or
        $commandLine -match $dcpPattern -or
        $commandLine -match $aspireWorkingDataPattern
    )

    return $isAppHostProcess -or $isAspireProcess
})

if ($ListOnly) {
    foreach ($process in $candidateProcesses) {
        Write-Output "Found $($process.Name) [$($process.ProcessId)]"
    }

    Write-Output "Found $($candidateProcesses.Count) repository-scoped Aspire process(es)."
    return
}

$stopProcessCommand = Get-Command ('Stop' + '-Process')
foreach ($process in $candidateProcesses) {
    $description = "$($process.Name) [$($process.ProcessId)]"
    if ($PSCmdlet.ShouldProcess($description, 'Stop repository-scoped Aspire process')) {
        Write-Output "Stopping $description"
        & $stopProcessCommand -Id $process.ProcessId -Force -ErrorAction Stop
    }
}

if ($candidateProcesses.Count -eq 0) {
    Write-Output 'No repository-scoped Aspire processes were running.'
}
else {
    Write-Output "Stopped $($candidateProcesses.Count) repository-scoped Aspire process(es)."
}
