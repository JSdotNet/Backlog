# 04. Solution Strategy

```meta
status: active
related: [".arc42/09-architecture-decisions.md"]
```

The core strategic decisions that turn the goals and constraints of chapters 01–02
into a coherent architecture.

## Local-first Architecture

```meta
status: active
related: [".arc42/02-constraints.md#technical-constraints"]
```

The desktop app is fully functional standalone and owns the canonical data:

- **All capture runs locally** — YouTube, website, and email polling happen on the
  desktop via background workers.
- **Cloud is optional** — it adds multi-device sync (mobile), GitHub webhook
  forwarding, and the Remote PC registry.
- **No cloud dependency for core workflows** — capture, triage, backlog, knowledge,
  and monitoring all work offline.
- **Desktop works independently or connected** — the cloud enhances but never gates
  functionality; mode switching is seamless.
- **No account required** — a personal-use system that syncs without login.

This directly serves quality goals 1 (availability) and 2 (credential privacy) in
`.arc42/01-introduction-and-goals.md#quality-goals`.

## Domain Decomposition

```meta
status: active
related: [".arc42/01-introduction-and-goals.md#requirements-overview"]
```

The system is split into independent functional domains (Capture, Inbox, Backlog,
Second Brain, Monitoring, Technology Stack, Dev PC Management, Repository Management).
Each is separately designable and deployable within the desktop client. A thin,
one-directional pipeline (Capture → Inbox → route to Backlog / Second Brain →
signals to Monitoring) keeps coupling low and lets channels expose a subset of
domains without owning domain lifecycle rules.

## Thin Cloud, Rich Desktop

```meta
status: active
related: [".arc42/07-deployment-view.md#cloud-deployment-azure"]
```

Responsibilities are deliberately pushed to the desktop; the cloud is minimized:

| Responsibility | Runs on | Rationale |
|---|---|---|
| YouTube / website / email fetching | Desktop workers | Keep external credentials off the cloud |
| GitHub issue sync | Desktop (`gh` CLI / API) | Direct calls, no relay needed |
| Full-text search | Desktop JSON-backed local indexes | Fast local search, no cloud index cost |
| Backlog / Knowledge CRUD | Desktop local storage | Markdown is canonical |
| Sync coordination, webhook forwarding, push, PC registry | Cloud | Cross-device concerns only |

## Technology Choices

```meta
status: active
related: [".arc42/09-architecture-decisions.md"]
```

| Channel | Candidate stack |
|---|---|
| **Desktop** | .NET MAUI Blazor Hybrid (WinUI 3 head on Windows, Razor UI in embedded WebView2); markdown + JSON. See `.arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md` — chosen so the desktop client can be launched from an Aspire AppHost and driven end-to-end with Playwright (via WebView2's CDP debugging port), while keeping the same native filesystem access and background-worker guarantees as plain WinUI 3. |
| **Mobile** | .NET MAUI (preferred, C#) with Blazor Hybrid or Blazor WebAssembly PWA as the closest fallback options; JSON-backed local storage |
| **IDE** | VS Code extension (TypeScript, webview) and Visual Studio extension (C#, WPF) |
| **Cloud** | C# / ASP.NET Core Minimal APIs on .NET, Azure hosting, Cosmos DB / PostgreSQL |

The cloud-service stack follows the organization's .NET ADRs (see
`.arc42/09-architecture-decisions.md`). Desktop, mobile, and IDE stacks are chosen
per platform and are intentionally not governed by the .NET ADRs.

## Cross-cutting Strategy

```meta
status: active
related: [".arc42/08-crosscutting-concepts.md"]
```

Storage & sync (markdown canonical, last-write-wins on edits, always-create on new
items), tagging/organization (`#tags`, PARA grouping, tag index), and authentication
(no account for personal use, OAuth for GitHub, device auth for cloud) are treated as
cross-cutting concepts applied uniformly across channels. They are detailed in
`.arc42/08-crosscutting-concepts.md`.




