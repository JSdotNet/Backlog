targetScope = 'resourceGroup'

// Sync infrastructure for local ADR 0005 — an Azure-hosted task replica carries
// multi-device sync. Provisioned and deployed with `azd`; see docs/deployment/sync.md.
//
// The resource group itself is NOT created here. It is created by hand once, the
// same way infra/foundry/ expects one to exist, so a mis-typed environment name
// cannot spawn a second group nobody notices paying for.

@minLength(1)
@maxLength(24)
@description('Environment name. azd supplies this from AZURE_ENV_NAME; it seeds every resource name and the azd-env-name tag.')
param environmentName string

@minLength(1)
@description('Azure region for every resource. azd supplies this from AZURE_LOCATION.')
param location string = resourceGroup().location

// West Europe and North Europe both refuse new Cosmos accounts for this subscription
// ("high demand in <region>", ServiceUnavailable, zone redundancy off or not; the fix each
// offers is a region-access request), so the account lives next to the Foundry account
// instead. Cross-region latency from the container app is a few milliseconds on a store
// that is written by a sync round-trip, not a request path.
@minLength(1)
@description('Azure region for the Cosmos account. azd supplies this from AZURE_COSMOS_LOCATION; defaults to swedencentral.')
param cosmosLocation string = 'swedencentral'

@description('Container image for the sync service. Left empty on the first provision so a public placeholder runs; azd sets it to the built image on every deploy after that.')
param containerImage string = ''

@description('Cosmos DB database holding both replica containers.')
param databaseName string = 'backlog'

// ADR 0005 fixes this at 180 days. It is provisioned rather than written into
// service code so the retention stays an infrastructure setting, per that
// record's "retention is Cosmos TTL, not code".
@description('Seconds a task tombstone survives in the tasks container. ADR 0005 fixes this at 180 days.')
param taskTombstoneTtlSeconds int = 15552000

@description('Seconds a session record survives in the sessions container. ADR 0005 fixes this at 12 months.')
param sessionRetentionSeconds int = 31536000

@description('Days Log Analytics keeps ingested telemetry. Application observability only — no domain data reaches this workspace.')
param logRetentionInDays int = 30

// An emergency stop, not a budget knob. In September 2026 a sibling project
// (spec-manager) paid ~$688 for one month of Log Analytics after stale container
// revisions logged a stack trace every five seconds with nothing capping the
// volume. Backlog cannot reach that state the same way — the sync app runs in
// Single revision mode and scales to zero — but the workspace should still refuse
// to bill an incident without limit. Once the quota is hit Azure drops everything
// for the rest of the day, exceptions included; that is intended: on such a day
// the volume is the incident, and the first lines already say what went wrong.
// 1 GB is far above a normal day for a scale-to-zero personal tool and bounds a
// repeat of that incident at roughly $3/day.
@minValue(1)
@description('Daily ingestion cap of the Log Analytics workspace, in GB. Emergency stop against runaway logging; Azure discards further ingestion for the rest of the day once it is reached.')
param logDailyQuotaGb int = 1

// The HMAC key the service signs device tokens with (Modules:Sync:Tokens:SigningKey).
// Outside Development the service validates it on start and refuses to come up
// without one, so it is a required parameter with no default: a provision that
// forgets it fails here, not as a container that restarts forever. It reaches the
// container as a container app secret, never as a plain environment variable, and
// ARM redacts it from deployment history because the parameter is @secure().
// Supplied from the GitHub environment secret SYNC_TOKEN_SIGNING_KEY (or the same
// variable in a local shell); see docs/deployment/sync.md for minting and rotation.
// 44 characters is 32 bytes of base64, the minimum the service accepts.
@secure()
@minLength(44)
@description('HMAC key the sync service signs device tokens with: base64, at least 32 bytes once decoded. azd supplies this from SYNC_TOKEN_SIGNING_KEY.')
param tokenSigningKey string

@description('Tags applied to every resource.')
param tags object = {
  workload: 'Backlog'
  component: 'sync'
  managedBy: 'azd'
}

// azd matches a deployed resource to an environment through this tag, so it is
// merged into every resource rather than left to the caller's tags object.
var allTags = union(tags, {
  'azd-env-name': environmentName
})

// One token per (resource group, environment) pair keeps the globally unique
// names — Cosmos account, Key Vault, container registry — stable across re-runs.
var resourceToken = toLower(uniqueString(resourceGroup().id, environmentName))

var cosmosAccountName = 'cosmos-${resourceToken}'
var keyVaultName = 'kv-${resourceToken}'
var registryName = 'acr${resourceToken}'
var logAnalyticsName = 'log-${resourceToken}'
var appInsightsName = 'appi-${resourceToken}'
var identityName = 'id-sync-${resourceToken}'
var containerAppsEnvironmentName = 'cae-${resourceToken}'
var containerAppName = 'ca-sync-${resourceToken}'

// First provision has no built image yet. A public placeholder lets the container
// app and its ingress come up so the rest of the template can be validated; azd
// replaces it on the first `azd deploy`.
var placeholderImage = 'mcr.microsoft.com/k8se/quickstart:latest'
var effectiveImage = empty(containerImage) ? placeholderImage : containerImage

// Built-in role definition ids, by GUID because the names are not addressable in Bicep.
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
// Cosmos DB Built-in Data Contributor. A *data-plane* role: it grants no control-plane
// rights over the account, which is the whole point of reaching Cosmos this way.
var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'

// ---------------------------------------------------------------------------
// Identity
// ---------------------------------------------------------------------------

// User-assigned rather than system-assigned. A system-assigned identity does not
// exist until its container app does, so the AcrPull assignment the app needs in
// order to pull its own image cannot be made before the app is created. Splitting
// the identity out breaks that cycle and lets one deployment grant every role.
resource syncIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
  tags: allTags
}

// ---------------------------------------------------------------------------
// Observability
//
// Application observability only. No task content, no session content and no
// other domain data is written here: a telemetry pipeline samples and drops
// under load, and nothing a dashboard answers from may inherit that.
// ---------------------------------------------------------------------------

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: allTags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: logRetentionInDays
    workspaceCapping: {
      dailyQuotaGb: logDailyQuotaGb
    }
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  tags: allTags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// ---------------------------------------------------------------------------
// Key Vault
// ---------------------------------------------------------------------------

// Provisioned because ADR 0005 lists it, and empty: nothing in the service reads
// Key Vault today. The device-token signing key is a container app secret (see
// tokenSigningKey above) rather than a vault secret, because the app scales to
// zero and a vault round-trip on every cold start buys nothing while one secret
// is all there is. The vault and the role below are reserved for the day that
// changes — vault-managed rotation, or the webhook secrets ADR 0005 also names —
// which would add Azure.Extensions.AspNetCore.Configuration.Secrets to the
// service and a secret named Modules--Sync--Tokens--SigningKey here. Until then
// the service is handed no vault endpoint, so nothing looks configured that is not.
//
// RBAC rather than access policies, so the sync identity's access is granted the
// same way every other permission in this template is. Purge protection is left
// off deliberately: this is a personal-scale deployment that has to be tearable
// down, and purge protection cannot be switched off once it is on.
resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: keyVaultName
  location: location
  tags: allTags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

resource keyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, syncIdentity.id, keyVaultSecretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: syncIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Cosmos DB — the replica store
// ---------------------------------------------------------------------------

// Serverless, and `disableLocalAuth` is what makes "no account keys" a property of
// the account rather than a convention the service is trusted to keep: with local
// auth off, Cosmos will not accept a key even if one leaked. Every caller must
// arrive as an Entra principal holding a data-plane role.
resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: cosmosAccountName
  location: cosmosLocation
  kind: 'GlobalDocumentDB'
  tags: allTags
  properties: {
    databaseAccountOfferType: 'Standard'
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: cosmosLocation
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    disableLocalAuth: true
    disableKeyBasedMetadataWriteAccess: true
    minimalTlsVersion: 'Tls12'
    publicNetworkAccess: 'Enabled'
  }
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: cosmosAccount
  name: databaseName
  properties: {
    resource: {
      id: databaseName
    }
  }
}

// Two containers, not one — ADR 0005 "Two containers, not one". Each carries its
// own change feed, its own indexing policy and its own TTL, and under serverless
// the second container costs nothing.

// `tasks` — the default indexing policy, because a task is read by id and by owner
// and nothing here knows which other field a later query will want.
//
// defaultTtl is -1: TTL is *enabled* on the container but no document expires on
// its own. A container has one TTL, and a container-level 180 days would expire
// live tasks as readily as tombstones — which ADR 0005 explicitly does not want
// ("a live task document carries no expiry"). So the 180 days is stamped per
// document, on the write that sets `deleted_at`, from the value provisioned as
// Sync__Cosmos__TaskTombstoneTtlSeconds below. Cosmos still does the deleting;
// no reaper runs and no scheduled job can fail silently.
resource tasksContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDatabase
  name: 'tasks'
  properties: {
    resource: {
      id: 'tasks'
      partitionKey: {
        paths: [
          '/ownerId'
        ]
        kind: 'Hash'
        version: 2
      }
      defaultTtl: -1
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
        excludedPaths: [
          {
            path: '/"_etag"/?'
          }
        ]
      }
    }
  }
}

// `sessions` — a lean custom index. A session record is read by owner and recency
// and by nothing else, so only the owner, the machine, the repository alias and
// the two timestamps are indexed; everything else is excluded. The whole record
// expires at 12 months, so here the TTL genuinely is a container setting.
resource sessionsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDatabase
  name: 'sessions'
  properties: {
    resource: {
      id: 'sessions'
      partitionKey: {
        paths: [
          '/ownerId'
        ]
        kind: 'Hash'
        version: 2
      }
      defaultTtl: sessionRetentionSeconds
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/ownerId/?'
          }
          {
            path: '/machineId/?'
          }
          {
            path: '/repositoryAlias/?'
          }
          {
            path: '/startedAt/?'
          }
          {
            path: '/lastActivityAt/?'
          }
        ]
        excludedPaths: [
          {
            path: '/*'
          }
          {
            path: '/"_etag"/?'
          }
        ]
      }
    }
  }
}

// The data-plane role the sync service reaches Cosmos with. Scoped to the account,
// which is as narrow as it goes: ADR 0005 Identity records that this grants sight
// of every partition, and that keeping a device inside its own partition is a check
// in the service, not a property of this assignment.
resource cosmosDataContributor 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, syncIdentity.id, cosmosDataContributorRoleId)
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    principalId: syncIdentity.properties.principalId
    scope: cosmosAccount.id
  }
}

// ---------------------------------------------------------------------------
// Container registry
//
// Not named in ADR 0005's resource list, but `azd` deploying a container app has
// to push the built image somewhere. Admin user is off, so the registry issues no
// credentials either; the container app pulls with the same managed identity.
// ---------------------------------------------------------------------------

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: registryName
  location: location
  tags: allTags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
    anonymousPullEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerRegistry
  name: guid(containerRegistry.id, syncIdentity.id, acrPullRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: syncIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Container Apps
// ---------------------------------------------------------------------------

// `azure-monitor` rather than `log-analytics` as the log destination: the
// log-analytics destination wants the workspace's shared key inline in the
// environment's configuration, and this deployment issues no keys. The diagnostic
// setting below routes the same console and system logs to the same workspace
// through the platform instead.
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppsEnvironmentName
  location: location
  tags: allTags
  properties: {
    appLogsConfiguration: {
      destination: 'azure-monitor'
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: false
  }
}

resource containerAppsEnvironmentDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: containerAppsEnvironment
  name: 'send-to-log-analytics'
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      {
        category: 'ContainerAppConsoleLogs'
        enabled: true
      }
      {
        category: 'ContainerAppSystemLogs'
        enabled: true
      }
    ]
  }
}

// Consumption, scaling to zero. A personal tool costs nothing while nobody syncs.
resource syncApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  // azd matches this container app to the `sync` service in azure.yaml by this tag.
  tags: union(allTags, {
    'azd-service-name': 'sync'
  })
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${syncIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: syncIdentity.id
        }
      ]
      // Two secrets, and they are not alike. The Application Insights connection
      // string is the one connection string this deployment still hands the
      // service: a telemetry ingestion endpoint, not a data credential — it opens
      // nothing, reads nothing, and reaches no replica — held as a secret rather
      // than a plain environment variable all the same. The token signing key
      // is a real secret: whoever holds it can mint a device token for any owner.
      secrets: [
        {
          name: 'applicationinsights-connection-string'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'sync-token-signing-key'
          value: tokenSigningKey
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'sync'
          image: effectiveImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            // Tells DefaultAzureCredential which user-assigned identity to present.
            {
              name: 'AZURE_CLIENT_ID'
              value: syncIdentity.properties.clientId
            }
            {
              name: 'ASPNETCORE_HTTP_PORTS'
              value: '8080'
            }
            // The account endpoint, not a connection string: the credential comes
            // from the managed identity above.
            {
              name: 'Sync__Cosmos__AccountEndpoint'
              value: cosmosAccount.properties.documentEndpoint
            }
            {
              name: 'Sync__Cosmos__DatabaseName'
              value: databaseName
            }
            {
              name: 'Sync__Cosmos__TasksContainerName'
              value: tasksContainer.name
            }
            {
              name: 'Sync__Cosmos__SessionsContainerName'
              value: sessionsContainer.name
            }
            {
              name: 'Sync__Cosmos__TaskTombstoneTtlSeconds'
              value: string(taskTombstoneTtlSeconds)
            }
            // Double underscore is how .NET reads a ':' configuration path from
            // an environment variable, so this lands on SyncTokenOptions as
            // Modules:Sync:Tokens:SigningKey. No Key Vault endpoint is passed:
            // nothing reads one (see the Key Vault section).
            {
              name: 'Modules__Sync__Tokens__SigningKey'
              secretRef: 'sync-token-signing-key'
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              secretRef: 'applicationinsights-connection-string'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
  dependsOn: [
    acrPull
    cosmosDataContributor
    keyVaultSecretsUser
  ]
}

// ---------------------------------------------------------------------------
// Outputs
//
// The AZURE_* names are azd's conventions; it writes them into the environment
// and reads them back on deploy.
// ---------------------------------------------------------------------------

output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.properties.loginServer
output AZURE_CONTAINER_REGISTRY_NAME string = containerRegistry.name
output AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = containerAppsEnvironment.id
output AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = containerAppsEnvironment.name
output AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = containerAppsEnvironment.properties.defaultDomain
output AZURE_KEY_VAULT_ENDPOINT string = keyVault.properties.vaultUri
output AZURE_KEY_VAULT_NAME string = keyVault.name

output SYNC_SERVICE_NAME string = syncApp.name
output SYNC_SERVICE_URI string = 'https://${syncApp.properties.configuration.ingress.fqdn}'
output SYNC_IDENTITY_CLIENT_ID string = syncIdentity.properties.clientId

output COSMOS_ACCOUNT_NAME string = cosmosAccount.name
output COSMOS_ACCOUNT_ENDPOINT string = cosmosAccount.properties.documentEndpoint
output COSMOS_DATABASE_NAME string = cosmosDatabase.name

output APPLICATIONINSIGHTS_NAME string = appInsights.name
output LOG_ANALYTICS_WORKSPACE_NAME string = logAnalytics.name
