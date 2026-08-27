#Requires -Version 7.0
<#
.SYNOPSIS
    Refresh the derived knowledge indexes (`_meta/graph.json`, `_meta/index.json`).

.DESCRIPTION
    On-demand wrapper around `.github/tools/knowledge-meta/build.mjs`, installed
    by the `knowledge-base-init` skill.

    Refreshing the indexes is deliberate, not automatic. The pull-request check
    only warns when they have drifted, because making every knowledge pull
    request carry a regenerated index is what turns those generated files into
    merge conflicts. Run this when you want your own branch current; otherwise
    the nightly job (`.github/workflows/knowledge-meta-nightly.yml`) reconciles
    the default branch on its own.

    Unlike the raw `node` invocation, this reports which index files actually
    moved, so a refresh that changed nothing is visibly a no-op.

.PARAMETER Scope
    Refresh one knowledge folder only (for example `.tech`). Omit to refresh
    every adopted scope plus the repository-wide rollup.

.PARAMETER Check
    Validate the authored Markdown without writing anything. Exits
    non-zero when a reference does not resolve or a `meta` block is
    inconsistent. This is what CI runs as its blocking step.

.PARAMETER Root
    Repository root. Defaults to the enclosing git working tree, so the script
    works from any directory inside the repository.

.EXAMPLE
    ./build/Update-KnowledgeIndex.ps1
    Refresh every adopted scope and list the index files that changed.

.EXAMPLE
    ./build/Update-KnowledgeIndex.ps1 -Scope .domain
    Refresh only the `.domain` indexes.

.EXAMPLE
    ./build/Update-KnowledgeIndex.ps1 -Check
    Validate the authored metadata without touching any file.
#>
[CmdletBinding()]
param(
    [string] $Scope,
    [switch] $Check,
    [string] $Root
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RepositoryRoot {
    param([string] $Requested)

    if ($Requested) {
        if (-not (Test-Path -LiteralPath $Requested -PathType Container)) {
            throw "Root '$Requested' is not a directory."
        }
        return (Resolve-Path -LiteralPath $Requested).Path
    }

    # Prefer the git working tree over the script's own location, so the script
    # keeps working when it is copied somewhere other than `build/`.
    $top = & git rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -eq 0 -and $top) { return (Resolve-Path -LiteralPath $top).Path }

    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
}

# Hash every committed index so we can report what the generator actually
# changed. Hashing is cheap here: these files number in the dozens, not the
# thousands, and the alternative is asking git, which fails in a non-git copy.
function Get-IndexFingerprints {
    param([string] $RepoRoot)

    $fingerprints = @{}
    $metaDirectories = Get-ChildItem -LiteralPath $RepoRoot -Directory -Recurse -Force -Filter '_meta' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](node_modules|\.git)[\\/]' }

    foreach ($directory in $metaDirectories) {
        foreach ($file in Get-ChildItem -LiteralPath $directory.FullName -File -Filter '*.json') {
            $relative = [IO.Path]::GetRelativePath($RepoRoot, $file.FullName).Replace('\', '/')
            $fingerprints[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    return $fingerprints
}

$repoRoot = Resolve-RepositoryRoot -Requested $Root
$generator = Join-Path $repoRoot '.github/tools/knowledge-meta/build.mjs'

if (-not (Test-Path -LiteralPath $generator -PathType Leaf)) {
    throw "Generator not found at '$generator'. Install it with the knowledge-base-init skill."
}
if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw 'Node.js is required to run the knowledge-meta generator, and `node` is not on PATH.'
}

$arguments = @($generator)
if ($Check) { $arguments += '--check' }
if ($Scope) { $arguments += @('--scope', $Scope) }

$before = if ($Check) { @{} } else { Get-IndexFingerprints -RepoRoot $repoRoot }

Push-Location -LiteralPath $repoRoot
try {
    & node @arguments
    $generatorExit = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($Check) {
    if ($generatorExit -ne 0) {
        Write-Host ''
        Write-Host 'Knowledge metadata has errors. Fix the Markdown above; regenerating will not clear them.' -ForegroundColor Red
    }
    exit $generatorExit
}

$after = Get-IndexFingerprints -RepoRoot $repoRoot

$added = $after.Keys | Where-Object { -not $before.ContainsKey($_) } | Sort-Object
$changed = $after.Keys | Where-Object { $before.ContainsKey($_) -and $before[$_] -ne $after[$_] } | Sort-Object
$removed = $before.Keys | Where-Object { -not $after.ContainsKey($_) } | Sort-Object

Write-Host ''
if (-not $added -and -not $changed -and -not $removed) {
    Write-Host 'Indexes already current — nothing changed.' -ForegroundColor Green
}
else {
    Write-Host 'Indexes refreshed:' -ForegroundColor Yellow
    foreach ($path in $added) { Write-Host "  added    $path" }
    foreach ($path in $changed) { Write-Host "  updated  $path" }
    foreach ($path in $removed) { Write-Host "  removed  $path" }
    Write-Host ''
    Write-Host 'Commit these alongside your Markdown, or leave them to the nightly refresh.'
}

# A non-zero exit here means the generator found errors in the authored
# Markdown. It still wrote every artifact, so the refresh above is complete;
# the exit code is about the metadata, not about the write.
exit $generatorExit
