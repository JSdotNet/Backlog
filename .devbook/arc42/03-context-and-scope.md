# 03. Context and Scope

```meta
```

## Business Context

```meta
related: [".devbook/arc42/01-introduction-and-goals.md#requirements-overview"]
```

![System Context — Backlog](c4:business-context)

### Domain boundary: Capture vs. Inbox

```meta
related: [".devbook/domain/context-map.md", ".devbook/domain/capture/domain.md#capture", ".devbook/domain/inbox/domain.md#inbox-item"]
```

The key internal boundary separates *how items enter* from *what happens after arrival*. `.devbook/domain/context-map.md` owns the bounded contexts and the `ItemCaptured` contract; this chapter records only the consequence:

| Concern | Owner |
|---|---|
| How items enter the system (sources, polling, syncing) | **Capture** |
| What happens to items after arrival (triage, classify, route) | **Inbox** |

Capture delivers normalized `InboxItem`s to the Inbox incoming queue via the
`ItemCaptured` contract (see `.devbook/domain/capture/domain.md#itemcaptured`).
Capture stays coupled to Inbox as one deployable pipeline (raw input → triage → route). It may split if capture tooling gets its own owner, which `.devbook/arc42/11-risks-and-technical-debt.md` tracks as R4.

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

Local desktop workers drive all inbound polling (YouTube, Websites, Email), so credentials never leave the machine.

## Access Channels (Scope)

```meta
related: [".devbook/arc42/05-building-block-view.md#container-view"]
```

| Channel / component | Role |
|---|---|
| **Desktop App** | Local-first full client; runs all fetch workers and manages all domains. |
| **Mobile App** | Mobile-first, offline-first capture; syncs via cloud. |
| **IDE Extensions** | VS Code, Visual Studio & GitHub Copilot App integration for backlog/knowledge browsing and capture. |
| **Cloud Service** (optional) | Thin sync/coordination layer: device sync, webhook forwarding, push, machine registry. |
| **MCP endpoint** (inside the Desktop App) | An AI session's tool surface over the running desktop app, on loopback. |

The MCP endpoint's tools cover plan items, status moves, and
[remarks](../domain/devbook/domain.md#remark) on devbook chapters. See local ADR 0012 in
`.devbook/arc42/adr/`.




