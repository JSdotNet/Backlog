# 09. Architecture Decisions

```meta
status: active
```

This chapter links to the authoritative Architecture Decision Records rather than
restating them. ADRs are maintained in the organization's guidance corpus
(`jsdotnet-project-guidelines`). The **sync service** is the part of Prompt Backlog
that the ASP.NET-specific ADRs govern directly, but it is not the only governed
code: the modules, the shared kernel, and the shared component library are .NET too
(see *Beyond the sync service* below). The IDE channel is TypeScript and stays
outside the .NET ADR set.

## Sync-service ADR alignments

```meta
status: active
related: [".arc42/05-building-block-view.md#cloud-service", ".arc42/07-deployment-view.md#cloud-deployment-azure"]
```

ADR numbers and titles below were verified against the `jsdotnet-project-guidelines`
corpus on 2026-08-17. The *alignment* column is still a reading of intent per ADR,
not a compliance audit of implemented code.

| ADR | Current alignment for the sync service |
|---|---|
| **0001 - Adopt .NET 10** | Target framework of `Backlog.Modules.Sync.Api`. |
| **0003 - .NET Aspire for ASP.NET** | Followed: the service is an Aspire resource (`sync`) and calls `AddServiceDefaults()`. |
| **0005 - Modular Monolith Structure** | Followed: the service is the `Sync` module's own `.Api` project under `src/Modules/Sync/`, per the modular solution structure. |
| **0006 - CQRS for ASP.NET API** | Already followed elsewhere: `Backlog.SharedKernel.Handlers` declares `ICommandHandler`/`IQueryHandler` once, with no mediator. Applies to the sync service when its endpoints grow past the current in-memory store. |
| **0007 - Minimal APIs over Controllers** | Expected endpoint style for sync, webhook, and PC-registry APIs. |
| **0010 - OpenTelemetry Observability** | Relevant for traces, metrics, and logs in the sync service. |
| **0012 - Authentication with External Identity Providers (OIDC)** | Relevant only for the GitHub OAuth callback / any future external identity flow, not for device-session auth. |
| **0013 - Authorization & Zero Trust** | Relevant for device/team authorization, least-privilege checks, and audit logging. |
| **0014 - Persistence Strategy & Repository Boundaries** | Relevant for sync-state persistence and data ownership boundaries. |
| **0015 - Resilience for Outbound Dependencies** | Relevant for GitHub and FCM outbound calls. |
| **0016 - Messaging & Integration-Event Delivery** | Relevant only if webhook forwarding or sync delivery is implemented with durable asynchronous messaging. |
| **0017 - HTTP Error Contract & Problem Details** | Expected error contract for the sync API surface. |
| **0018 - Configuration & Options Binding** | Relevant for strongly typed settings and externalized secrets. |

## Beyond the sync service

```meta
status: draft
related: [".arc42/05-building-block-view.md#container-view"]
```

Three ADRs already govern shipped code outside the sync service, and one is not
adopted at all. This list records the gap; it is not an alignment claim.

| ADR | Where it lands, and what is unrecorded |
|---|---|
| **0004 - Result Objects for Expected Failures** | `Backlog.SharedKernel` implements `Result`, `Result<T>`, and `Error`, and every module handler returns them. No alignment statement exists. |
| **0009 - Feature Slices Within Module Projects** | `Backlog.Modules.Backlog` already uses the prescribed layout (`DomainModels/`, `Features/`, repository interface at the module root, `Services/`, `Extensions/`). Never recorded as a decision. |
| **0011 - Centralized Frontend Styling Variables** | Design tokens live in one file, `src/UI/Backlog.UI.Components/wwwroot/components.css`, and `DesignTokenTests` enforces it. Never recorded as a decision. |
| **0002 - Central Package Management** | **Not adopted.** There is no `Directory.Packages.props`; package versions are declared per project. Either adopt it or record why not. |

## Local system decisions

```meta
status: active
related: [".arc42/04-solution-strategy.md"]
```

Decisions specific to Prompt Backlog that are not covered by an org-level ADR are
captured as solution strategy in `.arc42/04-solution-strategy.md`:

- **Local-first, markdown-canonical** storage with JSON files as derived indexes and metadata.
- **Thin cloud, rich desktop** responsibility split.
- **Conflict policy**: new items always create; edits are last-write-wins.
- **Capture/Inbox kept as one pipeline** for now, with a possible future split.

If any of these harden into formally governed decisions, promote them to ADRs via the
`orch-adr` skill and link them here rather than duplicating the content.

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
  client cannot reimplement the format and no caller can bypass an invariant.


