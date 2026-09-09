<#
.SYNOPSIS
    Deploys Backlog's Azure components — the AI Foundry models and the cloud sync
    tier — from a developer machine, using your own Azure sign-in.

.DESCRIPTION
    The same work the Deploy Foundry, Deploy Sync and Deploy All workflows do, run
    locally against the same templates and parameter files. Use it to deploy while
    the self-hosted runner's Azure access is broken, and to preview a template
    change before pushing it.

    Modes mirror the workflows so a local run and a CI run mean the same thing:

      validate   Template correctness only. Nothing is created or changed.
      what-if    Validate, then show what a deploy would change. Still no changes.
      deploy     Apply. For sync this provisions and then pushes the service.

    Two things this deliberately does NOT do. It does not create resource groups —
    both templates are resource-group scoped, and letting a typo spawn a second
    group is worse than failing. And it does not print the Foundry API key unless
    you ask with -ShowApiKey; the key is a bearer credential for a metered
    resource, so the default is to print the command that reads it.

.PARAMETER Component
    Which component to act on: foundry, sync, or all. Defaults to foundry.

.PARAMETER Mode
    validate, what-if, or deploy. Defaults to what-if, which changes nothing.

.PARAMETER SubscriptionId
    Target subscription. Defaults to the Sponsorship subscription both components
    live in.

.PARAMETER FoundryResourceGroup
    Existing resource group for the Foundry account. Defaults to JS-AI.

.PARAMETER FoundryEnvironment
    Selects infra/foundry/<name>.bicepparam. Defaults to backlog-ai.

.PARAMETER SyncResourceGroup
    Existing resource group for the sync tier. Required when Component includes
    sync — there is no sensible default, because the group does not exist yet.

.PARAMETER SyncLocation
    Region for the azd environment. Defaults to westeurope, matching every other
    group in the subscription.

.PARAMETER SyncEnvironment
    azd environment name. Defaults to backlog-sync.

.PARAMETER ShowApiKey
    Print the Foundry API key after a successful deploy. Off by default.

.EXAMPLE
    ./build/Deploy-Azure.ps1
    Previews the Foundry deployment. Changes nothing.

.EXAMPLE
    ./build/Deploy-Azure.ps1 -Component foundry -Mode deploy
    Deploys the Foundry account and its model deployments, then prints the
    endpoint and deployment name to paste into the desktop AI settings.

.EXAMPLE
    ./build/Deploy-Azure.ps1 -Component all -Mode deploy -SyncResourceGroup backlog-sync
    Deploys both components.
#>
#Requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('foundry', 'sync', 'all')]
    [string]$Component = 'foundry',

    [ValidateSet('validate', 'what-if', 'deploy')]
    [string]$Mode = 'what-if',

    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$SubscriptionId = '8235e3b9-4cd0-4426-879a-471503d9e4fc',

    [string]$FoundryResourceGroup = 'JS-AI',
    [string]$FoundryEnvironment = 'backlog-ai',

    [string]$SyncResourceGroup,
    [string]$SyncLocation = 'westeurope',
    [string]$SyncEnvironment = 'backlog-sync',

    [switch]$ShowApiKey
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Write-Step([string] $Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Native {
    <#
        az writes its own diagnostics; this only turns a non-zero exit into a
        terminating error, which native commands do not do on their own.
    #>
    param([scriptblock] $Command, [string] $What)

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$What failed with exit code $LASTEXITCODE."
    }
}

function Assert-Tool([string] $Name, [string] $Install) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name is not on PATH. Install it with: $Install"
    }
}

function Assert-AzureAccess([string] $Subscription) {
    <#
        `az account show` reads a cached account record and succeeds against a
        token that can no longer do anything — which is exactly how the
        self-hosted runner passed its own sign-in check and then failed the
        deployment with AADSTS50076. So prove access with a real management-plane
        call instead.
    #>
    Assert-Tool -Name 'az' -Install 'winget install Microsoft.AzureCLI'

    az account show --only-show-errors --output none 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Not signed in to Azure. Run: az login --tenant innovadis.com"
    }

    Invoke-Native { az account set --subscription $Subscription --only-show-errors } `
        "Selecting subscription $Subscription"

    $probe = az group list --subscription $Subscription --query "[0].name" --output tsv 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host $probe -ForegroundColor Red
        throw @"
Signed in, but the management plane refused the request.

If this is AADSTS50076, the token lacks an MFA claim: a cached or non-interactive
session cannot satisfy the tenant's conditional-access policy. Re-authenticate
interactively:

    az login --tenant innovadis.com
"@
    }

    $account = az account show --query "{name:name, user:user.name}" --output json | ConvertFrom-Json
    Write-Host "Signed in as $($account.user) on '$($account.name)'." -ForegroundColor DarkGray
}

function Assert-ResourceGroup([string] $Name, [string] $Subscription) {
    az group show --name $Name --subscription $Subscription --output none 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw @"
Resource group '$Name' does not exist in subscription $Subscription.

This script does not create resource groups: both templates are resource-group
scoped, and a mistyped name would otherwise quietly provision a second one.
Create it first:

    az group create --name $Name --location <region> --subscription $Subscription
"@
    }
}

function Deploy-Foundry {
    $template = Join-Path $repositoryRoot 'infra/foundry/main.bicep'
    $parameters = Join-Path $repositoryRoot "infra/foundry/$FoundryEnvironment.bicepparam"

    if (-not (Test-Path -LiteralPath $parameters)) {
        throw "Parameter file $parameters does not exist. Add one for environment '$FoundryEnvironment' before deploying to its subscription."
    }

    Assert-ResourceGroup -Name $FoundryResourceGroup -Subscription $SubscriptionId

    Write-Step 'Building the Bicep template'
    Invoke-Native { az bicep build --file $template --stdout | Out-Null } 'az bicep build'

    Write-Step 'Validating the deployment'
    Invoke-Native {
        az deployment group validate `
            --subscription $SubscriptionId `
            --resource-group $FoundryResourceGroup `
            --template-file $template `
            --parameters $parameters `
            --only-show-errors `
            --output none
    } 'az deployment group validate'
    Write-Host 'Template is valid.' -ForegroundColor Green

    if ($Mode -eq 'validate') { return }

    Write-Step 'Previewing changes (what-if)'
    Invoke-Native {
        az deployment group what-if `
            --subscription $SubscriptionId `
            --resource-group $FoundryResourceGroup `
            --template-file $template `
            --parameters $parameters
    } 'az deployment group what-if'

    if ($Mode -ne 'deploy') { return }

    # Distinct from the workflow's 'backlog-foundry-<run id>' so a local run never
    # overwrites a CI deployment record in the group's history.
    $deploymentName = "backlog-foundry-local-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

    if (-not $PSCmdlet.ShouldProcess("$FoundryResourceGroup/$FoundryEnvironment", 'Deploy Foundry account and model deployments')) {
        return
    }

    Write-Step "Deploying as $deploymentName"
    Invoke-Native {
        az deployment group create `
            --name $deploymentName `
            --subscription $SubscriptionId `
            --resource-group $FoundryResourceGroup `
            --template-file $template `
            --parameters $parameters `
            --output none
    } 'az deployment group create'

    $outputs = az deployment group show `
        --name $deploymentName `
        --subscription $SubscriptionId `
        --resource-group $FoundryResourceGroup `
        --query properties.outputs `
        --output json | ConvertFrom-Json

    if (-not $outputs -or -not $outputs.accountEndpoint.value) {
        throw "Deployment $deploymentName succeeded but returned no accountEndpoint output, so the AI connection cannot be reported."
    }

    Write-Step 'Enable the AI features'
    Write-Host 'In the desktop app, Settings -> Azure Foundry:'
    Write-Host ''
    Write-Host ("  Endpoint    {0}" -f $outputs.accountEndpoint.value) -ForegroundColor Green
    Write-Host ("  Deployment  {0}" -f $outputs.chatDeploymentName.value) -ForegroundColor Green

    if ($outputs.embeddingDeploymentName.value) {
        Write-Host ("  (knowledge search by meaning uses {0} on the same endpoint)" -f $outputs.embeddingDeploymentName.value) -ForegroundColor DarkGray
    }

    $keyCommand = "az cognitiveservices account keys list --name $($outputs.accountName.value) --resource-group $FoundryResourceGroup --subscription $SubscriptionId --query key1 --output tsv"

    if ($ShowApiKey) {
        $key = Invoke-Expression $keyCommand
        Write-Host ("  API key     {0}" -f $key) -ForegroundColor Yellow
        Write-Host '  (that key is now in your shell history and scrollback)' -ForegroundColor DarkYellow
    }
    else {
        Write-Host ''
        Write-Host '  Read the API key with:' -ForegroundColor DarkGray
        Write-Host "    $keyCommand" -ForegroundColor DarkGray
    }
}

function Deploy-Sync {
    if (-not $SyncResourceGroup) {
        throw 'Pass -SyncResourceGroup. The sync tier has no default group because it has not been created yet; see docs/deployment/sync.md.'
    }

    Assert-Tool -Name 'azd' -Install 'winget install Microsoft.Azd'
    Assert-ResourceGroup -Name $SyncResourceGroup -Subscription $SubscriptionId

    Push-Location $repositoryRoot
    try {
        Write-Step 'Signing azd in'
        # Separate from the az sign-in above: azd keeps its own token cache.
        Invoke-Native { azd auth login } 'azd auth login'

        Write-Step "Selecting azd environment $SyncEnvironment"
        # `azd env new` is not idempotent, so select an existing one and only
        # create when that fails.
        azd env select $SyncEnvironment 2>$null
        if ($LASTEXITCODE -ne 0) {
            Invoke-Native {
                azd env new $SyncEnvironment `
                    --subscription $SubscriptionId `
                    --location $SyncLocation `
                    --no-prompt
            } 'azd env new'
        }

        # infra/sync/main.bicep is resource-group scoped, so azd deploys into an
        # existing group rather than creating one.
        Invoke-Native { azd env set AZURE_RESOURCE_GROUP $SyncResourceGroup } 'azd env set'

        Write-Step 'Previewing infrastructure changes'
        Invoke-Native { azd provision --preview --no-prompt } 'azd provision --preview'

        if ($Mode -ne 'deploy') { return }

        if (-not $PSCmdlet.ShouldProcess($SyncResourceGroup, 'Provision and deploy the sync service')) {
            return
        }

        Write-Step 'Provisioning infrastructure'
        Invoke-Native { azd provision --no-prompt } 'azd provision'

        Write-Step 'Deploying the sync service'
        Invoke-Native { azd deploy sync --no-prompt } 'azd deploy'

        $values = azd env get-values --output json 2>$null | ConvertFrom-Json
        if ($values -and $values.PSObject.Properties.Name -contains 'SYNC_SERVICE_URI') {
            Write-Host ''
            Write-Host ("Sync endpoint: {0}" -f $values.SYNC_SERVICE_URI) -ForegroundColor Green
        }
    }
    finally {
        Pop-Location
    }
}

Write-Step "Backlog Azure deployment - component '$Component', mode '$Mode'"
Assert-AzureAccess -Subscription $SubscriptionId

if ($Component -in @('foundry', 'all')) { Deploy-Foundry }
if ($Component -in @('sync', 'all')) { Deploy-Sync }

Write-Host ''
if ($Mode -eq 'deploy' -and -not $WhatIfPreference) {
    Write-Host 'Done.' -ForegroundColor Green
}
elseif ($WhatIfPreference) {
    Write-Host 'Done. Nothing was changed - -WhatIf was passed.' -ForegroundColor Green
}
else {
    Write-Host "Done. Nothing was changed - mode was '$Mode'." -ForegroundColor Green
}
