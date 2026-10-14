# 03. Context and Scope

```meta
```

## Business Context

```meta
related: [".devbook/arc42/01-introduction-and-goals.md#requirements-overview"]
```

```mermaid
C4Context
    title System Context — Backlog

    Person(user, "ME", "Personal owner of the system across projects and devices")

    System(promptBacklog, "Backlog", "Local-first personal productivity system: capture, triage, backlog, knowledge, and monitoring")

    System_Ext(github, "GitHub", "Issue tracking, repository management, and webhook events")
    System_Ext(youtube, "YouTube", "Subscription feed for content capture")
    System_Ext(email, "Email / IMAP", "Email capture inbox")
    System_Ext(websites, "Websites / RSS", "Web content monitoring")
    System_Ext(appInsights, "Application Insights", "Telemetry for monitoring dashboards")
    System_Ext(pushProvider, "Push Notification Provider", "FCM for Android mobile alerts")

    Rel(user, promptBacklog, "Captures, triages, manages backlog and knowledge")
    Rel(promptBacklog, github, "Syncs issues; receives webhook events")
    Rel(promptBacklog, youtube, "Polls subscription feed")
    Rel(promptBacklog, email, "Ingests via IMAP")
    Rel(promptBacklog, websites, "Monitors via RSS and DOM diff")
    Rel(promptBacklog, appInsights, "Reads telemetry signals")
    Rel(promptBacklog, pushProvider, "Sends push notifications")
```

### Domain boundary: Capture vs. Inbox

```meta
related: [".devbook/domain/context-map.md", ".devbook/domain/capture/domain.md#capture", ".devbook/domain/inbox/domain.md#inbox-item"]
```

The most important internal boundary distinguishes *how items enter* from *what
happens after arrival*. `.devbook/domain/context-map.md` owns the bounded contexts,
the `ItemCaptured` published language contract, and the relationship between them.
This chapter records only the architectural consequence:

| Concern | Owner |
|---|---|
| How items enter the system (sources, polling, syncing) | **Capture** |
| What happens to items after arrival (triage, classify, route) | **Inbox** |

Capture delivers normalized `InboxItem`s to the Inbox incoming queue via the
`ItemCaptured` contract (see `.devbook/domain/capture/domain.md#itemcaptured`).
Capture stays tightly coupled to Inbox as one deployable pipeline (raw input →
triage → route). It may split later if capture tooling becomes independently
owned; `.devbook/arc42/11-risks-and-technical-debt.md` tracks that as R4.

## External Interfaces

```meta
```

| External system | Direction | Interface | Purpose |
|---|---|---|---|
| **GitHub** | out / in | HTTPS, `gh` CLI, webhooks | Issue sync (out), webhook events forwarded via cloud (in) |
| **YouTube** | in | HTTPS (API) | Poll subscribed channels for content capture |
| **Websites / RSS** | in | HTTPS (RSS, DOM diff) | Monitor sites for new content |
| **Email / IMAP** | in | IMAP | Ingest configured mailboxes as inbox items |
| **Package Registries** (npm, NuGet, PyPI) | in | HTTPS | Dependency scanning for Repository Management |
| **Application Insights** | in | HTTPS | Read telemetry signals for monitoring dashboards |
| **Push Provider** (FCM) | out | HTTPS | Deliver push notifications to the Android mobile app |

All inbound polling interfaces (YouTube, Websites, Email) are driven by **local
desktop workers**, so their credentials never leave the user's machine.

## Access Channels (Scope)

```meta
related: [".devbook/arc42/05-building-block-view.md#container-view"]
```

These are in scope:

| Channel / component | Role |
|---|---|
| **Desktop App** | Local-first full client; runs all fetch workers and manages all domains. |
| **Mobile App** | Mobile-first, offline-first capture; syncs via cloud. |
| **IDE Extensions** | VS Code, Visual Studio & GitHub Copilot App integration for backlog/knowledge browsing and capture. |
| **Cloud Service** (optional) | Thin sync/coordination layer: device sync, webhook forwarding, push, machine registry. |
| **MCP endpoint** (inside the Desktop App) | An AI session's tool surface over the running desktop app, on loopback: plan items, status moves, and [remarks](../domain/devbook/domain.md#remark) on devbook chapters. See local ADR 0012 in `.devbook/arc42/adr/`. |




