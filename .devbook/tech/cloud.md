# Cloud Stack

```meta
status: candidate
related: [".devbook/tech/technology-graph.md", ".devbook/arc42/07-deployment-view.md#cloud-deployment-azure"]
```

> The Azure side of the system. It holds a deliberately thin sync service, which
> does only sync coordination, webhook forwarding, push, and the machine
> registry, and the managed AI service the desktop calls.
>
> The app model that runs this service locally is
> [.NET Aspire](shared.md#net-aspire). It composes every channel, not just this
> one, so it lives in `shared.md`.

## ASP.NET Core Minimal APIs

```meta
status: adopted
type: framework
depends-on: [".devbook/tech/shared.md#aspnet-core", ".devbook/tech/shared.md#c-language"]
related: [".devbook/arc42/04-solution-strategy.md#thin-cloud-rich-desktop", ".devbook/arc42/09-architecture-decisions.md"]
alternatives: ["Azure Functions", "Controller-based ASP.NET Core"]
```

The HTTP surface of the cloud service.

- **Used for** — `Backlog.Modules.Sync.Api`. It serves the `/api/sync/devices/*`
  registration, pairing-code, pairing, and token endpoints, and the
  bearer-protected `/api/sync/inbox` capture, list, and acknowledge endpoints.
  Webhook intake, push dispatch, and the remote PC registry are to follow.
- **Why** — the organization's governed .NET stack for services; minimal APIs fit
  a handful of endpoints without ceremony. The API head sits over the
  `Backlog.Modules.Sync` module, with its stores in `Backlog.Infrastructure.Cosmos`
  and `Backlog.Infrastructure.BlobStorage`.

## Microsoft.AspNetCore.Authentication.JwtBearer

```meta
status: adopted
type: package
version: "10.0.12"
depends-on: [".devbook/tech/shared.md#aspnet-core"]
related: [".devbook/arc42/adr/guidelines/0012-authentication-external-identity-providers.md", ".devbook/arc42/08-crosscutting-concepts.md#task-sync"]
```

Validates the device token on every sync request.

- **Used for** — `Backlog.Modules.Sync.Api`'s authentication pipeline. It checks
  issuer, audience, lifetime, signature, and a pinned signing algorithm on every
  request, with no server-side session, ahead of the owner-scoping
  authorization filter.
- **Why** — inherited ADR 0012 requires stateless validation of every field on
  a device-session JWT, and this is the governed package that does it.

## Microsoft.IdentityModel.JsonWebTokens

```meta
status: adopted
type: package
version: "8.19.2"
depends-on: [".devbook/tech/shared.md#net-runtime"]
related: [".devbook/arc42/08-crosscutting-concepts.md#task-sync"]
```

The token library the sync service issues its own device tokens with.

- **Used for** — `JwtDeviceTokenIssuer` minting the 30-minute HS256 device
  token (`sub`, `owner_id`, `jti`, `iat`, `nbf`, `exp`) that a device presents
  on every sync call.
- **Why** — it is pinned as a direct reference, at the version
  `Microsoft.AspNetCore.Authentication.JwtBearer` already resolves transitively.
  The code that signs a token and the code that validates it therefore share
  one build of the library, rather than two that could drift apart.

## Microsoft.AspNetCore.OpenApi

```meta
status: adopted
type: package
version: "10.0.11"
depends-on: [".devbook/tech/shared.md#aspnet-core"]
```

Generates the OpenAPI document for the sync API.

- **Used for** — `AddOpenApi()` and an anonymous `MapOpenApi()` in
  `Backlog.Modules.Sync.Api`.
- **Why** — the first-party generator for minimal APIs, with no Swashbuckle
  dependency.

## Azure.Identity

```meta
status: adopted
type: package
version: "1.21.0"
depends-on: [".devbook/tech/shared.md#net-runtime"]
related: [".devbook/tech/cloud.md#azure-ai-foundry"]
```

Microsoft Entra token credentials.

- **Used for** — `DeveloperSignInTokenSource` in
  `Backlog.Infrastructure.AzureFoundry`, behind the `IAzureManagementTokenSource`
  port. It reads Foundry cost data from the management plane through an explicit
  `ChainedTokenCredential` of Azure CLI, Azure Developer CLI, and Visual Studio,
  not through `DefaultAzureCredential` and its probing chain.
- **Why** — the governed way to obtain an Entra token. Chat completions still
  use the API key, so this is the cost reader's only credential.

## Azure Container Apps

```meta
status: trial
type: platform
depends-on: [".devbook/tech/cloud.md#aspnet-core-minimal-apis"]
related: [".devbook/arc42/07-deployment-view.md#cloud-deployment-azure", ".devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md"]
alternatives: ["Azure App Service"]
```

The compute host for the cloud service.

- **Used for** — running a single-region, single-instance deployment sized for
  personal use.
- **Why** — scale-to-zero means a personal tool costs nothing while nobody is
  syncing. The Aspire AppHost already models `sync` as a container resource, so
  local and deployed topology keep the same shape. Local ADR 0005 settled this
  against App Service, which the deployment view had left as an equal option.
- **How** — `infra/sync/main.bicep` declares a consumption-profile managed
  environment and one container app (`minReplicas: 0`). The app pulls from a
  container registry with the same user-assigned managed identity it reaches
  Cosmos with.
- **Status** — `trial` rather than adopted. The environment and the app run in
  `JS-AI`, put there by hand. The `Deploy Sync` workflow has never completed,
  though, and no second device has synced against the service in real use.

## Azure AI Foundry

```meta
status: adopted
type: service
related: [".devbook/tech/tooling.md#bicep", ".devbook/tech/tooling.md#azure-cli", ".devbook/arc42/07-deployment-view.md#cloud-deployment-azure"]
alternatives: ["Anthropic API directly", "local models"]
```

The managed model endpoint the product's own AI features call.

- **Used for** — chat completions from `Backlog.Infrastructure.AzureFoundry`,
  which keeps endpoint settings, API-key storage, and the HTTP details out of the
  UI and the modules.
- **Why** — one governed Azure resource with per-model deployments and a content
  filter policy, rather than a vendor key per developer machine.
- **How** — `infra/foundry/main.bicep` declares a `Microsoft.CognitiveServices`
  account of kind `AIServices` and its deployments, each behind a parameter.
  Three models are required. A balanced model, speech transcription, and a
  `text-embedding-3-small` deployment behind `includeEmbeddingModel` are
  optional. The devbook database's semantic tier would call the embedding
  deployment. Nothing calls it yet, and the database is correct without it
  (`.devbook/arc42/adr/0004-knowledge-index-is-a-generated-local-database.md`).
  `.github/workflows/deploy-foundry.yml` builds, validates, what-ifs, and then
  deploys it from a self-hosted runner.
- **Local runs** — development runs never touch it. The Aspire AppHost starts
  `Backlog.AzureFoundry.TestService` instead and points the desktop harness at
  it through `BACKLOG_AZURE_FOUNDRY_LOCAL_ENDPOINT`.

## Azure Cosmos DB

```meta
status: trial
type: service
related: [".devbook/arc42/07-deployment-view.md#cloud-deployment-azure", ".devbook/arc42/08-crosscutting-concepts.md#storage-and-sync", ".devbook/arc42/08-crosscutting-concepts.md#task-sync", ".devbook/arc42/08-crosscutting-concepts.md#session-record-sync", ".devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md"]
alternatives: ["Azure PostgreSQL", "Azure Table Storage"]
```

The cloud data store for cross-device coordination state: serverless, one
account, one database, and the containers below.

| Container | Holds | Partition key | Retention |
|---|---|---|---|
| `tasks` | The Task aggregate. The phone's captures land here as task documents rather than as a shape of their own. | `/ownerId` | 180 days per tombstone, stamped by the service. A live task never expires. |
| `sessions` | Machine-stamped, append-only session records | `/ownerId` | 12 months for the whole record, as the container TTL |
| `annotations` | The person's [remarks](../domain/devbook/domain.md#remark) on Devbook chapters (local ADR 0011) | `/ownerId` | 180 days per tombstone, stamped by the service |
| `devices` | The device registry | `/id` | None: a registration lasts until the device is forgotten |
| `pairingCodes` | One document per live pairing code, keyed by the code's hash | `/id` | The code's ten-minute window plus a grace, stamped per document |

- **Used for** — the replica the devices reconcile through, and the device
  registry beside it. The replicas are separate containers because each wants
  its own change feed, indexing policy, and retention. Serverless levies no
  per-container charge to trade against that.
- **Why** — local ADR 0005 settled it. **TTL is the retention mechanism**:
  nothing reaps, so no scheduled job can fail silently. Serverless bills per
  request with no floor, which suits one person's sync traffic, where an
  always-on relational tier charges for an idle database. The aggregate is
  already a document, so no second relational schema needs versioning. The
  change feed *is* the sync protocol, answered natively from a continuation
  token.
- **How** — `infra/sync/main.bicep` declares the account with
  `disableLocalAuth: true`, so it accepts no account key at all. The service
  reaches it as a managed identity holding the built-in data-plane contributor
  role. The 180 days on `tasks` is a provisioned value the service stamps on each
  tombstone. A container has one TTL, and a container-level 180 days would
  expire live tasks too. Local runs use the Cosmos vNext emulator the Aspire
  AppHost starts, so building or testing the sync path needs no Azure account.
- **Status** — `trial` and no higher. The account is provisioned in `JS-AI` and
  the hand-deployed sync service reaches it. The `Deploy Sync` workflow has
  never completed, though, and no real multi-device use has validated it.

## Azure Blob Storage

```meta
status: trial
type: service
depends-on: [".devbook/tech/cloud.md#aspnet-core-minimal-apis"]
related: [".devbook/arc42/adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md", ".devbook/arc42/07-deployment-view.md#cloud-deployment-azure", ".devbook/tech/cloud.md#azure-cosmos-db"]
```

The attachment store beside the Cosmos replica (local ADR 0014).

- **Used for** — the files on a phone capture, in a private `attachments`
  container keyed `{ownerId}/{attachmentId}` and reached only through the sync
  service. `Backlog.Infrastructure.BlobStorage` uses `Azure.Storage.Blobs`
  through the `Aspire.Azure.Storage.Blobs` client integration. Locally the
  AppHost runs Azurite in its place.
- **Why** — a capture document carries metadata only, and a device never holds
  a storage credential.
- **Status** — `trial` for the same reason as the rest of the Azure tier. The
  deployment view records the storage account as provisioned and running, but
  real use has not yet validated it.

## Azure Key Vault

```meta
status: trial
type: service
related: [".devbook/arc42/07-deployment-view.md#cloud-deployment-azure", ".devbook/arc42/adr/guidelines/0013-authorization-zero-trust.md"]
```

The secret store for the cloud service.

- **Used for** — GitHub webhook secrets and OAuth tokens.
- **Why** — credentials are never held in app configuration, and user-source
  credentials stay on the desktop entirely.
- **How** — `infra/sync/main.bicep` provisions it with RBAC authorization and
  grants the sync service's managed identity **Key Vault Secrets User**. It is
  provisioned empty. The sync tier holds no Cosmos key and no registry
  credential to put in it, so the vault stands ready for the webhook and OAuth
  secrets.

## Azure Monitor

```meta
status: trial
type: service
related: [".devbook/arc42/07-deployment-view.md#provisioning-and-delivery", ".devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md"]
alternatives: ["Aspire dashboard only"]
```

The telemetry sink for the deployed cloud service: a Log Analytics workspace and
a workspace-based Application Insights resource over it.

- **Used for** — requests, dependencies, traces, exceptions and metrics from the
  sync container app, plus the container app environment's console and system
  logs. OpenTelemetry already flows through `AddServiceDefaults()`, so this is
  wiring rather than design. Locally the same signals go to the Aspire dashboard
  instead.
- **Why** — the deployed service scales to zero and has no dashboard of its own,
  so without a sink its telemetry ends when the replica does.
- **How** — provisioned by `infra/sync/main.bicep`. The container app environment
  logs through the `azure-monitor` destination and a diagnostic setting rather
  than the `log-analytics` destination. The latter wants the workspace's shared
  key inline in configuration, and this deployment issues no keys.
- **Constraint** — application observability **only**. The service writes no
  task or session content here, and nothing that identifies the owner. A
  telemetry pipeline samples and drops under load, so anything a dashboard
  answered from it would inherit that sampling and be wrong exactly when the
  system is busiest. This is a rule about what the service logs; no template can
  enforce it.

## Firebase Cloud Messaging

```meta
status: candidate
type: service
related: [".devbook/arc42/07-deployment-view.md#cloud-deployment-azure", ".devbook/tech/mobile.md#android"]
alternatives: ["Azure Notification Hubs"]
```

The push-notification transport to the phone client.

- **Used for** — delivering sync and triage notifications to Android.
- **Why** — the native push channel for Android, and it needs no additional
  Azure resource.
