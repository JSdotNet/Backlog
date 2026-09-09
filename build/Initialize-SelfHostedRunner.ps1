<#
.SYNOPSIS
    Sets up the Windows self-hosted GitHub Actions runner that Deploy Foundry
    runs on, including the Azure access it needs.

.DESCRIPTION
    Deploy Foundry is the one workflow that does not authenticate to Azure itself
    — it expects the runner to already have access. That makes the runner host's
    Azure sign-in part of the deployment configuration, and this script sets up
    both halves in one pass:

      1. Prerequisites: Azure CLI and the Bicep CLI. Neither is installed by the
         workflow, and both must be present.
      2. The runner service: downloads the pinned runner release, verifies its
         checksum, registers it against the repository, and installs it as a
         Windows service running under a chosen account.
      3. Azure access: signs in, proves the sign-in can actually reach the
         management plane, and grants the identity Cognitive Services Contributor
         on the target resource group.

    Step 3 verifies with a real management-plane call rather than `az account
    show`. That distinction matters: a cached account record keeps `az account
    show` succeeding long after the token stops working, which is how the runner
    passed the workflow's own sign-in check and then failed the deployment with
    AADSTS50076.

    Prefer -UseManagedIdentity. A conditional-access policy on the tenant requires
    MFA that a non-interactive user session cannot satisfy, so a user sign-in
    decays and has to be renewed by hand at the console. Managed identity is not
    subject to conditional access and does not decay.

.PARAMETER Repository
    The repository the runner serves, as owner/name.

.PARAMETER RunnerName
    Name the runner registers under. Defaults to the machine name.

.PARAMETER InstallPath
    Where the runner is installed. Defaults to C:\actions-runner.

.PARAMETER RunnerVersion
    Pinned runner release. Left blank, the latest release is resolved and its
    version reported before anything is downloaded.

.PARAMETER Labels
    Extra labels beyond the implicit self-hosted/Windows/X64 ones. Deploy Foundry
    targets bare `self-hosted`, so none are required.

.PARAMETER ServiceAccount
    Windows account the runner service runs as, e.g. 'DOMAIN\svc-actions'. Left
    blank, the runner's default (NT AUTHORITY\NETWORK SERVICE) is used. The Azure
    sign-in must belong to THIS account — a sign-in performed as your own
    interactive user is invisible to the service.

.PARAMETER UseManagedIdentity
    Authenticate Azure with the host's managed identity instead of a user
    sign-in. Strongly preferred; requires a managed identity assigned to the VM.

.PARAMETER SubscriptionId
    Subscription the runner deploys into.

.PARAMETER ResourceGroup
    Resource group holding the Foundry account, granted to the runner identity.

.PARAMETER Tenant
    Entra tenant used for an interactive sign-in.

.PARAMETER SkipRunnerInstall
    Configure and verify Azure access only, leaving an existing runner alone.
    Use this to repair the AADSTS50076 failure without touching the runner.

.PARAMETER VerifyOnly
    Change nothing. Report whether the prerequisites, the runner service, and
    Azure access are each in place. Safe to run at any time.

.EXAMPLE
    ./build/Initialize-SelfHostedRunner.ps1 -VerifyOnly
    Diagnoses the current state without changing anything.

.EXAMPLE
    ./build/Initialize-SelfHostedRunner.ps1 -SkipRunnerInstall -UseManagedIdentity
    Repairs Azure access on a runner that is already installed.

.EXAMPLE
    ./build/Initialize-SelfHostedRunner.ps1 -ServiceAccount 'INNOVADIS\svc-actions' -UseManagedIdentity
    Full setup: prerequisites, runner service, and Azure access.
#>
#Requires -Version 7.0
#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Repository = 'JSdotNet/Backlog',
    [string]$RunnerName = $env:COMPUTERNAME,
    [string]$InstallPath = 'C:\actions-runner',
    [string]$RunnerVersion,
    [string[]]$Labels = @(),
    [string]$ServiceAccount,

    [switch]$UseManagedIdentity,

    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$SubscriptionId = '8235e3b9-4cd0-4426-879a-471503d9e4fc',

    [string]$ResourceGroup = 'JS-AI',
    [string]$Tenant = 'innovadis.com',

    [switch]$SkipRunnerInstall,
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:Failures = @()

function Write-Step([string] $Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Good([string] $Message) { Write-Host "    OK    $Message" -ForegroundColor Green }
function Write-Bad([string] $Message) {
    Write-Host "    FAIL  $Message" -ForegroundColor Red
    $script:Failures += $Message
}

function Test-Tool([string] $Name) { [bool](Get-Command $Name -ErrorAction SilentlyContinue) }

function Invoke-Native {
    param([scriptblock] $Command, [string] $What)
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed with exit code $LASTEXITCODE." }
}

# --------------------------------------------------------------------------
# 1. Prerequisites
# --------------------------------------------------------------------------
function Install-Prerequisites {
    Write-Step 'Prerequisites'

    if (Test-Tool 'az') {
        Write-Good "Azure CLI: $((az version --output json | ConvertFrom-Json).'azure-cli')"
    }
    elseif ($VerifyOnly) {
        Write-Bad 'Azure CLI is not installed.'
        return
    }
    else {
        if ($PSCmdlet.ShouldProcess('Azure CLI', 'Install')) {
            Invoke-Native { winget install --exact --id Microsoft.AzureCLI --accept-package-agreements --accept-source-agreements } 'Installing the Azure CLI'
            # winget updates the machine PATH, which this process does not inherit.
            $env:PATH = [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('PATH', 'User')
        }
    }

    if (-not (Test-Tool 'az')) { return }

    # The Bicep CLI is a separate download from the Azure CLI, and the workflow's
    # first real step is `az bicep build`.
    $bicep = az bicep version 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Good "Bicep CLI: $($bicep -replace '\s+', ' ')"
    }
    elseif ($VerifyOnly) {
        Write-Bad 'Bicep CLI is not installed (az bicep install).'
    }
    elseif ($PSCmdlet.ShouldProcess('Bicep CLI', 'Install')) {
        Invoke-Native { az bicep install } 'az bicep install'
        Write-Good 'Bicep CLI installed.'
    }
}

# --------------------------------------------------------------------------
# 2. The runner service
# --------------------------------------------------------------------------
function Get-RunnerService {
    # The runner registers as 'actions.runner.<owner>-<repo>.<name>'.
    Get-Service -Name "actions.runner.*" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "*$($Repository -replace '/', '-')*" } |
        Select-Object -First 1
}

function Install-Runner {
    Write-Step 'Runner service'

    $existing = Get-RunnerService
    if ($existing) {
        Write-Good "Service '$($existing.Name)' is $($existing.Status)."
        if ($existing.Status -ne 'Running') {
            Write-Bad "Service is $($existing.Status), not Running."
        }
        return
    }

    if ($VerifyOnly) { Write-Bad "No runner service is registered for $Repository."; return }
    if ($SkipRunnerInstall) { Write-Host '    Skipped (-SkipRunnerInstall).' -ForegroundColor DarkGray; return }

    if (-not (Test-Tool 'gh')) {
        throw 'The GitHub CLI (gh) is required to mint a runner registration token. Install it with: winget install GitHub.cli'
    }

    # A registration token is short-lived (about an hour) and single-use, which is
    # why it is minted here rather than passed in and left lying in a parameter.
    Write-Host '    Requesting a registration token...' -ForegroundColor DarkGray
    $token = gh api --method POST "repos/$Repository/actions/runners/registration-token" --jq .token
    if ($LASTEXITCODE -ne 0 -or -not $token) {
        throw "Could not get a registration token for $Repository. Check 'gh auth status' — the active account needs admin on the repository."
    }

    if (-not $RunnerVersion) {
        $RunnerVersion = (gh api repos/actions/runner/releases/latest --jq .tag_name) -replace '^v', ''
        if ($LASTEXITCODE -ne 0 -or -not $RunnerVersion) { throw 'Could not resolve the latest runner release.' }
    }
    Write-Host "    Runner version $RunnerVersion" -ForegroundColor DarkGray

    $package = "actions-runner-win-x64-$RunnerVersion.zip"
    $archive = Join-Path $env:TEMP $package

    if (-not $PSCmdlet.ShouldProcess("$InstallPath", "Install runner $RunnerVersion for $Repository")) { return }

    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null

    if (-not (Test-Path -LiteralPath $archive)) {
        Write-Host "    Downloading $package..." -ForegroundColor DarkGray
        Invoke-WebRequest -Uri "https://github.com/actions/runner/releases/download/v$RunnerVersion/$package" -OutFile $archive
    }

    # The release notes publish the SHA-256 for each asset; compare against it
    # rather than trusting the download.
    $expected = (gh api repos/actions/runner/releases/tags/v$RunnerVersion --jq .body 2>$null |
        Select-String -Pattern "([0-9a-f]{64})\s*$package" -AllMatches).Matches.Groups[1].Value
    $actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()

    if ($expected) {
        if ($actual -ne $expected) {
            Remove-Item -LiteralPath $archive -Force
            throw "Checksum mismatch for $package. Expected $expected, got $actual. The download was discarded."
        }
        Write-Good 'Package checksum verified.'
    }
    else {
        Write-Host "    Could not find a published checksum in the release notes; downloaded SHA-256 is $actual" -ForegroundColor DarkYellow
    }

    Expand-Archive -LiteralPath $archive -DestinationPath $InstallPath -Force

    $configure = @(
        '--url', "https://github.com/$Repository"
        '--token', $token
        '--name', $RunnerName
        '--unattended'
        '--replace'
        '--runasservice'
    )
    if ($Labels.Count -gt 0) { $configure += @('--labels', ($Labels -join ',')) }
    if ($ServiceAccount) {
        # No password here: use a group managed service account (gMSA), which needs
        # none, or set the service's credentials afterwards. Passing a password on
        # a command line puts it in the process table and the transcript.
        $configure += @('--windowslogonaccount', $ServiceAccount)
    }

    Push-Location $InstallPath
    try {
        Invoke-Native { & '.\config.cmd' @configure } 'Runner configuration'
    }
    finally {
        Pop-Location
    }

    Write-Good "Runner '$RunnerName' registered and installed as a service."

    if ($ServiceAccount) {
        Write-Host ''
        Write-Host "    The service runs as $ServiceAccount. The Azure sign-in below must be" -ForegroundColor Yellow
        Write-Host '    performed as THAT account, not as you. A sign-in in your own session is' -ForegroundColor Yellow
        Write-Host '    stored in your profile and the service will never see it.' -ForegroundColor Yellow
    }
}

# --------------------------------------------------------------------------
# 3. Azure access
# --------------------------------------------------------------------------
function Test-ManagementPlaneAccess {
    <#
        The check that matters. `az account show` reads a cached record and keeps
        succeeding against a token that can no longer do anything, so it is only
        used to detect "never signed in". Reaching the management plane is what
        proves the runner can actually deploy.
    #>
    az account show --only-show-errors --output none 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Bad 'Not signed in to Azure at all.'
        return $false
    }

    az account set --subscription $SubscriptionId --only-show-errors 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Bad "Signed in, but this identity cannot see subscription $SubscriptionId."
        return $false
    }

    $probe = az group show --name $ResourceGroup --subscription $SubscriptionId --output none 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Bad "Signed in, but the management plane refused a read of '$ResourceGroup'."
        Write-Host "          $probe" -ForegroundColor DarkRed
        if ("$probe" -match 'AADSTS50076|multi-factor') {
            Write-Host ''
            Write-Host '    This is the AADSTS50076 failure. The token has no MFA claim, so a' -ForegroundColor Yellow
            Write-Host '    conditional-access policy blocks management-plane calls even though' -ForegroundColor Yellow
            Write-Host '    the account record still looks signed in. Re-run without -VerifyOnly.' -ForegroundColor Yellow
        }
        return $false
    }

    $who = az account show --query "user.name" --output tsv
    Write-Good "Management plane reachable as $who."
    return $true
}

function Set-AzureAccess {
    Write-Step 'Azure access'

    if (-not (Test-Tool 'az')) { Write-Bad 'Azure CLI missing; cannot check Azure access.'; return }

    if (Test-ManagementPlaneAccess) { return }
    if ($VerifyOnly) { return }

    if ($UseManagedIdentity) {
        if ($PSCmdlet.ShouldProcess('Azure', 'Sign in with the host managed identity')) {
            Write-Host '    Signing in with the host managed identity...' -ForegroundColor DarkGray
            Invoke-Native { az login --identity --only-show-errors --output none } 'az login --identity'
        }
    }
    else {
        Write-Host ''
        Write-Host '    Interactive sign-in required. A browser will open.' -ForegroundColor Yellow
        Write-Host '    This decays when the MFA claim ages out and must then be repeated at the' -ForegroundColor Yellow
        Write-Host '    console as the service account. Prefer -UseManagedIdentity.' -ForegroundColor Yellow
        if ($PSCmdlet.ShouldProcess('Azure', "Interactive sign-in to tenant $Tenant")) {
            Invoke-Native { az login --tenant $Tenant --only-show-errors --output none } 'az login'
        }
    }

    if (-not (Test-ManagementPlaneAccess)) {
        throw 'Azure sign-in completed but the management plane is still unreachable. Nothing further can be configured.'
    }

    Grant-FoundryAccess
}

function Grant-FoundryAccess {
    <#
        Microsoft's Foundry guidance asks for permissions equivalent to Cognitive
        Services Contributor on the Foundry resource or its group. Scoped to the
        group, not the subscription.
    #>
    $scope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup"

    # A managed identity is a service principal, not a user, so `az ad
    # signed-in-user show` fails for it. Its client id is what `az account show`
    # reports as the user name; the object id the role assignment needs is looked
    # up from that.
    $principalId = if ($UseManagedIdentity) {
        $clientId = az account show --query "user.name" --output tsv 2>$null
        if ($clientId -and $clientId -ne 'systemAssignedIdentity') {
            az ad sp show --id $clientId --query id --output tsv 2>$null
        }
        else {
            # System-assigned identities report no client id here; the object id
            # lives on the host VM resource, which this script cannot name.
            $null
        }
    }
    else {
        az ad signed-in-user show --query id --output tsv 2>$null
    }

    if (-not $principalId) {
        Write-Host ''
        Write-Host '    Could not resolve the principal id automatically.' -ForegroundColor Yellow
        Write-Host '    For a system-assigned identity, read it off the runner VM:' -ForegroundColor Yellow
        Write-Host '      az vm identity show --name <vm> --resource-group <vm-group> --query principalId --output tsv' -ForegroundColor DarkGray
        Write-Host '    then grant it:' -ForegroundColor Yellow
        Write-Host "      az role assignment create --assignee <principal-id> --role `"Cognitive Services Contributor`" --scope $scope" -ForegroundColor DarkGray
        return
    }

    $existing = az role assignment list --assignee $principalId --scope $scope --include-inherited `
        --query "[?roleDefinitionName=='Cognitive Services Contributor' || roleDefinitionName=='Contributor' || roleDefinitionName=='Owner'].roleDefinitionName" `
        --output tsv 2>$null

    if ($existing) {
        Write-Good "Identity already holds '$($existing -split "`n" | Select-Object -First 1)' over $ResourceGroup."
        return
    }

    if ($PSCmdlet.ShouldProcess($scope, 'Grant Cognitive Services Contributor')) {
        az role assignment create --assignee $principalId --role 'Cognitive Services Contributor' --scope $scope --output none 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Bad "Could not create the role assignment. You need Owner or User Access Administrator on $ResourceGroup to grant it."
            return
        }
        Write-Good "Granted Cognitive Services Contributor over $ResourceGroup."
    }
}

# --------------------------------------------------------------------------

Write-Host ''
Write-Host "Backlog self-hosted runner setup - $Repository" -ForegroundColor White
if ($VerifyOnly) { Write-Host 'Verify only. Nothing will be changed.' -ForegroundColor DarkGray }

Install-Prerequisites
Install-Runner
Set-AzureAccess

Write-Step 'Summary'
if ($script:Failures.Count -eq 0) {
    Write-Host '    Everything checked is in place.' -ForegroundColor Green
    Write-Host ''
    Write-Host '    Verify end to end by running Deploy Foundry in what-if mode:' -ForegroundColor DarkGray
    Write-Host "      gh workflow run deploy-foundry.yml -f mode=what-if" -ForegroundColor DarkGray
    exit 0
}

Write-Host "    $($script:Failures.Count) problem(s):" -ForegroundColor Red
$script:Failures | ForEach-Object { Write-Host "      - $_" -ForegroundColor Red }
exit 1
