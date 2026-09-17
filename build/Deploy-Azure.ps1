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

    Both components land in the same resource group (JS-AI) by default. One thing
    that makes dangerous: `azd down` on the sync environment deletes the resource
    group it was pointed at, Foundry account included. Never run it against a
    shared group; remove the sync resources by hand instead.

    The sync tier needs one secret in the process environment before it runs, in
    every mode: SYNC_TOKEN_SIGNING_KEY, the base64 HMAC key (32 bytes or more) the
    sync service signs device tokens with. azd reads it from there when it
    substitutes infra/sync/main.parameters.json. The script fills it in when the
    shell does not: from the secret already on the deployed container app, so a
    re-run keeps its key, or freshly minted on a first deploy. Set it in the
    shell yourself only to rotate it. It is deliberately never passed to
    `azd env set`, which would write it to .azure/<env>/.env on disk, and never
    printed.

.PARAMETER Component
    Which component to act on: foundry, sync, or all. Defaults to all.

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
    Existing resource group for the sync tier. Defaults to JS-AI, the same group as
    Foundry: one personal deployment does not earn a second group.

.PARAMETER SyncLocation
    Region for the azd environment. Defaults to westeurope, matching every other
    group in the subscription.

.PARAMETER SyncEnvironment
    azd environment name. Defaults to backlog-sync.

.PARAMETER ShowApiKey
    Print the Foundry API key after a successful deploy. Off by default.

.EXAMPLE
    ./build/Deploy-Azure.ps1
    Previews both deployments. Changes nothing.

.EXAMPLE
    ./build/Deploy-Azure.ps1 -Mode deploy
    Deploys the Foundry account and its model deployments, then provisions and
    pushes the sync service, then prints the endpoint and deployment name to
    paste into the desktop AI settings and the sync endpoint.

.EXAMPLE
    ./build/Deploy-Azure.ps1 -Component foundry -Mode deploy
    Deploys Foundry only.
#>
#Requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('foundry', 'sync', 'all')]
    [string]$Component = 'all',

    [ValidateSet('validate', 'what-if', 'deploy')]
    [string]$Mode = 'what-if',

    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$SubscriptionId = '8235e3b9-4cd0-4426-879a-471503d9e4fc',

    [string]$FoundryResourceGroup = 'JS-AI',
    [string]$FoundryEnvironment = 'backlog-ai',

    [string]$SyncResourceGroup = 'JS-AI',
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

function Assert-SyncTokenSigningKeyShape([string] $Key, [string] $Origin) {
    <#
        The same check the Deploy Sync workflow's first step makes, and the same
        rule the service enforces on start: base64, at least 32 bytes decoded.
        Only the shape is ever reported; the value is not.
    #>
    try {
        $bytes = [Convert]::FromBase64String($Key)
    }
    catch {
        throw "SYNC_TOKEN_SIGNING_KEY ($Origin) is not valid base64. Mint one as docs/deployment/sync.md describes."
    }

    if ($bytes.Length -lt 32) {
        throw "SYNC_TOKEN_SIGNING_KEY ($Origin) decodes to $($bytes.Length) bytes; the sync service requires at least 32. Mint one as docs/deployment/sync.md describes."
    }
}

function Get-DeployedSyncApp([string] $ResourceGroup, [string] $Subscription, [string] $Environment) {
    <#
        The container app the previous provision of this azd environment created,
        found by the tags azd itself matches on, so it follows -SyncEnvironment
        rather than a hard-coded name. Nothing deployed yet means $null.
    #>
    # Filtered here rather than in --query: the hyphenated tag keys need quoting
    # inside the JMESPath, and that quoting does not survive the az.cmd shim.
    $apps = az containerapp list `
        --resource-group $ResourceGroup `
        --subscription $Subscription `
        --query '[].{name:name, tags:tags}' `
        --only-show-errors `
        --output json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $apps) { return $null }

    $names = @($apps | ConvertFrom-Json | Where-Object {
            $_.tags -and $_.tags.'azd-env-name' -eq $Environment -and $_.tags.'azd-service-name' -eq 'sync'
        } | ForEach-Object name)
    if ($names.Count -eq 0) { return $null }
    if ($names.Count -gt 1) {
        throw "Resource group '$ResourceGroup' holds $($names.Count) sync container apps tagged for environment '$Environment' ($($names -join ', ')); expected one."
    }
    return $names[0]
}

function Resolve-SyncTokenSigningKey {
    <#
        Puts SYNC_TOKEN_SIGNING_KEY into the process environment, where azd reads
        it when it substitutes infra/sync/main.parameters.json. It runs before azd
        is touched so a bad key fails with its name rather than as an azd prompt
        that would save whatever was typed into .azure/<env>/.env.

        Three sources, in order:

          1. The shell. Set it yourself to rotate, or to deploy a key you chose.
          2. The deployed container app's own secret. A re-run of an existing
             deployment keeps its key, so no device token is invalidated and
             nothing has to be pasted between sessions.
          3. Freshly minted, when nothing is deployed yet. The provision makes it
             the app's secret; copy it from there into the GitHub environment
             secret, or the next workflow run rotates it.

        In every case the value lives in this process only. It is never printed,
        never written to disk, and never passed to `azd env set`.
    #>
    if ($env:SYNC_TOKEN_SIGNING_KEY) {
        Assert-SyncTokenSigningKeyShape -Key $env:SYNC_TOKEN_SIGNING_KEY -Origin 'from the shell'
        Write-Host 'Using SYNC_TOKEN_SIGNING_KEY from the shell.' -ForegroundColor DarkGray
        return
    }

    $app = Get-DeployedSyncApp -ResourceGroup $SyncResourceGroup -Subscription $SubscriptionId -Environment $SyncEnvironment
    if ($app) {
        # `az containerapp secret show` returns the value to a principal that can
        # already write the app - the same trust as redeploying it with any key.
        $key = az containerapp secret show `
            --name $app `
            --resource-group $SyncResourceGroup `
            --subscription $SubscriptionId `
            --secret-name 'sync-token-signing-key' `
            --query value `
            --only-show-errors `
            --output tsv 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $key) {
            throw @"
SYNC_TOKEN_SIGNING_KEY is not set in this shell, and the deployed sync app '$app'
did not return its 'sync-token-signing-key' secret. Either grant yourself write
access to the app, or set the key for this session only:

    `$env:SYNC_TOKEN_SIGNING_KEY = '<base64 key>'
"@
        }

        Assert-SyncTokenSigningKeyShape -Key $key -Origin "read from container app '$app'"
        $env:SYNC_TOKEN_SIGNING_KEY = $key
        Write-Host "Using the signing key already deployed on container app '$app'." -ForegroundColor DarkGray
        return
    }

    $bytes = [byte[]]::new(32)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $env:SYNC_TOKEN_SIGNING_KEY = [Convert]::ToBase64String($bytes)
    Write-Host "No sync app is deployed for environment '$SyncEnvironment' yet; minted a new signing key for this session." -ForegroundColor DarkGray
    if ($Mode -eq 'deploy') {
        Write-Host '  After the deploy, copy it into the GitHub secret SYNC_TOKEN_SIGNING_KEY (environment backlog-sync) with:' -ForegroundColor DarkGray
        Write-Host "    az containerapp secret show --name <sync app> --resource-group $SyncResourceGroup --subscription $SubscriptionId --secret-name sync-token-signing-key --query value --output tsv" -ForegroundColor DarkGray
    }
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
        throw 'SyncResourceGroup is empty. Pass an existing group, or leave the default; see docs/deployment/sync.md.'
    }

    Assert-Tool -Name 'azd' -Install 'winget install Microsoft.Azd'
    Assert-ResourceGroup -Name $SyncResourceGroup -Subscription $SubscriptionId
    # Every mode, the preview included: azd resolves the parameter file before
    # it knows whether it is going to change anything.
    Resolve-SyncTokenSigningKey

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
