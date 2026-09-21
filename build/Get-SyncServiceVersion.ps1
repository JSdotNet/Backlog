<#
.SYNOPSIS
    Prints which build of the sync service is deployed, or which build it should
    be running — one short commit sha, one line, for a system tools row to read.

.DESCRIPTION
    The two halves of the "Backlog sync service" row in the desktop app's System
    tools pane. That row is a catalog entry of provider `command`: its `detect`
    runs this script with -Side deployed, its `available` runs it with -Side
    wanted, and the pane compares the two lines and offers Update when they
    differ. Update itself is `build/Deploy-Azure.ps1 -Component sync -Mode
    deploy` — see docs/deployment/sync.md for the catalog entry.

    Both sides print the same vocabulary — the first seven characters of a commit
    sha — because the pane compares them as strings and nothing else.

      deployed   Asks the service. Its anonymous root reports the commit the
                 running container was built from (the SDK stamps HEAD into the
                 informational version on every build), so this needs no Azure
                 sign-in when -Endpoint is given. Without -Endpoint the container
                 app is found through az, by the tags azd matches on, exactly as
                 Deploy-Azure.ps1 finds it.

      wanted     Asks the repository. Not the tip of origin/main — a deploy after
                 every unrelated merge would be a row that is never current — but
                 the newest origin/main commit that touches what the deployed
                 image is built from: the Sync module, infra/sync, azure.yaml,
                 the service defaults and the props files. The deploy is built
                 at some later HEAD, so when that commit is already an ancestor
                 of the deployed one the deployed sha is printed back, and the
                 row reads as current.

    Exit code is non-zero when the side cannot be answered: the service is not
    reachable, the container app is not there, or the fetch failed. The pane
    reads that as "not installed" (deployed) or "unknown" (wanted), which is the
    honest state, and the transcript beside the row carries this script's error.

.PARAMETER Side
    deployed or wanted.

.PARAMETER Endpoint
    The sync service's base URL. Given, the deployed side is a single anonymous
    GET; absent, it is resolved through az for the container app tagged for
    -SyncEnvironment in -SyncResourceGroup.

.PARAMETER Remote
    The git remote the wanted side reads from. Defaults to origin.

.PARAMETER Branch
    The branch on that remote the wanted side reads. Defaults to main.

.PARAMETER NoFetch
    Read the remote-tracking branch as it is rather than fetching first. For a
    machine that has just fetched, or one that is offline.

.EXAMPLE
    ./build/Get-SyncServiceVersion.ps1 -Side deployed -Endpoint https://backlog-sync.example.azurecontainerapps.io

.EXAMPLE
    ./build/Get-SyncServiceVersion.ps1 -Side wanted
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('deployed', 'wanted')]
    [string]$Side,

    [string]$Endpoint,

    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$SubscriptionId = '8235e3b9-4cd0-4426-879a-471503d9e4fc',

    [string]$SyncResourceGroup = 'JS-AI',
    [string]$SyncEnvironment = 'backlog-sync',

    [string]$Remote = 'origin',
    [string]$Branch = 'main',

    [switch]$NoFetch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot

# What the deployed image is built from. The same set the Deploy Sync workflow's
# parked push trigger filtered on; a commit outside it does not change the image.
$syncPaths = @(
    'src/Modules/Sync',
    'infra/sync',
    'azure.yaml',
    'src/Aspire/Backlog.Aspire.ServiceDefaults',
    'Directory.Build.props',
    'Directory.Packages.props'
)

function Get-ShortSha([string] $Sha) {
    # A fixed seven rather than `git rev-parse --short`, which lengthens when a
    # prefix is ambiguous: the two sides must abbreviate identically or the row
    # reports an update between two spellings of one commit.
    if ($Sha.Length -lt 7) { return $Sha }
    return $Sha.Substring(0, 7).ToLowerInvariant()
}

function Invoke-Git([string[]] $Arguments) {
    $output = & git -C $repositoryRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code ${LASTEXITCODE}: $output"
    }
    return $output
}

function Resolve-SyncEndpoint {
    <#
        Deploy-Azure.ps1's Get-DeployedSyncApp, then the app's ingress. Kept in
        step by hand: both find the app by the tags azd writes, so a renamed
        environment moves both at once.
    #>
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw 'No -Endpoint given and az is not on PATH; pass the service URL or install the Azure CLI.'
    }

    $apps = az containerapp list `
        --resource-group $SyncResourceGroup `
        --subscription $SubscriptionId `
        --query '[].{name:name, tags:tags, fqdn:properties.configuration.ingress.fqdn}' `
        --only-show-errors `
        --output json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $apps) {
        throw "Could not list container apps in resource group '$SyncResourceGroup'; is az signed in?"
    }

    $found = @($apps | ConvertFrom-Json | Where-Object {
            $_.tags -and $_.tags.'azd-env-name' -eq $SyncEnvironment -and $_.tags.'azd-service-name' -eq 'sync'
        })
    if ($found.Count -eq 0) {
        throw "No sync container app tagged for environment '$SyncEnvironment' in resource group '$SyncResourceGroup'; nothing is deployed."
    }
    if ($found.Count -gt 1) {
        throw "Resource group '$SyncResourceGroup' holds $($found.Count) sync container apps tagged for environment '$SyncEnvironment'; expected one."
    }
    if (-not $found[0].fqdn) {
        throw "Container app '$($found[0].name)' has no ingress; nothing answers for it."
    }

    return "https://$($found[0].fqdn)"
}

function Get-DeployedCommit {
    $base = if ($Endpoint) { $Endpoint } else { Resolve-SyncEndpoint }
    $uri = $base.TrimEnd('/') + '/'

    try {
        $identity = Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec 30
    }
    catch {
        throw "The sync service at $uri did not answer: $($_.Exception.Message)"
    }

    if (-not ($identity.PSObject.Properties.Name -contains 'commit')) {
        throw "The service at $uri answered without a commit; it predates the build that reports one. Deploy once by hand, then this row can read it."
    }
    if ([string]::IsNullOrWhiteSpace($identity.commit)) {
        throw "The service at $uri reports no commit: its image was built outside a git checkout."
    }

    return [string]$identity.commit
}

function Get-WantedCommit {
    if (-not $NoFetch) {
        Invoke-Git @('fetch', $Remote, $Branch, '--quiet') | Out-Null
    }

    $tip = "$Remote/$Branch"
    $wanted = [string](Invoke-Git (@('log', '-1', '--format=%H', $tip, '--') + $syncPaths))
    if ([string]::IsNullOrWhiteSpace($wanted)) {
        throw "No commit on $tip touches the sync path set."
    }

    return $wanted.Trim()
}

switch ($Side) {
    'deployed' {
        Get-ShortSha (Get-DeployedCommit)
    }
    'wanted' {
        $wanted = Get-WantedCommit

        # Current when the newest sync-touching commit is already in the deployed
        # build's history. The deployed sha may be unknown here — built from a
        # branch this clone never fetched — and then it is simply not an ancestor,
        # which reads as an update due; a deploy from main resolves that.
        $deployed = $null
        try { $deployed = Get-DeployedCommit } catch { $deployed = $null }

        if ($deployed) {
            & git -C $repositoryRoot merge-base --is-ancestor $wanted $deployed 2>$null
            if ($LASTEXITCODE -eq 0) {
                Get-ShortSha $deployed
                break
            }
        }

        Get-ShortSha $wanted
    }
}
