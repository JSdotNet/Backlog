targetScope = 'resourceGroup'

// The sync identity's grant on the Foundry account (local ADR 0045 §5): Cognitive
// Services OpenAI User, scoped to the account itself. A module of its own because the
// account may live in another resource group than the sync resources, and a role
// assignment can only be declared in the scope it applies to; main.bicep deploys this
// into the Foundry account's group.

@minLength(2)
@description('Name of the existing Foundry (Cognitive Services) account.')
param accountName string

@description('Principal id of the sync service user-assigned identity.')
param principalId string

// Cognitive Services OpenAI User: call the data plane (completions, transcriptions) with
// an Entra token, and nothing else — no key listing, no deployment management.
var cognitiveServicesOpenAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-09-01' existing = {
  name: accountName
}

resource openAiUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, principalId, cognitiveServicesOpenAiUserRoleId)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAiUserRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

// The account's endpoint, which the sync container's settings point the relay at.
output endpoint string = foundryAccount.properties.endpoint
