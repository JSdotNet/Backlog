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
related: [".arc42/07-deployment-view.md#cloud-deployment-azure"]
alternatives: ["Azure App Service"]
```

The compute host for the cloud service.

- **Used for** — running a single-region, single-instance deployment sized for
  personal use.
- **Why** — scale-to-low cost profile with container-based deploys; App Service
  remains an equivalent fallback.

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
related: [".arc42/07-deployment-view.md#cloud-deployment-azure"]
alternatives: ["Azure PostgreSQL", "Azure Table Storage"]
```

The cloud data store for cross-device coordination state.

- **Used for** — sync state, buffered webhook events, and the machine registry,
  all with TTL-based expiry (7 days / 24 hours).
- **Why** — TTL support and a serverless tier match a small, transient,
  document-shaped workload. The choice against PostgreSQL is still open in
  `.arc42/04-solution-strategy.md#technology-choices`; the sync service currently
  holds state in memory.

## Azure Key Vault

```meta
status: candidate
type: service
related: [".arc42/07-deployment-view.md#cloud-deployment-azure"]
```

The secret store for the cloud service.

- **Used for** — GitHub webhook secrets and OAuth tokens.
- **Why** — the constraint that credentials are never held in app configuration;
  user-source credentials stay on the desktop entirely.

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
