# 09. Architecture Decisions

```meta
status: active
```

This chapter links to the decision records rather than restating them. There are
two sets, and they are numbered independently:

- **Inherited decisions** live in `.arc42/guidelines/` — the organization's ADRs,
  imported into this repository on 2026-08-27 and authoritative here since. They
  were previously read from the `jsdotnet-project-guidelines` MCP server; nothing
  is fetched at read time any more.
- **Local decisions** live in `.arc42/adr/` — decisions Prompt Backlog took for
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
related: [".arc42/guidelines/README.md", ".arc42/05-building-block-view.md#cloud-service", ".arc42/07-deployment-view.md#cloud-deployment-azure"]
```

The *alignment* column is a reading of intent per decision, not a compliance
audit of implemented code. Each linked document carries its own **Deviations and
gaps** section, which is the accurate account.

| Decision | Current alignment for the sync service |
|---|---|
| **[0001 — .NET 10](guidelines/0001-adopt-dotnet-10.md)** | Target framework of `Backlog.Modules.Sync.Api`. |
| **[0003 — .NET Aspire](guidelines/0003-aspire-for-web-services.md)** | Followed: the service is an Aspire resource (`sync`) and calls `AddServiceDefaults()`. |
| **[0005 — Modular monolith structure](guidelines/0005-modular-monolith-structure.md)** | Followed: the service is the `Sync` module's own `.Api` project under `src/Modules/Sync/`. |
| **[0006 — CQRS](guidelines/0006-cqrs-for-api-projects.md)** | Already followed elsewhere: `Backlog.SharedKernel.Handlers` declares `ICommandHandler`/`IQueryHandler` once, with no mediator. Applies to the sync service when its endpoints grow past the current in-memory store. |
| **[0007 — Minimal APIs](guidelines/0007-minimal-apis-over-controllers.md)** | Followed in style: `MapGroup` plus `Results` helpers, no controllers. OpenAPI and Scalar are not wired up. |
| **[0010 — OpenTelemetry](guidelines/0010-opentelemetry-observability.md)** | Wired through ServiceDefaults; no service-specific activities or metrics. |
| **[0012 — External identity (OIDC)](guidelines/0012-authentication-external-identity-providers.md)** | Relevant only for the GitHub OAuth callback and any future external identity flow, not for device-session auth. |
| **[0013 — Authorization & Zero Trust](guidelines/0013-authorization-zero-trust.md)** | Relevant for device authorization, least-privilege checks, and audit logging. None implemented — the baseline is single-user. |
| **[0014 — Persistence & repository boundaries](guidelines/0014-persistence-and-repository-boundaries.md)** | Relevant for sync-state persistence and data ownership boundaries. |
| **[0015 — Resilience](guidelines/0015-resilience-for-outbound-dependencies.md)** | Relevant for GitHub and push-delivery outbound calls. |
| **[0017 — Problem Details](guidelines/0017-http-error-contract-and-problem-details.md)** | The expected error contract for the sync API surface. Not implemented. |
| **[0018 — Configuration & options](guidelines/0018-configuration-and-options-binding.md)** | Relevant for strongly typed settings and externalized secrets. |

## Beyond the sync service

```meta
status: active
related: [".arc42/guidelines/README.md", ".arc42/05-building-block-view.md#container-view"]
```

Inherited decisions that govern shipped code outside the sync service.

| Decision | Where it lands |
|---|---|
| **[0002 — Central Package Management](guidelines/0002-central-package-management.md)** | Adopted. `Directory.Packages.props` is the version catalog; transitive pinning is deliberately off, for the reason recorded in that file. |
| **[0004 — Result objects](guidelines/0004-result-objects-for-expected-failures.md)** | `Backlog.SharedKernel` implements `Result`, `Result<T>`, and `Error`, and module handlers return them. |
| **[0009 — Feature slices](guidelines/0009-feature-slices-module-structure.md)** | `Backlog.Modules.Backlog` and `Backlog.Modules.Roadmap` use the prescribed layout: `DomainModels/`, `Features/`, the repository port at the module root, `Services/`, `Extensions/`. |
| **[0011 — Centralized styling variables](guidelines/0011-centralized-frontend-styling-variables.md)** | Design tokens live in one file, `src/Core/Backlog.UI.Components/wwwroot/components.css`, and `DesignTokenTests` enforces it. |

## Local system decisions

```meta
status: active
related: [".arc42/04-solution-strategy.md"]
```

Decisions specific to Prompt Backlog that no inherited decision covers are
captured as solution strategy in `.arc42/04-solution-strategy.md`:

- **Local-first, markdown-canonical** storage with JSON files as derived indexes and metadata.
- **Thin cloud, rich desktop** responsibility split.
- **Conflict policy**: new items always create; edits are last-write-wins.
- **Capture/Inbox kept as one pipeline** for now, with a possible future split.

If any of these harden into formally governed decisions, promote them to ADRs via
the `orch-adr` skill and link them here rather than duplicating the content.

## Local ADRs

```meta
status: active
related: [".arc42/04-solution-strategy.md"]
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
  language untouched.
