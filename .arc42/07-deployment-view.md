# 07. Deployment View

```meta
status: active
```

How Prompt Backlog's containers map onto infrastructure. There are two deployment
domains: the **user's local machines** (canonical) and the **optional Azure-hosted
cloud service** (additive).

## Local Deployment (Desktop)

```meta
status: active
related: [".arc42/05-building-block-view.md#desktop-app", ".arc42/08-crosscutting-concepts.md#storage-and-sync"]
```

The desktop app is installed per machine (Windows, macOS, Linux) and is the canonical
deployment. Everything needed for core workflows runs here.

- **Local Storage** — markdown files under a user-owned root (e.g. `~/PromptBacklog/`)
  are the source of truth; JSON files provide indexes and metadata.
- **Local Fetch Workers** — YouTube, website, email, GitHub-sync, and stale-detection
  workers run in-process/background on the desktop.
- **IDE Extensions** — installed in VS Code / Visual Studio on the same machine; read
  the desktop's local markdown / local API directly.

```
~/PromptBacklog/
  inbox/{incoming,processed}/     # captured / triaged items (markdown)
  projects/<repo-id>/{backlog,notes}/
  knowledge/topics/               # knowledge notes by topic
  monitoring/dashboards/          # saved dashboard configs
  tags/index.md                   # tag registry
  .index/*.json                   # JSON indexes and metadata
```

## Cloud Deployment (Azure)

```meta
status: active
related: [".arc42/05-building-block-view.md#cloud-service", ".arc42/09-architecture-decisions.md"]
```

The optional cloud service is deployed to Azure as a single-region, low-cost
footprint sized only for sync coordination and webhook forwarding.

```mermaid
flowchart TB
    subgraph "Azure"
        subgraph "Compute"
            AppService["Azure App Service\nor Container Apps"]
        end
        subgraph "Data"
            CosmosDB["Azure Cosmos DB\n(sync state, webhook events,\nmachine registry)"]
            KeyVault["Azure Key Vault\n(webhook secrets, OAuth tokens)"]
        end
    end

    subgraph "External"
        GitHub["GitHub\n(webhooks in)"]
        FCM["Firebase Cloud Messaging\n(Android push)"]

    end

    subgraph "Clients"
        Desktop["Desktop App"]
        Mobile["Mobile App"]
        IDE["IDE Extensions"]
    end

    Desktop -->|"HTTPS — state sync"| AppService
    Mobile -->|"HTTPS — sync and offline flush"| AppService
    IDE -->|"HTTPS — state sync"| AppService

    AppService --> CosmosDB
    AppService --> KeyVault

    GitHub -->|"Webhook events"| AppService

    AppService -->|"Android push"| FCM


    FCM -.->|Notification| Mobile

```

Deployment considerations:

- **Single region** is sufficient for a personal tool; a single App Service /
  Container App instance meets demand.
- **TTL-based cleanup** — sync payloads (7 days) and webhook events (24h) expire
  automatically, keeping storage minimal.
- **Webhook timeout handling** — GitHub expects a response within ~10s, so the
  service stores-and-forwards.
- **Secrets in Key Vault** — webhook secrets and OAuth tokens are externalized.
- **No blob storage** — attachments live on the desktop's local file system.



