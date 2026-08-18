targetScope = 'resourceGroup'

@description('Name of the Azure AI Services / Foundry resource.')
param accountName string

@description('Azure region for the Foundry resource. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('SKU for the Azure AI Services account.')
param accountSkuName string = 'S0'

@allowed([
  'GlobalStandard'
  'DataZoneStandard'
  'Standard'
  'GlobalProvisioned'
  'Provisioned'
])
@description('Default SKU used for model deployments that do not specify their own.')
param deploymentSkuName string = 'GlobalStandard'

@minValue(1)
@description('Default capacity used for model deployments that do not specify their own.')
param deploymentCapacity int = 1

@description('Deploy the optional balanced alternative model.')
param includeBalancedModel bool = false

@description('Deploy the optional speech-to-text model.')
param includeSpeechModel bool = false

@description('Responsible AI content filter policy name for model deployments.')
param contentFilterPolicyName string = 'Microsoft.DefaultV2'

@description('Tags applied to the Foundry resource and deployments.')
param tags object = {
  workload: 'Backlog'
  environment: 'backlog-ai'
  managedBy: 'bicep'
}

// Each entry may pin its own deployment SKU and capacity. An empty skuName falls back to
// deploymentSkuName, and a zero capacity falls back to deploymentCapacity. Speech models
// need this because their quota and supported SKUs differ from the chat models.
var requiredModelDeployments = [
  {
    name: 'gpt-5-4'
    modelName: 'gpt-5.4'
    modelFormat: 'OpenAI'
    publisher: 'OpenAI'
    modelVersion: ''
    role: 'default-coding-architecture'
    promptTokenBudget: '922000'
    outputTokenBudget: '128000'
    skuName: ''
    capacity: 0
  }
  {
    name: 'gpt-5-5'
    modelName: 'gpt-5.5'
    modelFormat: 'OpenAI'
    publisher: 'OpenAI'
    modelVersion: ''
    role: 'premium-fallback'
    promptTokenBudget: '922000'
    outputTokenBudget: '128000'
    skuName: ''
    capacity: 0
  }
  {
    name: 'gpt-5-6-luna'
    modelName: 'gpt-5.6-luna'
    modelFormat: 'OpenAI'
    publisher: 'OpenAI'
    modelVersion: ''
    role: 'fast-routine'
    promptTokenBudget: '922000'
    outputTokenBudget: '128000'
    skuName: ''
    capacity: 0
  }
]

var optionalModelDeployments = includeBalancedModel ? [
  {
    name: 'gpt-5-6-sol'
    modelName: 'gpt-5.6-sol'
    modelFormat: 'OpenAI'
    publisher: 'OpenAI'
    modelVersion: ''
    role: 'balanced-alternative'
    promptTokenBudget: '922000'
    outputTokenBudget: '128000'
    skuName: ''
    capacity: 0
  }
] : []

var speechModelDeployments = includeSpeechModel ? [
  {
    name: 'gpt-4o-transcribe'
    modelName: 'gpt-4o-transcribe'
    modelFormat: 'OpenAI'
    publisher: 'OpenAI'
    modelVersion: ''
    role: 'speech-to-text'
    promptTokenBudget: ''
    outputTokenBudget: ''
    skuName: 'GlobalStandard'
    capacity: 0
  }
] : []

var selectedModelDeployments = concat(requiredModelDeployments, optionalModelDeployments, speechModelDeployments)

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-09-01' = {
  name: accountName
  location: location
  kind: 'AIServices'
  sku: {
    name: accountSkuName
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: accountName
    publicNetworkAccess: 'Enabled'
  }
  tags: tags
}

resource modelDeployments 'Microsoft.CognitiveServices/accounts/deployments@2025-09-01' = [for deployment in selectedModelDeployments: {
  name: deployment.name
  parent: foundryAccount
  sku: {
    name: empty(deployment.skuName) ? deploymentSkuName : deployment.skuName
    capacity: deployment.capacity == 0 ? deploymentCapacity : deployment.capacity
  }
  properties: {
    model: empty(deployment.modelVersion) ? {
      format: deployment.modelFormat
      name: deployment.modelName
      publisher: deployment.publisher
    } : {
      format: deployment.modelFormat
      name: deployment.modelName
      publisher: deployment.publisher
      version: deployment.modelVersion
    }
    raiPolicyName: contentFilterPolicyName
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
  }
  tags: union(tags, {
    'backlog-model-role': deployment.role
  }, empty(deployment.promptTokenBudget) ? {} : {
    'backlog-prompt-token-budget': deployment.promptTokenBudget
    'backlog-output-token-budget': deployment.outputTokenBudget
  })
}]

output accountName string = foundryAccount.name
output accountResourceId string = foundryAccount.id
output deploymentNames array = [for deployment in selectedModelDeployments: deployment.name]
