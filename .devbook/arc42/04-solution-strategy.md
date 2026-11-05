# 04. Solution Strategy

```meta
related: [".devbook/arc42/09-architecture-decisions.md"]
```

## Local-first Architecture

```meta
related: [".devbook/arc42/02-constraints.md#technical-constraints"]
```

The desktop app is fully functional standalone and owns the canonical data:

- **All capture runs locally.** Background workers poll YouTube, websites and email.
- **Cloud is optional.** It adds multi-device sync, GitHub webhook forwarding and the Remote PC registry.
- **No cloud dependency for core workflows.** Capture, triage, backlog, knowledge and monitoring work offline.
- **Standalone or connected.** Either mode the glossary defines (`.devbook/arc42/12-glossary.md#terms`), and the cloud never gates a feature.
- **No account required.** Sync works without login.

This serves quality goals 1 (availability) and 2 (credential privacy) in `.devbook/arc42/01-introduction-and-goals.md#quality-goals`.

## Domain Decomposition

```meta
related: [".devbook/arc42/01-introduction-and-goals.md#requirements-overview"]
```

`.devbook/arc42/01-introduction-and-goals.md#requirements-overview` lists the domains and `.devbook/domain/context-map.md#subdomain-landscape` classifies them. Each is separately designable and deployable within the desktop client. A one-directional pipeline keeps coupling low: Capture → Inbox → route to Tasks or Devbook → signals to Monitoring. Channels can expose a subset of domains without owning lifecycle rules.

## Thin Cloud, Rich Desktop

```meta
related: [".devbook/arc42/07-deployment-view.md#cloud-deployment-azure"]
```

| Responsibility | Runs on | Rationale |
|---|---|---|
| YouTube / website / email fetching | Desktop workers | Keep external credentials off the cloud |
| GitHub issue sync | Desktop (`gh` CLI / API) | Direct calls, no relay needed |
| Full-text search | Desktop, over the devbook SQLite database in app storage | Fast local search, no cloud index cost |
| Tasks / Devbook CRUD | Desktop local storage | Tasks live in `backlog.db` (local ADR 0003); devbook chapters stay markdown in their repository |
| Sync coordination, webhook forwarding, push, PC registry | Cloud | Cross-device concerns only |

## Technology Choices

```meta
related: [".devbook/arc42/09-architecture-decisions.md"]
```

| Channel | Stack |
|---|---|
| **Desktop** | .NET MAUI Blazor Hybrid (WinUI 3 head, Razor UI in WebView2); SQLite for tasks, JSON for settings. |
| **Mobile** | .NET MAUI Blazor Hybrid on Android; a device SQLite file holds the outbox and the pulled views. |
| **IDE** | VS Code extension (TypeScript). Visual Studio has no extension; `.devbook/tech/ide.md` lists it as a candidate |
| **Cloud** | C# / ASP.NET Core Minimal APIs on .NET, Azure Container Apps (consumption, scale-to-zero), Cosmos DB for NoSQL (serverless). |

- **Desktop:** the WinUI 3 head is the Windows one. `.devbook/arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md` records why: an Aspire AppHost can launch it and Playwright can drive it end to end.
- **Mobile:** `.devbook/arc42/05-building-block-view.md#mobile-app` describes the device store.
- **Cloud:** `.devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md` records why Cosmos DB won over PostgreSQL and Container Apps over App Service.

The cloud stack follows the organization's .NET ADRs (`.devbook/arc42/09-architecture-decisions.md`); desktop, mobile and IDE stacks are chosen per platform and not governed by them.

## Cross-cutting Strategy

```meta
related: [".devbook/arc42/08-crosscutting-concepts.md"]
```

Three concerns apply uniformly across channels, and
`.devbook/arc42/08-crosscutting-concepts.md` details them:

- **Storage and sync**: a local canonical store, with conflicts resolved as the
  glossary's *Last-write-wins* says (`.devbook/arc42/12-glossary.md#terms`).
- **Tagging and organization**: `#tags`, PARA grouping, and a tag index.
- **Authentication**: no account for personal use, OAuth for GitHub, and device
  auth for the cloud.




