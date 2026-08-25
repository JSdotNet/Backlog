<#
.SYNOPSIS
    Lists the repository-relative paths a scheduled release gate must watch for one head project.

.DESCRIPTION
    A release gate asks "did anything that ships in this package change since the last
    release?", and answers it with `git diff -- <paths>`. The paths therefore have to name
    every project the head composes.

    That list used to be written out by hand in each workflow, and it went stale twice: the
    `src/UI` -> `src/Core` restructure left both of the mobile gate's shared paths pointing at
    directories that no longer exist, and the desktop gate never grew the Dashboard, Roadmap
    and Sessions modules as they were added. Neither failure was visible, because a git
    pathspec that matches nothing is not an error -- it just reports no changed files, so the
    gate concludes there is nothing to release and skips the run.

    So the list is derived instead. This walks the head project's transitive ProjectReference
    graph and emits one repository-relative directory per project. A new module reaches the
    gate the moment the head references it, which is the same moment it starts shipping.

.PARAMETER HeadProject
    Repository-relative path to the head .csproj -- the project producing the package released.

.PARAMETER Extra
    Additional repository-relative paths to include: the solution file, packaging assets, the
    workflow itself. Anything that belongs in the gate but is not a referenced project.

.PARAMETER RepositoryRoot
    Repository root. Defaults to the parent of this script's directory.

.OUTPUTS
    System.String. One repository-relative path per line, forward-slashed and sorted.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $HeadProject,

    [string[]] $Extra = @(),

    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$rootFull = [System.IO.Path]::GetFullPath($RepositoryRoot)

# Case-insensitive: the same project can be spelled either way across a reference chain and
# Windows resolves both to one file. Visiting it twice would be harmless, but the recursion
# needs this set to terminate on a cycle.
$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

function Get-ProjectClosure {
    param([string] $ProjectPath)

    $full = [System.IO.Path]::GetFullPath($ProjectPath)
    if (-not $seen.Add($full)) { return }

    if (-not (Test-Path -LiteralPath $full)) {
        throw "Project '$full' is referenced but does not exist. The reference graph is broken."
    }

    $projectDir = Split-Path -Parent $full
    $xml = [xml](Get-Content -LiteralPath $full -Raw)

    # Namespace-agnostic: SDK-style projects carry no default namespace, but a project that
    # predates them would, and a plain '//ProjectReference' would then match nothing at all.
    foreach ($node in $xml.SelectNodes('//*[local-name()="ProjectReference"]')) {
        $include = $node.GetAttribute('Include')
        if ([string]::IsNullOrWhiteSpace($include)) { continue }

        # MSBuild writes these with backslashes whatever the host platform; .NET path APIs
        # accept forward slashes on Windows too, so normalising one way covers both.
        $relative = $include.Replace('\', '/')
        Get-ProjectClosure -ProjectPath (Join-Path $projectDir $relative)
    }
}

Get-ProjectClosure -ProjectPath (Join-Path $rootFull $HeadProject)

$paths = foreach ($project in $seen) {
    $dir = [System.IO.Path]::GetFullPath((Split-Path -Parent $project))

    if (-not $dir.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Project '$project' sits outside the repository root '$rootFull'."
    }

    # git pathspecs want forward slashes on every platform.
    $dir.Substring($rootFull.Length).Trim([char]0x5C, [char]0x2F).Replace('\', '/')
}

@($paths) + @($Extra) | Where-Object { $_ } | Sort-Object -Unique
