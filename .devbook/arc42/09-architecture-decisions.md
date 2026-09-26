# 09. Architecture Decisions

```meta
status: active
```

This chapter links to the decision records rather than restating them. There are
two sets, and they are numbered independently:

- **Inherited decisions** live in `.devbook/arc42/adr/guidelines/` — the organization's ADRs,
  imported into this repository on 2026-08-27 and authoritative here since. They
  were previously read from the `jsdotnet-project-guidelines` MCP server; nothing
  is fetched at read time any more.
- **Local decisions** live in `.devbook/arc42/adr/` — decisions Prompt Backlog took for
  itself, in their own sequence.

Both sequences start at 0001, so always name the folder when citing one:
*guidelines 0003* is Aspire, *local ADR 0003* is SQLite.

The **sync service** is the part of Prompt Backlog that the ASP.NET-specific
inherited decisions govern directly, but it is not the only governed code: the
modules, the shared kernel, and the shared component library are .NET too (see
*Beyond the sync service* below). The IDE channel is TypeScript and stays outside
the .NET decision set.

## Sync-service alignments

```meta
status: active
related: [".devbook/arc42/adr/guidelines/README.md", ".devbook/arc42/05-building-block-view.md#cloud-service", ".devbook/arc42/07-deployment-view.md#cloud-deployment-azure"]
```

The *alignment* column is a reading of intent per decision, not a compliance
audit of implemented code. Each linked document carries its own **Deviations and
gaps** section, which is the accurate account.

An inherited decision marked `proposed` rather than `active` is one no shipped
code applies yet — 0012, 0013, 0017, and 0018 are all in that state. It still
binds the first work that reaches its ground.

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
status: active
related: [".devbook/arc42/adr/guidelines/README.md", ".devbook/arc42/05-building-block-view.md#container-view"]
```

Inherited decisions that govern shipped code outside the sync service.

| Decision | Where it lands |
|---|---|
| **[0002 — Central Package Management](adr/guidelines/0002-central-package-management.md)** | Adopted. `Directory.Packages.props` is the version catalog; transitive pinning is deliberately off, for the reason recorded in that file. |
| **[0004 — Result objects](adr/guidelines/0004-result-objects-for-expected-failures.md)** | `Backlog.SharedKernel` implements `Result`, `Result<T>`, and `Error`, and module handlers return them. |
| **[0009 — Feature slices](adr/guidelines/0009-feature-slices-module-structure.md)** | `Backlog.Modules.Tasks` and `Backlog.Modules.Roadmap` use the prescribed layout: `DomainModels/`, `Features/`, the repository port at the module root, `Services/`, `Extensions/`. |
| **[0011 — Centralized styling variables](adr/guidelines/0011-centralized-frontend-styling-variables.md)** | Design tokens live in one file, `src/Core/Backlog.UI.Components/wwwroot/components.css`, and `DesignTokenTests` enforces it. |

## Local system decisions

```meta
status: active
related: [".devbook/arc42/04-solution-strategy.md"]
```

Decisions specific to Prompt Backlog that no inherited decision covers are
captured as solution strategy in `.devbook/arc42/04-solution-strategy.md`:

- **Local-first storage**, with one canonical local store per corpus: a SQLite database
  for tasks (ADR 0003), and markdown for a repository's knowledge folders with a
  generated layer over it (ADR 0004).
- **Thin cloud, rich desktop** responsibility split.
- **Conflict policy**: new items always create; edits are last-write-wins.
- **Capture/Inbox kept as one pipeline** for now, with a possible future split.

If any of these harden into formally governed decisions, promote them to ADRs via
the `orch-adr` skill and link them here rather than duplicating the content.

## Local ADRs

```meta
status: active
related: [".devbook/arc42/04-solution-strategy.md"]
```

- **[ADR 0001 — Desktop channel uses .NET MAUI Blazor Hybrid, not plain WinUI 3](adr/0001-desktop-stack-maui-blazor-hybrid.md)**:
  supersedes the original WinUI 3 desktop choice so the desktop client can be
  launched from an Aspire AppHost and tested end-to-end with Playwright, while
  preserving local-first storage and native background-worker constraints.
- **[ADR 0002 — The Backlog module owns the entry text language](adr/0002-backlog-module-owns-the-entry-text-language.md)**:
  moves the entry text format and every use case over an entry out of the desktop
  client and into the module, behind a published Abstractions surface, so a second
  client cannot reimplement the format and no caller can bypass an invariant. It
  takes a deliberate position between inherited decisions 0005 and 0009.
- **[ADR 0003 — SQLite is the canonical local task store; markdown is the content](adr/0003-sqlite-is-the-canonical-local-task-store.md)**:
  replaces one-markdown-file-per-task plus its derived JSON index and order sidecar
  with a single local SQLite database, so no two files can disagree about a task,
  while keeping a task's content as markdown and leaving the published entry text
  language untouched. Amended on 2026-09-05: the record originally gave the roadmap
  plan as its counter-example, a document that stayed JSON on disk. The plan is a
  `roadmap_plan` row in the same database now — folded in for local ADR 0005's
  file-sync hazard, not for anything the original reasoning got wrong.
- **[ADR 0004 — One generated local database holds the derived knowledge layer; markdown stays canonical](adr/0004-knowledge-index-is-a-generated-local-database.md)**
  *(proposed)*: replaces the committed `_meta/*.json` indexes with a single
  generated, uncommitted SQLite database per knowledge repository, so branches stop
  conflicting on files nobody authored and every channel — desktop, mobile, IDE, a
  future MCP server — reads one schema instead of carrying its own markdown parser.
  Extends local ADR 0003 to a second corpus without making a database canonical for
  knowledge.
- **[ADR 0005 — An Azure-hosted task replica carries multi-device sync; the local store stays canonical](adr/0005-azure-hosted-task-replica-for-multi-device-sync.md)**
  *(accepted)*: answers the question local ADR 0003 did not ask — what happens when
  one person runs the desktop on two machines. A serverless Cosmos DB account with
  two replica containers, `tasks` and `sessions`, plus `devices` and
  `pairingCodes` for the registry, and the existing sync service carry a
  replica and the change feed over it; each device's SQLite database stays canonical
  for that device. Amends local ADR 0003 without superseding it, and replaces
  file-syncing the database — which produced six conflicted copies and silent
  data loss — with a store built for concurrent writers. Three kinds of state:
  the Task aggregate reconciled last-write-wins, session records that are
  single-writer and therefore reconcile not at all, and the phone's captures, which
  arrive as task documents. Retention is container TTL rather than code — 180 days
  for task tombstones, 12 months for session records. The per-owner boundary is
  enforced by the sync service scoping every query to the `ownerId` in the device's
  JWT, not by the partition key: the managed identity is account-scoped and can see
  every partition.
  Also settles the local file name: it stays `backlog.db` on every device, because
  a per-device name trades a visible failure for two silently divergent backlogs.
  Detecting a root inside a sync provider's folder is the named mitigation
  instead. Amended on 2026-09-16: the external-change poll that record leaned on
  for cross-device freshness is retired — the Storage tab warns against the shared
  folder it watched, and a sync pull now reloads the Tasks pane directly.
- **[ADR 0006 — Additive, idempotent bootstrapping is the local store's migration mechanism](adr/0006-additive-schema-bootstrapping-is-the-local-migration-mechanism.md)**
  *(proposed)*: names the mechanism the SQLite adapter has been using unlabelled for
  three schema changes — additive `ALTER TABLE` guarded by `PRAGMA table_info`, plus
  seeding and retired-value `UPDATE`s that are idempotent by construction rather than
  by bookkeeping — permits exactly three shapes, forbids everything non-additive, and
  makes the first non-additive change the trigger for a versioned mechanism. Written
  to discharge D3 before local ADR 0005's `updated_at` and `deleted_at` reached a
  table holding live user data. Since 2026-09-05 it reaches the roadmap plan too,
  which it explicitly did not when written: *creating* the `roadmap_plan` table falls
  under ADR 0003's idempotent `IF NOT EXISTS` DDL rather than one of the three
  column-level shapes, but any column added to it later is this record's business.
- **[ADR 0007 — Import reuses the entry text grammar; a plan is multi-task entry text](adr/0007-import-reuses-the-entry-text-grammar.md)**
  *(accepted)*: an import plan is entry text with more than one `#`-titled entry in it,
  not a format of its own, so `EntryTextParser` stays the only grammar the product has
  to parse and a plan stays hand-editable. Upload and paste feed one path, and `after:`
  across a fresh batch is resolved by Import in two passes rather than by the parser.
- **[ADR 0008 — The Devbook reads from a cached branch snapshot when there is no clone; only a clone is editable](adr/0008-knowledge-reads-from-a-branch-snapshot-when-there-is-no-clone.md)**
  *(proposed)*: a repository nobody has cloned is read from a cached snapshot of its
  branch, fetched file by file as readers ask, and never edited there. Amended on
  2026-09-16: the snapshot cache defaults under the storage folder rather than
  beside the per-user settings, so a backlog moved to another disk takes it along;
  the override stays.
- **[ADR 0009 — Captures are a document kind on the replica; the desktop acknowledges by tombstone](adr/0009-captures-are-a-document-kind-on-the-replica.md)**
  *(accepted)*: a phone capture is a task-shaped document with its own kind token in
  the `tasks` container, and routing it on the desktop tombstones it there.
- **[ADR 0010 — A backup is the database committed to a GitHub repository, one way, on a schedule](adr/0010-backup-is-the-database-committed-to-a-repository.md)**
  *(accepted)*: one consistent copy of `backlog.db`, taken through SQLite's backup
  API, replaces `backlog/backlog.db` on the named repository's default branch on a
  daily or weekly schedule or on demand; an unchanged database makes no commit,
  and nothing ever reads the repository back. The database and only the database,
  because local ADR 0003 makes that the whole of the backlog.
- **[ADR 0011 — Devbook annotations are the person's data, in a Backlog-owned store, replicated through a third container](adr/0011-devbook-annotations-are-a-third-replica-container.md)**
  *(accepted)*: a remark left on a Devbook chapter lives in one JSON file per
  repository under the storage folder — never in the repository's generated
  `_meta/devbook.db`, never in `backlog.db` — behind a port the panels and
  replication share, and travels between the person's desktops through a third
  replica container, `annotations`, on the task container's last-write-wins and
  tombstone terms.
- **[ADR 0012 — Backlog is an MCP server hosted inside the running desktop application](adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md)**
  *(accepted, not yet built)*: an AI session's tool surface over Backlog is a
  Streamable HTTP endpoint the desktop app itself listens on — loopback, a
  configurable port, an `Origin` check and a bearer token — registered as
  `backlog` everywhere, mapped by the web harness too, and never a standalone
  stdio host. Every scoped call carries the repository as `owner/name`; a status
  move is a rewrite of the entry's status token through the existing save; the
  private reading note is the inbox a session reads and resolves, and the answer
  is a devbook fence the session writes itself. Tool groups follow the feature
  flags of the areas they serve.
- **[ADR 0013 — An imported plan is one Roadmap Item; a `plan` entry is the same grammar, and the importer places it](adr/0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md)**
  *(accepted)*: an imported plan is represented on the roadmap by one item whose
  tag is the plan's shared tag with the `+` sigil lifted — `import_plan_id` keeps
  the sigil, the item holds the bare slug, and the existing gather-by-tag joins
  them. A roadmap-level entry is the entry grammar with the type word `plan`,
  handed from Tasks' Import to Roadmap through a port and adapter; the importer
  places the window (end from `due:`, start after the predecessors, length from
  gathered effort over a person-owned velocity) and never overrides a window a
  person moved; an item expands on the timeline into the tasks it gathers, sized
  by effort, with progress read from Tasks and never stored.
- **[ADR 0014 — Attachments travel through a blob store beside the replica; the sync service is the only door](adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md)**
  *(proposed)*: a phone capture's files — pictures, PDFs, documents — go into one
  Azure Storage container beside the Cosmos replica, keyed by owner and
  attachment id, and every byte passes through the sync service's owner-scoped,
  size-capped, type-checked `PUT`/`GET` rather than a SAS URL on the device. The
  capture document carries metadata only and is posted after its files; the
  acknowledgement tombstone releases them and a 30-day lifecycle rule is the
  backstop. Task attachments stay machine-local.
