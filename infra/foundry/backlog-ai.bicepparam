using './main.bicep'

param accountName = 'backlog-foundry'
param location = 'swedencentral'
param includeBalancedModel = false
param includeSpeechModel = true
param tags = {
  workload: 'Backlog'
  environment: 'backlog-ai'
  owner: 'JSdotNet'
  managedBy: 'bicep'
}
