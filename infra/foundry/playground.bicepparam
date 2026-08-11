using './main.bicep'

param accountName = 'backlog-foundry'
param includeBalancedModel = false
param tags = {
  workload: 'Backlog'
  environment: 'playground'
  owner: 'JSdotNet'
  managedBy: 'bicep'
}
