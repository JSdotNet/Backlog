# Adopted Organization Guidelines

```meta
status: active
related: [".arc42/09-architecture-decisions.md", ".arc42/02-constraints.md#technical-constraints"]
issue: null
```

The organization-level architecture decisions that govern Backlog's .NET code,
imported into this repository on **2026-08-27** and **authoritative here from
that date**.

Until then these decisions were read live from the `jsdotnet-project-guidelines`
MCP server and this repository only linked to them by number. That link is gone:
an agent working in Backlog reads this folder, and nothing has to be fetched to
know what governs the code.

## What these documents are

Each file states a decision the organization took, **trimmed to what applies to
Backlog and grounded in what this repository actually does**. Three sections:

| Section | What it holds |
|---|---|
| **Decision** | The rule, as it binds this repository. |
| **How Backlog applies it** | Where the rule is implemented, named by file or project. |
| **Deviations and gaps** | Where Backlog knowingly differs, and what is not implemented yet. |

The *Deviations and gaps* section is descriptive, not an alignment claim: it
records what is true on the date of the import. A gap is a gap until a change
closes it, not a violation to be argued away.

## Relationship to the other decision records

- **`.arc42/guidelines/`** (here) — decisions Backlog **inherited**. They apply to
  more repositories than this one; the numbering is the organization's.
- **`.arc42/adr/`** — decisions Backlog **took for itself**, in its own 0001-…
  sequence. A local ADR may deliberately override an inherited decision; it says
  so explicitly when it does (see `adr/0002-backlog-module-owns-the-entry-text-language.md`,
  which takes a position between inherited ADRs 0005 and 0009).
- **`.arc42/09-architecture-decisions.md`** — the chapter that links to both and
  says which decision governs which part of the system.

The two numbering sequences are independent. **Inherited ADR 0003** (Aspire) and
**local ADR 0003** (SQLite as the canonical store) are different decisions;
always name the folder when citing one.

## Index

| # | Decision | Governs |
|---|---|---|
| [0001](0001-adopt-dotnet-10.md) | .NET 10 as target framework | every .NET project |
| [0002](0002-central-package-management.md) | Central Package Management | `Directory.Packages.props` |
| [0003](0003-aspire-for-web-services.md) | .NET Aspire orchestration | `src/Aspire/`, every host |
| [0004](0004-result-objects-for-expected-failures.md) | Result objects for expected failures | `Backlog.SharedKernel`, module handlers |
| [0005](0005-modular-monolith-structure.md) | Modular monolith structure | the whole `src/` layout |
| [0006](0006-cqrs-for-api-projects.md) | Lightweight CQRS, no mediator | module feature slices |
| [0007](0007-minimal-apis-over-controllers.md) | Minimal APIs over controllers | the sync service |
| [0009](0009-feature-slices-module-structure.md) | Feature slices inside a module | module project layout |
| [0010](0010-opentelemetry-observability.md) | OpenTelemetry for observability | `ServiceDefaults`, modules |
| [0011](0011-centralized-frontend-styling-variables.md) | Centralized styling variables | `Backlog.UI.Components` |
| [0012](0012-authentication-external-identity-providers.md) | OIDC for external identity | GitHub OAuth, cloud auth |
| [0013](0013-authorization-zero-trust.md) | Zero Trust authorization | sync service, module calls |
| [0014](0014-persistence-and-repository-boundaries.md) | Persistence and repository boundaries | `Backlog.Infrastructure.Sqlite` |
| [0015](0015-resilience-for-outbound-dependencies.md) | Resilience at adapter boundaries | GitHub, Claude, FCM calls |
| [0017](0017-http-error-contract-and-problem-details.md) | Problem Details error contract | the sync API surface |
| [0018](0018-configuration-and-options-binding.md) | Typed options, validated at startup | every host |

Numbers are the organization's and are kept as they were, so **0008** and
**0016** are absent by intent rather than by mistake — see below.

## What was deliberately not imported

| Not imported | Why |
|---|---|
| **ADR 0008 — Vertical Slice Architecture** | Superseded in physical-layout detail by ADR 0009, which is imported. Importing both would put two layouts in one folder. |
| **ADR 0016 — Messaging and Integration-Event Delivery** | Backlog has no asynchronous messaging: sync is a request/response API and cross-module collaboration is in-process. Import it if durable messaging is ever introduced. |
| **ADR 0019 — C# Standalone Script Files** | Repository automation here is PowerShell under `build/` and Node under `.github/tools/`. No `.cs` scripts exist to govern. |
| **Recommendations** (C# coding style, testing unit/integration/e2e/architecture, object calisthenics, validation strategy, logging and audit logging, caching, API versioning, idempotency, background jobs, Blazor guidance, CI/CD, database migrations, feature flags, Aspire start script, Copilot instruction setup, agent skill authoring) | These are coding and process conventions rather than architecture decisions. They bite while editing code, so their home is an instruction file, not an architecture chapter. Held for a separate pass — see `.arc42/11-risks-and-technical-debt.md`. |
| **Designs** (modular monolith architecture, pragmatic DDD, modular solution structure) | Intent and trade-off essays behind ADRs 0005/0009/0014, which are imported. The bounded-context modelling they describe is already carried by `.domain/`. |
| **Structures** (feature-slices scaffold, folder-structure reference, minimal-API endpoint organization, simple solution structure) | Copy-paste scaffolds whose binding rules are already in ADRs 0005 and 0009. |
| **Design and UX style guides** | Already materialized, product-specific, in `.design/`. That folder is authoritative for design; it is not duplicated here. |

## Changing an inherited decision

These files are a **fork, not a mirror**. There is no sync job and no upstream
read at session start.

- To record that Backlog does something different, edit the *Deviations and gaps*
  section of the file, or write a local ADR in `.arc42/adr/` when the divergence
  is a decision in its own right.
- To adopt a decision that was not imported, add it here with its organization
  number and delete its row from the table above.
- The upstream corpus (`JSdotNet/Project-Guidelines-MCP`, `guide/`) stays the
  organization's copy. Re-reading it is a deliberate act, not a default.
