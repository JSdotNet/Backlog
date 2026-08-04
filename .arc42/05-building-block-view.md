# 05. Building Block View

```meta
status: active
```

Static decomposition of Prompt Backlog, from the system-level container view down to
the internal structure of each access channel.

## Container View

```meta
status: active
related: [".arc42/03-context-and-scope.md#access-channels-scope", ".domain/context-map.md"]
```

Container boundaries below are the deployable/runtime split; the domains they
serve (Capture, Inbox, Backlog Management, Second Brain, Monitoring, Technology
Stack, Dev PC Management, Repository Management) are defined in
`.domain/context-map.md` and each context's own `.domain/<context>/domain.md` —
this view does not restate domain responsibilities.

```mermaid
C4Container
    title Container Diagram — Prompt Backlog

    Person(user, "ME", "Personal owner of the system")

    System_Boundary(b0, "Prompt Backlog") {
        Container(desktop, "Desktop App", "WinUI 3, Markdown + JSON", "Local-first Windows client — runs all fetch workers and manages all domains")
        Container(mobile, "Mobile App", ".NET MAUI / Blazor Hybrid, JSON", "Capture-first mobile client with offline storage")
        Container(ide, "IDE Extensions", "TypeScript / C#", "VS Code and Visual Studio integrations")
        Container(cloud, "Cloud Service", ".NET / ASP.NET Core", "Thin optional sync layer: device sync, webhook forwarding, push, PC registry")
        ContainerDb(localStore, "Local Storage", "Markdown files, JSON", "Desktop canonical data store — markdown is source of truth")
        ContainerDb(cloudDb, "Cloud Database", "Cosmos DB / PostgreSQL", "Sync state, webhook events, machine registry")
    }

    System_Ext(github, "GitHub", "Issues and webhooks")
    System_Ext(pushProvider, "Push Provider", "FCM")
    System_Ext(externalSources, "External Sources", "YouTube, Email, Websites / RSS")

    Rel(user, desktop, "Uses — capture, backlog, knowledge, monitoring", "local")
    Rel(user, mobile, "Captures on mobile", "touch / voice")
    Rel(user, ide, "Browses backlog and knowledge", "IDE commands")

    Rel(desktop, localStore, "Reads and writes", "file system")
    Rel(desktop, github, "Syncs issues", "HTTPS / gh CLI")
    Rel(desktop, externalSources, "Polls for new content", "HTTPS / IMAP")
    Rel(desktop, cloud, "Pushes state snapshots", "HTTPS, optional")

    Rel(mobile, cloud, "Syncs items and pulls state", "HTTPS")
    Rel(ide, desktop, "Reads backlog and knowledge", "local API / file system")

    Rel(cloud, cloudDb, "Reads and writes sync state", "")
    Rel(cloud, pushProvider, "Sends notifications", "HTTPS")
    Rel(cloud, github, "Receives webhooks", "HTTPS")
    Rel(cloud, desktop, "Forwards webhook events", "SSE / WebSocket, optional")
```

### System-level flow

```mermaid
flowchart TB
  subgraph "Prompt Backlog System"
    subgraph "Capture Sources"
      Mobile["Mobile App\n(speech, shortcuts)"]
      YTWorker["YouTube Fetcher"]
      WebWorker["Website Monitor"]
      EmailWorker["Email Fetcher\n(IMAP)"]
      IDECap["IDE Capture\n(adapter)"]
      ManualCap["Manual / Web Clipper"]
    end

    subgraph "Desktop App (standalone or connected)"
      InboxQueue["Inbox Queue\n(triage, route)"]
      BacklogSvc["Backlog\n(refine, prioritize)"]
      KnowledgeSvc["Second Brain\n(PARA, links)"]
      MonitoringSvc["Monitoring\n(dashboards, signals)"]
      TechStackSvc["Technology Stack\n(baselines, adoption)"]
      DevPCSvc["Dev PC Management\n(registry, compliance)"]
      RepoMgmtSvc["Repository Management\n(repo registry, health)"]
    end

    subgraph "IDE Extensions"
      IDEBrowse["Browse Backlog\n& Knowledge"]
    end

    subgraph "Cloud Service (optional)"
      SyncAPI["Sync API"]
      GitHubWebhooks["GitHub Webhook\nReceiver"]
      PCRegistry["PC Registry\n& WoL Relay"]
      Notifications["Push Notifications"]
    end

    subgraph "Local Storage"
      LocalMD["Local Markdown\n(Canonical)"]
      JsonIndex["JSON\n(Indexes, Metadata)"]
    end

    subgraph "External Services"
      GitHub["GitHub API"]
      YouTube["YouTube API"]
      Websites["Websites / RSS"]
      Email["Email (IMAP)"]
      PackageRegs["Package Registries\n(npm, NuGet, PyPI)"]
      CopilotSessions["Copilot Sessions"]
      AppInsights["Application Insights"]
    end
  end

  Mobile -.->|sync| SyncAPI
  SyncAPI -.->|deliver| InboxQueue
  Mobile -->|offline: local| InboxQueue
  YTWorker --> InboxQueue
  WebWorker --> InboxQueue
  EmailWorker --> InboxQueue
  IDECap --> InboxQueue
  ManualCap --> InboxQueue

  YTWorker -->|poll| YouTube
  WebWorker -->|poll| Websites
  EmailWorker -->|IMAP| Email

  InboxQueue -->|route| BacklogSvc
  InboxQueue -->|route| KnowledgeSvc
  BacklogSvc <-->|embed| KnowledgeSvc
  BacklogSvc -->|signals| MonitoringSvc
  TechStackSvc --> DevPCSvc
  TechStackSvc --> RepoMgmtSvc
  RepoMgmtSvc --> MonitoringSvc
  DevPCSvc --> MonitoringSvc

  IDEBrowse -->|read| BacklogSvc
  IDEBrowse -->|read| KnowledgeSvc

  BacklogSvc -->|sync issues| GitHub
  RepoMgmtSvc -->|repo metadata| GitHub
  RepoMgmtSvc -->|dependency scan| PackageRegs
  CopilotSessions --> DevPCSvc
  GitHub -.->|webhooks| GitHubWebhooks
  GitHubWebhooks -.->|forward| SyncAPI

  BacklogSvc --> LocalMD
  KnowledgeSvc --> LocalMD
  InboxQueue --> LocalMD
  BacklogSvc --> JsonIndex
  RepoMgmtSvc --> JsonIndex
  DevPCSvc --> JsonIndex

  SyncAPI -.->|push| Notifications
  Notifications -.->|push| Mobile
  MonitoringSvc --> AppInsights

  PCRegistry -.->|WoL relay| SyncAPI
```

## Desktop App

```meta
status: active
related: [".arc42/06-runtime-view.md#backlog-entry-to-github-issue"]
```

Local-first Windows client. Serves Capture, Inbox, Backlog Management, Second Brain, Monitoring, Technology Stack, Dev PC Management, and Repository Management. It runs in two seamless modes: **Standalone** (no cloud) and
**Connected** (adds cloud sync, phone access, and webhook forwarding).

```mermaid
graph TB
  subgraph "Desktop App"
    UI["UI Layer\n(WinUI 3)"]

    subgraph "Core Services"
      Inbox["Inbox Service\n(capture, triage)"]
      Backlog["Backlog Service\n(browse, edit, route)"]
      Knowledge["Knowledge Service\n(organize, link)"]
      Monitoring["Monitoring Service\n(signals, dashboards)"]
    end

    subgraph "Local Fetch Workers"
      YTWorker["YouTube Fetcher\n(poll subscriptions)"]
      WebWorker["Website Monitor\n(RSS, DOM diff)"]
      EmailWorker["Email Fetcher\n(IMAP polling)"]
      GitHubWorker["GitHub Sync\n(gh CLI / API)"]
      StaleWorker["Stale Detection\n(flag old items)"]
    end

    subgraph "Infrastructure"
      LocalStore["Local Storage\n(Markdown files)"]
      JsonIndex["JSON Indexes\n(metadata, search)"]
      SyncClient["Sync Client\n(optional)"]
    end
  end

  SyncAPI["Cloud Sync API\n(optional)"]
  GitHub["GitHub API"]
  YouTube["YouTube API"]
  Websites["Websites / RSS"]
  Email["Email (IMAP)"]
  AppInsights["Application Insights"]

  UI --> Inbox
  UI --> Backlog
  UI --> Knowledge
  UI --> Monitoring

  Inbox --> LocalStore
  Backlog --> LocalStore
  Knowledge --> LocalStore
  Monitoring --> LocalStore

  Inbox --> JsonIndex
  Backlog --> JsonIndex
  Knowledge --> JsonIndex

  YTWorker --> YouTube
  WebWorker --> Websites
  EmailWorker --> Email
  GitHubWorker --> GitHub

  YTWorker --> Inbox
  WebWorker --> Inbox
  EmailWorker --> Inbox
  GitHubWorker --> Backlog

  SyncClient -.->|push state| SyncAPI
  SyncAPI -.->|webhook events| SyncClient
  Monitoring --> AppInsights
```

Local fetch workers keep external credentials on the machine, work offline (queuing
fetches), and give the user full control over frequency and retry behavior.

## Mobile App

```meta
status: active
related: [".arc42/06-runtime-view.md#mobile-capture-and-sync", ".arc42/06-runtime-view.md#sync-item-lifecycle"]
```

Android-first, offline-first capture app. Serves Capture, Inbox,
and lightweight Backlog Management. It owns mobile UI, push plumbing, and sync
transport, but not domain lifecycle rules.

```mermaid
graph TB
  UI["UI Layer\n(.NET MAUI / Blazor Hybrid)"]

  subgraph "App Layer"
    Capture["Capture Service\n(Quick add, Voice, Shortcuts)"]
    Storage["Local Storage\n(JSON)"]
    Sync["Sync Engine\n(Conflict resolution)"]
  end

  subgraph "Platform Layer"
    OS["OS Services\n(Camera, Microphone,\nShare Sheet)"]
    Creds["Secure Storage\n(Keychain / Vault)"]
  end

  Cloud["Cloud Sync\n(OneDrive / GDrive)"]
  API["Cloud Sync API\n(REST + Auth)"]

  UI --> Capture
  UI --> Storage
  UI --> Sync
  Capture --> OS
  Capture --> Creds
  Storage --> Sync
  Sync -->|HTTPS| API
  Sync -->|Optional| Cloud
  OS -->|Speech-to-text| Capture
```

## IDE Extensions

```meta
status: active
related: [".arc42/06-runtime-view.md#ide-context-aware-capture"]
```

Repo-aware integrations for VS Code and Visual Studio. Serve Inbox (capture intake),
Backlog Management, and Second Brain browsing. IDE packaging, extension-host APIs, and
release cadence are architecture concerns; domain lifecycle rules stay with the
owning domains.

```mermaid
graph TB
  subgraph "VS Code Extension"
    UI["Webview UI\n(React / Vue)"]
    Commands["Commands & Context\n(Menus, Keybindings)"]
    ExtAPI["Extension API\n(Repo context, File selection)"]
  end

  subgraph "Visual Studio Extension"
    VSUI["WPF UI\n(Tool Windows)"]
    VSCmd["Commands & Context\n(Context Menu)"]
    VSAPI["Extension API\n(Project context)"]
  end

  subgraph "Shared Services"
    Capture["Capture Service\n(Selection to Item)"]
    Browse["Backlog Browser\n(Repo-scoped queries)"]
    KnowBrowser["Knowledge Browser\n(Search & links)"]
  end

  API["Backend API\n(REST + Auth)"]
  LocalStore["Local Markdown\n(Cache)"]

  UI --> Capture
  UI --> Browse
  UI --> KnowBrowser
  Commands --> ExtAPI
  ExtAPI -->|Repo context| Capture

  VSUI --> Capture
  VSUI --> Browse
  VSCmd --> VSAPI
  VSAPI -->|Project context| Capture

  Capture --> LocalStore
  Browse --> API
  KnowBrowser --> API
  Capture -->|Sync| API
```

## Cloud Service

```meta
status: active
related: [".arc42/06-runtime-view.md#state-sync-and-webhook-forwarding", ".arc42/07-deployment-view.md#cloud-deployment-azure"]
```

A thin sync and coordination layer — deliberately not the backbone. It coordinates
device sync, receives and forwards GitHub webhooks, sends push notifications, and hosts a Remote PC registry / Wake-on-LAN relay. It stores minimal, mostly TTL-based state — never domain data or external credentials.

```mermaid
flowchart TB
  subgraph "Cloud Service (thin sync layer)"
    subgraph "API Layer"
      Gateway["API Gateway\n(Auth, Rate Limiting)"]
      SyncAPI["Sync Service\n(Device coordination)"]
    end
    subgraph "Webhook & Notifications"
      GitHubWebhooks["GitHub Webhook\nReceiver"]
      NotificationService["Notification Service\n(Push to Phone)"]
    end
    subgraph "Data Layer"
      DB["Cloud Database\n(Sync state only)"]
    end
  end

  subgraph "Clients"
    Phone["Phone App"]
    Desktop["Desktop App\n(runs all workers)"]
    IDE["IDE Extensions"]
  end

  GitHub["GitHub API"]

  Phone -->|Sync captures| Gateway
  Desktop -->|Sync state| Gateway
  IDE -->|Sync state| Gateway

  Gateway --> SyncAPI
  SyncAPI --> DB

  GitHub -->|Webhooks| GitHubWebhooks
  GitHubWebhooks -->|Forward event| SyncAPI
  SyncAPI --> NotificationService
  NotificationService -->|Push| Phone
```

Cloud components:

| Component | Responsibility |
|---|---|
| **API Gateway & Auth** | Minimal REST surface; GitHub OAuth for webhook registration; JWT device sessions; rate limiting. |
| **Sync Service** | The only domain-aware service; stores sync *state* (not domain data); delta push/pull, conflict listing/resolution. |
| **GitHub Webhook Receiver** | Validates HMAC-SHA256, stores events (TTL 24h), forwards to desktop; never processes domain data. |
| **Notification Service** | Push to phone (FCM); SSE/WebSocket to desktop for real-time forwarding. |
| **Remote PC Registry & WoL Relay** | Register machines, heartbeat, Wake-on-LAN relay, connection details. |









