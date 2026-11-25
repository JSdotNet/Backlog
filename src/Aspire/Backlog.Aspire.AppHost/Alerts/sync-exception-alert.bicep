// The sync tier's exception alert (org ADR 0002: alert rules are modelled in the AppHost and
// deploy with it). SyncDeployment.cs adds this template to the published model with the
// Application Insights component it watches; Aspire has no integration for a log alert.
//
// It reads what .github/workflows/app-insights-exceptions.yml reads: the component's
// `exceptions` table, one row per problemId, Application Insights' own grouping of an
// exception by type and throw site. The alert fires per problemId, so two problems are two
// alerts, and resolves itself once an hour passes without the exception. The workflow is
// what files the GitHub issue; this is what says, within the hour, that one is due.

@description('Name of the Application Insights component the sync service reports to.')
param appInsightsName string

@description('Email address the alert notifies.')
param alertEmail string

@description('Azure region of the alert rule; the action group is global.')
param location string = resourceGroup().location

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-sync-exceptions'
  location: 'global'
  properties: {
    groupShortName: 'syncexc'
    enabled: true
    emailReceivers: [
      {
        name: 'owner'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

resource exceptionAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'sync-exceptions'
  location: location
  properties: {
    displayName: 'Sync service exceptions'
    description: 'An exception the sync service recorded in Application Insights, per problemId. .github/workflows/app-insights-exceptions.yml files one GitHub issue per problem.'
    severity: 2
    enabled: true
    scopes: [
      appInsights.id
    ]
    evaluationFrequency: 'PT1H'
    windowSize: 'PT1H'
    criteria: {
      allOf: [
        {
          query: 'exceptions | summarize occurrences = count() by problemId'
          timeAggregation: 'Total'
          metricMeasureColumn: 'occurrences'
          dimensions: [
            {
              name: 'problemId'
              operator: 'Include'
              values: [
                '*'
              ]
            }
          ]
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
    actions: {
      actionGroups: [
        actionGroup.id
      ]
    }
  }
}

output exceptionAlertName string = exceptionAlert.name
