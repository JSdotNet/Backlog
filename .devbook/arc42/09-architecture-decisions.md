# 09. Architecture Decisions

```meta
```

This chapter links to the decision records rather than restating them. There are
two sets, and they are numbered independently:

- **Inherited decisions** live in `.devbook/arc42/adr/guidelines/`. They are the
  organization's ADRs, imported into this repository on 2026-08-27 and
  authoritative here since. Nothing is fetched at read time.
- **Local decisions** live in `.devbook/arc42/adr/`. Backlog took them for itself,
  in their own sequence.

Both sequences start at 0001, so always name the folder when citing one:
*guidelines 0003* is Aspire, *local ADR 0003* is SQLite.

The ASP.NET-specific inherited decisions govern the **sync service** directly. It
is not the only governed code: the modules, the shared kernel, and the shared
component library are .NET too (see *Beyond the sync service* below). The IDE
channel is TypeScript and stays outside the .NET decision set.

## Sync-service alignments

```meta
related: [".devbook/arc42/adr/guidelines/README.md", ".devbook/arc42/05-building-block-view.md#cloud-service", ".devbook/arc42/07-deployment-view.md#cloud-deployment-azure"]
```

The *alignment* column is a reading of intent per decision, not a compliance
audit of implemented code. Each linked document carries its own **Deviations and
gaps** section, which is the accurate account.

An inherited decision marked `proposed` rather than `active` is one no shipped
code applies yet; 0012, 0013, 0017, and 0018 are in that state. It still binds the
first work that reaches its ground.

| Decision | Current alignment for the sync service |
|---|---|
| **[0001 — .NET 10](adr/guidelines/0001-adopt-dotnet-10.md)** | Target framework of `Backlog.Modules.Sync.Api`. |
| **[0003 — .NET Aspire](adr/guidelines/0003-aspire-for-web-services.md)** | Followed: the service is an Aspire resource (`sync`) and calls `AddServiceDefaults()`. |
| **[0005 — Modular monolith structure](adr/guidelines/0005-modular-monolith-structure.md)** | Followed: the service is the `Sync` module's own `.Api` project under `src/Modules/Sync/`. |
| **[0006 — CQRS](adr/guidelines/0006-cqrs-for-api-projects.md)** | Followed: `Backlog.SharedKernel.Handlers` declares `ICommandHandler`/`IQueryHandler` once, with no mediator, and every sync route delegates to one of the module's ten feature slices. |
| **[0007 — Minimal APIs](adr/guidelines/0007-minimal-apis-over-controllers.md)** | Followed: `MapGroup` plus `Results` helpers, no controllers, thin lambdas over handlers, and two endpoint filters. OpenAPI is served in Development only; Scalar is not wired up. |
| **[0010 — OpenTelemetry](adr/guidelines/0010-opentelemetry-observability.md)** | Wired through ServiceDefaults, and the Sync module owns its own `ActivitySource` and counter on top of it — the only module that does. |
| **[0012 — External identity (OIDC)](adr/guidelines/0012-authentication-external-identity-providers.md)** | Relevant only for the GitHub OAuth callback and any future external identity flow, not for device-session auth. |
| **[0013 — Authorization & Zero Trust](adr/guidelines/0013-authorization-zero-trust.md)** | Relevant for device authorization, least-privilege checks, and audit logging. None implemented — the baseline is single-user. |
| **[0014 — Persistence & repository boundaries](adr/guidelines/0014-persistence-and-repository-boundaries.md)** | Relevant for sync-state persistence and data ownership boundaries. |
| **[0015 — Resilience](adr/guidelines/0015-resilience-for-outbound-dependencies.md)** | Relevant for GitHub and push-delivery outbound calls. Cosmos is the service's own outbound dependency and sits outside the standard HTTP pipeline by construction; its timeout and retry caps are set on the client. |
| **[0017 — Problem Details](adr/guidelines/0017-http-error-contract-and-problem-details.md)** | Implemented: every failure is RFC 7807 with a `code` extension, mapped in one file, and unhandled exceptions go through `UseExceptionHandler()`. The `proposed` marker on the record has not caught up. |
| **[0018 — Configuration & options](adr/guidelines/0018-configuration-and-options-binding.md)** | Relevant for strongly typed settings and externalized secrets. |

## Beyond the sync service

```meta
related: [".devbook/arc42/adr/guidelines/README.md", ".devbook/arc42/05-building-block-view.md#container-view"]
```

| Decision | Where it lands |
|---|---|
| **[0002 — Central Package Management](adr/guidelines/0002-central-package-management.md)** | Adopted. `Directory.Packages.props` is the version catalog; transitive pinning is deliberately off, for the reason recorded in that file. |
| **[0004 — Result objects](adr/guidelines/0004-result-objects-for-expected-failures.md)** | `Backlog.SharedKernel` implements `Result`, `Result<T>`, and `Error`, and module handlers return them. |
| **[0009 — Feature slices](adr/guidelines/0009-feature-slices-module-structure.md)** | `Backlog.Modules.Tasks` and `Backlog.Modules.Roadmap` use the prescribed layout: `DomainModels/`, `Features/`, the repository port at the module root, `Services/`, `Extensions/`. |
| **[0011 — Centralized styling variables](adr/guidelines/0011-centralized-frontend-styling-variables.md)** | Design tokens live in one file, `src/Core/Backlog.UI.Components/wwwroot/components.css`, and `DesignTokenTests` enforces it. |

## Local system decisions

```meta
related: [".devbook/arc42/04-solution-strategy.md"]
```

`.devbook/arc42/04-solution-strategy.md` frames these product choices, which no
inherited decision covers:

- **Local-first storage**, with one canonical local store per corpus: a SQLite
  database for tasks (local ADR 0003), and markdown for a repository's knowledge
  folders with a generated layer over it (local ADR 0004).
- **Thin cloud, rich desktop** responsibility split.
- **Conflict policy**: the glossary's *Last-write-wins* (`.devbook/arc42/12-glossary.md#terms`).
- **Capture and Inbox kept as one pipeline**, with a possible later split.

A choice that hardens into a governed decision becomes a record under `adr/`
through the delivery engine's `flow-spec`, and is linked here.

## Local ADRs

```meta
related: [".devbook/arc42/adr/README.md", ".devbook/arc42/04-solution-strategy.md"]
```

[adr/README.md](adr/README.md) gives each record's standing choice and status.

- [ADR 0001 — Desktop channel uses .NET MAUI Blazor Hybrid, not plain WinUI 3](adr/0001-desktop-stack-maui-blazor-hybrid.md)
- [ADR 0002 — The Backlog module owns the entry text language](adr/0002-backlog-module-owns-the-entry-text-language.md)
- [ADR 0003 — SQLite is the canonical local task store; markdown is the content](adr/0003-sqlite-is-the-canonical-local-task-store.md)
- [ADR 0004 — One generated local database holds the derived knowledge layer; markdown stays canonical](adr/0004-knowledge-index-is-a-generated-local-database.md)
- [ADR 0005 — An Azure-hosted task replica carries multi-device sync; the local store stays canonical](adr/0005-azure-hosted-task-replica-for-multi-device-sync.md)
- [ADR 0006 — Additive, idempotent bootstrapping is the local store's migration mechanism](adr/0006-additive-schema-bootstrapping-is-the-local-migration-mechanism.md)
- [ADR 0007 — Import reuses the entry text grammar; a plan is multi-task entry text](adr/0007-import-reuses-the-entry-text-grammar.md)
- [ADR 0008 — The Devbook reads from a cached branch snapshot when there is no clone; only a clone is editable](adr/0008-knowledge-reads-from-a-branch-snapshot-when-there-is-no-clone.md)
- [ADR 0009 — Captures are a document kind on the replica; the desktop acknowledges by tombstone](adr/0009-captures-are-a-document-kind-on-the-replica.md)
- [ADR 0010 — A backup is the database committed to a GitHub repository, one way, on a schedule](adr/0010-backup-is-the-database-committed-to-a-repository.md)
- [ADR 0011 — Devbook annotations are the person's data, in a Backlog-owned store, replicated through a third container](adr/0011-devbook-annotations-are-a-third-replica-container.md)
- [ADR 0012 — Backlog is an MCP server hosted inside the running desktop application](adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md)
- [ADR 0013 — An imported plan is one Roadmap Item; a `plan` entry is the same grammar, and the importer places it](adr/0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md)
- [ADR 0014 — Attachments travel through a blob store beside the replica; the sync service is the only door](adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md)
- [ADR 0015 — The devbook database lives in the app's storage, one per repository path, and the app builds it](adr/0015-devbook-database-lives-in-app-storage-and-the-app-builds-it.md)
- [ADR 0016 — The knowledge folders adopt the devbook convention under `.devbook/`; the derived layer stays a local build output](adr/0016-knowledge-folders-adopt-the-devbook-convention.md)
- [ADR 0017 — Inbox import is a capture source; its manifest is Markdown with front matter, not entry text](adr/0017-inbox-import-is-a-capture-source-with-a-markdown-manifest.md)
- [ADR 0018 — The roadmap plan and the planning pace ride the task feed as two whole documents](adr/0018-roadmap-plan-and-pace-ride-the-task-feed.md)
- [ADR 0019 — The roadmap counts the person's working week in hours, date by date, and the week travels with the pace](adr/0019-roadmap-counts-the-working-week.md)
- [ADR 0020 — Items from GitHub and spec-manager arrive as linked tasks, through one connector contract](adr/0020-external-items-arrive-as-linked-tasks.md)
- [ADR 0021 — The GitHub accounts and the repository registry ride the task feed as two whole documents](adr/0021-github-accounts-and-repository-registry-ride-the-task-feed.md)
- [ADR 0022 — The desktop shell shows one main view, picked by a view switch, and takeovers replace it](adr/0022-the-shell-shows-one-main-view-picked-by-a-view-switch.md)
- [ADR 0023 — Inbox items may be read by the Foundry model](adr/0023-inbox-items-may-be-read-by-the-foundry-model.md)
- [ADR 0024 — Claude Code's telemetry arrives as OTLP/HTTP logs on the in-app MCP listener](adr/0024-claude-code-telemetry-arrives-on-the-mcp-listener.md)
- [ADR 0025 — Notes ride the task feed as a second Inbox document kind, synced both ways](adr/0025-notes-ride-the-task-feed.md)
- [ADR 0026 — The Brain is a personal devbook, one folder per user outside any repository](adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md)
- [ADR 0027 — The Brain is a scope beside the repositories, the scope of everything filed against no repository](adr/0027-the-brain-is-a-scope-beside-the-repositories.md)
- [ADR 0028 — Progress is its own boundary, and Tasks knows no sprints](adr/0028-progress-is-its-own-boundary-and-tasks-knows-no-sprints.md) (sprint parts superseded by ADR 0035)
- [ADR 0030 — Every stored credential goes through the OS-protected envelope](adr/0030-stored-credentials-go-through-the-os-protected-envelope.md)
- [ADR 0031 — GitHub calls resolve their account per repository, and a token taken from the GitHub CLI is never stored](adr/0031-github-calls-resolve-their-account-per-repository-and-never-store-a-gh-token.md)
- [ADR 0032 — The My Day preferences ride the task feed as one whole document, and the phone folds it](adr/0032-my-day-preferences-ride-the-task-feed.md)
- [ADR 0033 — The Dashboard sends its figures to Foundry, whichever part was pressed](adr/0033-the-dashboard-sends-its-figures-to-foundry-whichever-part-was-pressed.md)
- [ADR 0034 — Archify renders are generated on demand into app storage, not committed](adr/0034-archify-renders-are-generated-on-demand-not-committed.md)
- [ADR 0035 — Goals and impediments replace sprints](adr/0035-goals-and-impediments-replace-sprints.md)
- [ADR 0037 — Backlog queues session requests and a Manager session dispatches them](adr/0037-backlog-queues-session-requests-and-a-manager-session-dispatches-them.md)
