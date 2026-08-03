# Dependencies: Technology Stack

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the integration pattern for each
> relationship (synchronous call, domain/integration event, shared kernel,
> anti-corruption layer, etc.).

## Outbound dependencies

| Depends on (context/module) | Integration pattern | Why |
|---|---|---|
| [Dev PC Management](../dev-pc-management/domain.md#aggregate-machine-registry) | Consumes tool-version reports (inbound data) | Machine tool inventories provide adoption counts and version distribution. |
| [Repository Management](../repository-management/domain.md#aggregate-repository-registry) | Consumes tech-stack scans (inbound data) | Repository tech stacks provide adoption data. |
| Public package registries (npm, NuGet, PyPI) | Polling (ACL) | Latest available version information for each technology. |
| [Second Brain](../second-brain/domain.md#aggregate-knowledge-note) | Id reference (ADR links) | Technology decisions link to ADRs for justification and history. |

## Inbound dependents (known)

| Consumer (context/module) | Integration pattern | What it relies on |
|---|---|---|
| [Dev PC Management](../dev-pc-management/domain.md#aggregate-machine-registry) | `BaselineRequested` → `BaselineProvided` (sync) | Consumes the team tool baseline to compute machine compliance. |
| [Repository Management](../repository-management/domain.md#aggregate-repository-registry) | Baseline consumption (sync/read) | Consumes tech baselines to flag deprecated tech and enforce versions. |
| [Monitoring](../monitoring/domain.md#aggregate-progress-signal) | Adoption metrics feed (read) | Portfolio adoption trends and tech trends feed dashboards. |
| [Backlog](../backlog/domain.md#aggregate-backlog-entry) | Recommendation → task creation | Adoption gaps generate tech-upgrade backlog items. |

## Notes

- Technology Stack is the authoritative baseline owner; Dev PC and Repository
  Management both consume its baselines and report adoption back — a
  supplier/customer relationship in both directions.
- External registry lookups sit behind an anti-corruption adapter so registry
  formats never leak into the `Technology` model.
