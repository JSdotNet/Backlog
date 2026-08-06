# Cloud Stack

```meta
status: candidate
related: [".tech/technology-graph.md", ".arc42/07-deployment-view.md#cloud-deployment-azure"]
```

> The optional, deliberately thin Azure service. It only does sync coordination,
> webhook forwarding, push, and the machine registry — so this layer stays small
> on purpose.

## ASP.NET Core Minimal APIs

```meta
status: candidate
kind: framework
depends-on: [".tech/shared.md#net-runtime", ".tech/shared.md#c-language"]
related: [".arc42/04-solution-strategy.md#thin-cloud-rich-desktop", ".arc42/09-architecture-decisions.md"]
alternatives: ["Azure Functions", "Controller-based ASP.NET Core"]
```

The HTTP surface of the cloud service.

- **Used for** — state sync endpoints, GitHub webhook intake, push dispatch, and
  the remote PC registry.
- **Why** — the organization's governed .NET stack for services; minimal APIs fit
  a handful of endpoints without ceremony.

## .NET Aspire

```meta
status: candidate
kind: framework
depends-on: [".tech/cloud.md#aspnet-core-minimal-apis"]
alternatives: ["Docker Compose only", "no orchestration"]
```

The app-model and orchestration layer for local run and deployment.

- **Used for** — composing the cloud service with its dependencies, local
  dashboard/telemetry, and generating deployment artifacts.
- **Why** — already the assumed local-run and QA tooling in this repository's
  workflow guidance; gives observability without bespoke wiring.

## Azure Container Apps

```meta
status: candidate
kind: platform
depends-on: [".tech/cloud.md#aspnet-core-minimal-apis"]
related: [".arc42/07-deployment-view.md#cloud-deployment-azure"]
alternatives: ["Azure App Service"]
```

The compute host for the cloud service.

- **Used for** — running a single-region, single-instance deployment sized for
  personal use.
- **Why** — scale-to-low cost profile with container-based deploys; App Service
  remains an equivalent fallback.

## Azure Cosmos DB

```meta
status: candidate
kind: service
related: [".arc42/07-deployment-view.md#cloud-deployment-azure"]
alternatives: ["Azure PostgreSQL", "Azure Table Storage"]
```

The cloud data store for cross-device coordination state.

- **Used for** — sync state, buffered webhook events, and the machine registry,
  all with TTL-based expiry (7 days / 24 hours).
- **Why** — TTL support and a serverless tier match a small, transient,
  document-shaped workload.

## Azure Key Vault

```meta
status: candidate
kind: service
related: [".arc42/07-deployment-view.md#cloud-deployment-azure"]
```

The secret store for the cloud service.

- **Used for** — GitHub webhook secrets and OAuth tokens.
- **Why** — the constraint that credentials are never held in app configuration;
  user-source credentials stay on the desktop entirely.

## Firebase Cloud Messaging

```meta
status: candidate
kind: service
related: [".arc42/07-deployment-view.md#cloud-deployment-azure", ".tech/mobile.md#android"]
alternatives: ["Azure Notification Hubs"]
```

The push-notification transport to the phone client.

- **Used for** — delivering sync and triage notifications to Android.
- **Why** — the native push channel for Android; no additional Azure resource
  needed.
