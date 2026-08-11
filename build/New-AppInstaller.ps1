<#
.SYNOPSIS
    Generates the Backlog desktop App Installer (.appinstaller) file from the
    template by substituting release-specific values.

.DESCRIPTION
    The .appinstaller is the stable update source sideloaded from GitHub Releases.
    Its MainPackage Name, Publisher, Version, and ProcessorArchitecture MUST match
    exactly what the signed MSIX declares (from AppxManifest.xml / the signing
    cert), or Windows will refuse to apply updates. Pass MsixPath in the release
    pipeline so those values are read from the built package instead of relying on
    defaults.

    Versions are 4-part (e.g. 1.2.3.0) as MSIX requires.

.PARAMETER Version
    The 4-part package version, e.g. "1.2.3.0". Used for both the AppInstaller
    element and the MainPackage element unless MsixPath is supplied, in which case
    the MSIX manifest version is authoritative.

.PARAMETER Tag
    The Git/Release tag the MSIX asset is published under, e.g. "v1.2.3".

.PARAMETER MsixFileName
    The MSIX asset file name as uploaded to the release, e.g.
    "Backlog.Desktop_1.2.3.0_x64.msix".

.PARAMETER MsixPath
    Optional path to the built MSIX. When supplied, Name, Publisher, Version, and
    ProcessorArchitecture are read from AppxManifest.xml so the generated
    .appinstaller cannot drift from the signed package.

.PARAMETER OutputPath
    Where to write the generated .appinstaller file.

.PARAMETER Architecture
    Processor architecture declared in the package. Defaults to "x64".

.PARAMETER Name
    Package identity name. Must match Package.appxmanifest. Defaults to
    "JSdotNet.Backlog.Desktop".

.PARAMETER Publisher
    Package publisher (certificate subject). Must match the signing certificate
    and Package.appxmanifest. Defaults to "CN=JSdotNet".

.PARAMETER TemplatePath
    The .appinstaller template. Defaults to the sibling template file.

.EXAMPLE
    ./New-AppInstaller.ps1 -Version 1.2.3.0 -Tag v1.2.3 `
        -MsixFileName Backlog.Desktop_1.2.3.0_x64.msix `
        -MsixPath ./out/Backlog.Desktop_1.2.3.0_x64.msix `
        -OutputPath ./out/Backlog.Desktop.appinstaller
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$MsixFileName,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$MsixPath,
    [string]$Architecture = 'x64',
    [string]$Name = 'JSdotNet.Backlog.Desktop',
    [string]$Publisher = 'CN=JSdotNet',
    [string]$TemplatePath = (Join-Path $PSScriptRoot 'Backlog.Desktop.appinstaller.template')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $TemplatePath)) {
    throw "Template not found: $TemplatePath"
}

if ($MsixPath) {
    if (-not (Test-Path -LiteralPath $MsixPath)) {
        throw "MSIX not found: $MsixPath"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($MsixPath)
    try {
        $entry = $zip.GetEntry('AppxManifest.xml')
        if (-not $entry) {
            throw "MSIX does not contain AppxManifest.xml: $MsixPath"
        }

        $reader = New-Object System.IO.StreamReader($entry.Open())
        try {
            [xml]$manifest = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $zip.Dispose()
    }

    $identity = $manifest.Package.Identity
    if (-not $identity) {
        throw "MSIX AppxManifest.xml does not contain a Package Identity: $MsixPath"
    }

    $Name = $identity.Name
    $Publisher = $identity.Publisher
    $Version = $identity.Version
    $Architecture = $identity.ProcessorArchitecture
}

if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Version must be a 4-part version (e.g. 1.2.3.0), got '$Version'."
}

$template = Get-Content -LiteralPath $TemplatePath -Raw

$content = $template `
    -replace '\{VERSION\}', $Version `
    -replace '\{NAME\}', $Name `
    -replace '\{PUBLISHER\}', $Publisher `
    -replace '\{ARCH\}', $Architecture `
    -replace '\{TAG\}', $Tag `
    -replace '\{MSIX_FILE_NAME\}', $MsixFileName

# Fail loudly if any token was left unsubstituted.
$leftovers = @([regex]::Matches($content, '\{[A-Z_]+\}') | ForEach-Object { $_.Value } | Sort-Object -Unique)
if ($leftovers.Count -gt 0) {
    throw "Unsubstituted tokens remain: $($leftovers -join ', ')"
}

# Validate the result is well-formed XML before writing it out.
try {
    [xml]$null = $content
}
catch {
    throw "Generated .appinstaller is not well-formed XML: $($_.Exception.Message)"
}

$outputDir = Split-Path -Parent $OutputPath
if ($outputDir -and -not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Write UTF-8 without BOM.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($OutputPath, $content, $utf8NoBom)

Write-Output "Wrote App Installer to $OutputPath (version $Version, tag $Tag)."
