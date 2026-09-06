# Cloud Stack

```meta
status: candidate
related: [".tech/technology-graph.md", ".arc42/07-deployment-view.md#cloud-deployment-azure"]
```

> The Azure side of the system: a deliberately thin sync service that only does
> sync coordination, webhook forwarding, push, and the machine registry, plus
> the managed AI service the desktop calls. The sync layer stays small on
> purpose.
>
> The app model that runs this service locally is `.tech/shared.md#net-aspire`;
> it composes every channel, not just this one, so it lives in `shared.md`.

## ASP.NET Core Minimal APIs

```meta
status: adopted
type: framework
depends-on: [".tech/shared.md#aspnet-core", ".tech/shared.md#c-language"]
related: [".arc42/04-solution-strategy.md#thin-cloud-rich-desktop", ".arc42/09-architecture-decisions.md"]
alternatives: ["Azure Functions", "Controller-based ASP.NET Core"]
```

The HTTP surface of the cloud service.

- **Used for** — `Backlog.Modules.Sync.Api`: the `/api/sync/inbox` capture,
  list, and acknowledge endpoints today, with webhook intake, push dispatch, and
  the remote PC registry to follow.
- **Why** — the organization's governed .NET stack for services; minimal APIs fit
  a handful of endpoints without ceremony. The whole service is one `Program.cs`
  plus a store, which is the point.

## Azure Container Apps

```meta
status: candidate
type: platform
depends-on: [".tech/cloud.md#aspnet-core-minimal-apis"]
related: [".arc42/07-deployment-view.md#cloud-deployment-azure", ".arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md"]
alternatives: ["Azure App Service"]
```

The compute host for the cloud service.

- **Used for** — running a single-region, single-instance deployment sized for
  personal use.
- **Why** — scale-to-zero means a personal tool costs nothing while nobody is
  syncing, and the Aspire AppHost already models `sync` as a container resource,
  so local and deployed topology stay the same shape. Local ADR 0005 settled this
  against App Service, which the deployment view had left as an equal option.
- **How** — `infra/sync/main.bicep` declares a consumption-profile managed
  environment and one container app (`minReplicas: 0`), pulling from a container
  registry with the same user-assigned managed identity it reaches Cosmos with.
  Candidate rather than adopted because nothing is deployed yet: the template and
  its `Deploy Sync` workflow exist, the Azure resources do not.

## Azure AI Foundry

```meta
status: adopted
type: service
related: [".tech/tooling.md#bicep", ".tech/tooling.md#azure-cli", ".arc42/07-deployment-view.md#cloud-deployment-azure"]
alternatives: ["Anthropic API directly", "local models"]
```

The managed model endpoint the product's own AI features call.

- **Used for** — chat completions from `Backlog.Infrastructure.AzureFoundry`,
  which keeps endpoint settings, API-key storage, and the HTTP details out of the
  UI and the modules.
- **Why** — one governed Azure resource with per-model deployments and a content
  filter policy, rather than a vendor key per developer machine.
- **How** — `infra/foundry/main.bicep` declares a `Microsoft.CognitiveServices`
  account of kind `AIServices` plus its deployments: three required models, an
  optional balanced model, and optional speech transcription, each behind a
  parameter. `.github/workflows/deploy-foundry.yml` builds, validates, what-ifs,
  and then deploys it from a self-hosted runner. Development runs never touch it:
  the Aspire AppHost starts `Backlog.AzureFoundry.TestService` instead and points
  the desktop harness at it through
  `BACKLOG_AZURE_FOUNDRY_LOCAL_ENDPOINT`.

## Azure Cosmos DB

```meta
status: candidate
type: service
related: [".arc42/07-deployment-view.md#cloud-deployment-azure", ".arc42/08-crosscutting-concepts.md#storage-and-sync", ".arc42/08-crosscutting-concepts.md#task-sync", ".arc42/08-crosscutting-concepts.md#session-record-sync", ".arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md"]
alternatives: ["Azure PostgreSQL", "Azure Table Storage"]
```

The cloud data store for cross-device coordination state: serverless, one account,
one database, **two containers**.

- **Used for** — the replica the devices reconcile through. `tasks` holds the
  Task aggregate, and the phone's captures land in it as task documents rather
  than as a shape of their own; `sessions` holds machine-stamped, append-only
  session records. Both are partitioned on `/ownerId`. Buffered webhook events and
  the machine registry sit on the same account with their own short expiries
  (7 days / 24 hours). Two containers and not one because each wants its own
  change feed, its own indexing policy, and its own retention — and serverless
  levies no per-container charge to trade against.
- **Why** — settled by local ADR 0005 rather than left open. **TTL is the
  retention mechanism**, and the numbers are concrete: 180 days on `tasks`,
  expiring a tombstone and never a live task, and 12 months on `sessions`,
  expiring the whole record. Nothing reaps, so there is no scheduled job to fail
  silently. Beyond TTL: serverless bills per request with no floor, which suits
  one person's sync traffic where an always-on relational tier charges for an idle
  database; the aggregate is already a document, so no second relational schema
  needs versioning; and the change feed *is* the sync protocol, answered natively
  from a continuation token.
- **How** — `infra/sync/main.bicep` declares the account with
  `disableLocalAuth: true`, so it will not accept an account key at all; the
  service reaches it as a managed identity holding the built-in data-plane
  contributor role. The `sessions` retention above is a container TTL; the 180
  days on `tasks` is a provisioned value the service stamps on a tombstone,
  because one container has one TTL and a container-level 180 days would expire
  live tasks too. Local runs use the Cosmos preview emulator started by the
  Aspire AppHost, so no Azure account is needed to build or test the sync path.
- **Status** — `candidate` and no higher. The template and its `Deploy Sync`
  workflow exist, but nothing is provisioned in Azure, the sync service still
  holds state in memory, and none of it has been validated by real use.

## Azure Key Vault

```meta
status: candidate
type: service
related: [".arc42/07-deployment-view.md#cloud-deployment-azure", ".arc42/adr/guidelines/0013-authorization-zero-trust.md"]
```

The secret store for the cloud service.

- **Used for** — GitHub webhook secrets and OAuth tokens.
- **Why** — the constraint that credentials are never held in app configuration;
  user-source credentials stay on the desktop entirely.
- **How** — `infra/sync/main.bicep` provisions it with RBAC authorization and
  grants the sync service's managed identity **Key Vault Secrets User**. It is
  provisioned empty: the sync tier holds no Cosmos key and no registry credential
  to put in it, so the vault is standing ready for the webhook and OAuth
  secrets rather than holding anything today.

## Azure Monitor

```meta
status: candidate
type: service
related: [".arc42/07-deployment-view.md#provisioning-and-delivery", ".arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md"]
alternatives: ["Aspire dashboard only"]
```

The telemetry sink for the deployed cloud service — a Log Analytics workspace and
a workspace-based Application Insights resource over it.

- **Used for** — requests, dependencies, traces, exceptions and metrics from the
  sync container app, plus the container app environment's console and system
  logs. OpenTelemetry already flows through `AddServiceDefaults()`, so this is
  wiring rather than design; locally the same signals go to the Aspire dashboard
  instead.
- **Why** — the deployed service scales to zero and has no dashboard of its own,
  so without a sink its telemetry ends when the replica does.
- **How** — provisioned by `infra/sync/main.bicep`. The container app environment
  logs through the `azure-monitor` destination and a diagnostic setting rather
  than the `log-analytics` destination, because the latter wants the workspace's
  shared key inline in configuration and this deployment issues no keys.
- **Constraint** — application observability **only**. No task content, no session
  content, no owner-identifying payload is written here. A telemetry pipeline
  samples and drops under load, so anything a dashboard answers from would
  inherit that sampling and answer wrongly exactly when the system is busiest.
  This is a rule about what the service logs; no template can enforce it.

## Firebase Cloud Messaging

```meta
status: candidate
type: service
related: [".arc42/07-deployment-view.md#cloud-deployment-azure", ".tech/mobile.md#android"]
alternatives: ["Azure Notification Hubs"]
```

The push-notification transport to the phone client.

- **Used for** — delivering sync and triage notifications to Android.
- **Why** — the native push channel for Android; no additional Azure resource
  needed.
