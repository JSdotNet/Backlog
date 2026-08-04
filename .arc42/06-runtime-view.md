# 06. Runtime View

```meta
status: active
related: [".arc42/05-building-block-view.md"]
```

Key runtime scenarios that exercise the building blocks from chapter 05 and show how
the local-first and thin-cloud strategies play out dynamically.

## Backlog Entry to GitHub Issue

```meta
status: active
related: [".arc42/05-building-block-view.md#desktop-app"]
```

Creating a backlog entry writes markdown locally first, then asynchronously creates
one GitHub issue per targeted repo.

```mermaid
sequenceDiagram
    actor User
    participant UI as Desktop UI
    participant Backlog as Backlog Service
    participant Store as Local Storage
    participant DB as SQLite
    participant GitHub as GitHub API

    User->>UI: Create backlog entry
    UI->>Backlog: createEntry(title, body, repo_ids, tags)
    Backlog->>Store: Write markdown file
    Backlog->>DB: Update SQLite index
    Backlog-->>UI: Entry created (id)

    Note over Backlog,GitHub: Async GitHub sync - one issue per repo_id
    loop For each repo_id
        Backlog->>GitHub: POST /repos/{repo}/issues
        GitHub-->>Backlog: 201 Created (issue_number, html_url)
        Backlog->>Store: Update entry with github_issue_ids
        Backlog->>DB: Update index with issue links
    end

    Backlog-->>UI: github_issue_ids available
    UI-->>User: Entry visible with GitHub issue links
```

## State Sync and Webhook Forwarding

```meta
status: active
related: [".arc42/05-building-block-view.md#cloud-service"]
```

In connected mode the desktop pushes state snapshots to the cloud, the phone pulls
deltas, and GitHub webhooks are validated and forwarded to the desktop.

```mermaid
sequenceDiagram
    participant Desktop as Desktop App
    participant Cloud as Cloud Service
    participant DB as Cloud Database
    participant Phone as Phone App
    participant GH as GitHub

    Desktop->>+Cloud: POST /sync/push (state snapshot)
    Cloud->>DB: Store SyncPayload (TTL 7 days)
    Cloud-->>-Desktop: 200 OK

    Note over Phone,Cloud: Phone comes online
    Phone->>+Cloud: GET /sync/pull?since=token
    Cloud->>DB: Query delta for user
    DB-->>Cloud: Changed items since token
    Cloud-->>-Phone: 200 OK (items, next_sync_token)

    Note over GH,Cloud: GitHub webhook received
    GH->>+Cloud: POST /webhooks/github
    Cloud->>Cloud: Validate HMAC signature
    Cloud->>DB: Store WebhookEvent (TTL 24h)
    Cloud-->>-GH: 202 Accepted
    Cloud-)Desktop: SSE - github.event forwarded
```

## Mobile Capture and Sync

```meta
status: active
related: [".arc42/05-building-block-view.md#mobile-app"]
```

Captures are stored locally first and flushed to the cloud when the network is
available, with exponential-backoff retry on failure.

```mermaid
sequenceDiagram
    actor User
    participant App as Phone App
    participant Storage as Local SQLite
    participant Sync as Sync Engine
    participant Cloud as Cloud Service

    User->>App: Capture item (text or voice)
    App->>Storage: INSERT inbox_item (status=captured)
    App-->>User: Stored locally

    Note over Sync,Cloud: When network becomes available
    Sync->>Storage: Query pending items
    Storage-->>Sync: inbox_items where status=pending_sync
    Sync->>+Cloud: POST /sync/items (batch upload)
    Cloud-->>-Sync: 200 OK (merged_count)
    Sync->>Storage: UPDATE status=synced
    App-->>User: Sync complete

    Note over Sync,Cloud: On sync failure
    Sync->>+Cloud: POST /sync/items
    Cloud-->>-Sync: Error or timeout
    Sync->>Storage: UPDATE status=sync_failed, increment retry
    Sync->>Sync: Schedule retry (exponential backoff)
```

## Sync Item Lifecycle

```meta
status: active
related: [".arc42/05-building-block-view.md#mobile-app"]
```

```mermaid
stateDiagram-v2
    [*] --> Captured: User captures item
    Captured --> PendingSync: Network available
    PendingSync --> Syncing: Sync engine picks up item
    Syncing --> Synced: POST /sync/items succeeds
    Syncing --> SyncFailed: Network error or timeout
    SyncFailed --> PendingSync: Retry scheduled
    Synced --> [*]
    PendingSync --> Captured: Network lost before sync starts

    note right of Synced
        Item confirmed on Cloud Service
    end note
    note right of SyncFailed
        Exponential backoff, max 5 retries
    end note
```

## IDE Context-aware Capture

```meta
status: active
related: [".arc42/05-building-block-view.md#ide-extensions"]
```

Selecting code in the IDE captures it with repo context (file, line, branch) into the
local markdown cache and posts it to the inbox.

```mermaid
sequenceDiagram
    actor Dev as Developer
    participant IDE as IDE Extension
    participant Ext as Extension API
    participant Capture as Capture Service
    participant API as Backend API
    participant Store as Local Markdown

    Dev->>IDE: Select code -> right-click Capture
    IDE->>Ext: Get repo context
    Ext-->>IDE: file_path, line, branch, selection
    IDE->>Dev: Show capture dialog (pre-filled context)
    Dev->>IDE: Add title and tags -> confirm
    IDE->>Capture: createItem(title, body, metadata)
    Capture->>Store: Write to local markdown cache
    Capture->>API: POST /inbox/items (source=ide-vs-code)
    API-->>Capture: 201 Created (id)
    Capture-->>IDE: Item created (id, deep_link)
    IDE-->>Dev: Confirmation + deep-link to item
```

## Repository Baseline Scan and Health Signal

```meta
status: active
related: [".arc42/05-building-block-view.md#desktop-app", ".arc42/08-crosscutting-concepts.md#shared-data-types"]
```

Repository Management scans registered repositories, compares discovered package
versions with Technology Stack baselines, and emits monitoring signals when drift is
found.

```mermaid
sequenceDiagram
    participant Scheduler as Desktop Scheduler
    participant RepoMgmt as Repository Management
    participant RepoRegistry as Repository Registry
    participant Pkg as Package Registries
    participant TechStack as Technology Stack
    participant DB as SQLite
    participant Monitoring as Monitoring Service

    Scheduler->>RepoMgmt: Start dependency scan
    RepoMgmt->>RepoRegistry: Load registered repos and manifests
    loop For each registered manifest
        RepoMgmt->>Pkg: Query latest package metadata
        Pkg-->>RepoMgmt: Current versions / advisories
    end
    RepoMgmt->>TechStack: Compare discovered versions to baselines
    TechStack-->>RepoMgmt: Drift and policy evaluation
    RepoMgmt->>DB: Persist repository health snapshot
    RepoMgmt->>Monitoring: Emit ProgressSignal(s)
    Monitoring-->>Scheduler: Dashboard/alert state updated
```

## Remote PC Wake and Status Update

```meta
status: active
related: [".arc42/05-building-block-view.md#desktop-app", ".arc42/05-building-block-view.md#cloud-service"]
```

Dev PC Management uses the optional cloud registry to wake a registered machine and
reconcile its status when the target desktop comes back online.

```mermaid
sequenceDiagram
    actor User
    participant Desktop as Desktop App
    participant DevPC as Dev PC Management
    participant Cloud as Cloud Service
    participant Registry as Machine Registry
    participant Target as Remote Desktop Agent
    participant Monitoring as Monitoring Service

    User->>Desktop: Wake remote machine
    Desktop->>DevPC: wake(machine_id)
    DevPC->>Cloud: POST /pc/wake/{machine_id}
    Cloud->>Registry: Load machine registration
    Registry-->>Cloud: MAC address, last status
    Cloud->>Target: Relay WoL packet
    Cloud-->>DevPC: 202 Accepted

    Note over Target,Cloud: Machine resumes and heartbeat starts
    Target->>Cloud: POST /pc/heartbeat
    Cloud->>Registry: Update status=online
    Cloud-->>DevPC: pc.wake-result / status update
    DevPC->>Monitoring: Emit machine-online signal
    Monitoring-->>User: Machine visible as online
```
