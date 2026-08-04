# Dependencies: Technology Stack

```meta
status: draft
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Dev PC Management](../dev-pc-management/domain.md#aggregate-machine-registry) | Customer/Supplier (Technology Stack = customer of reports) | Consumes tool-version reports | `.domain/dev-pc-management/domain.md#aggregate-machine-registry` | Machine tool inventories provide adoption counts and version distribution. |
| [Repository Management](../repository-management/domain.md#aggregate-repository-registry) | Customer/Supplier (Technology Stack = customer of scans) | Consumes tech-stack scans | `.domain/repository-management/domain.md#domain-service-repository-scan` | Repository tech stacks provide adoption data. |
| Public package registries (npm, NuGet, PyPI) | ACL | Polling | `.domain/technology-stack/domain.md#domain-service-adoption-tracking` | Latest available version information for each technology. |
| [Second Brain](../second-brain/domain.md#aggregate-knowledge-note) | Customer/Supplier (Technology Stack = customer) | ADR links by id | `.domain/second-brain/domain.md#aggregate-knowledge-note` | Technology decisions link to ADRs for justification and history. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Dev PC Management](../dev-pc-management/domain.md#aggregate-machine-registry) | Customer/Supplier (Technology Stack = supplier) | `BaselineRequested` -> `BaselineProvided` sync contract | `.domain/technology-stack/domain.md#domain-service-deprecation-management` | Consumes the team tool baseline to compute machine compliance. |
| [Repository Management](../repository-management/domain.md#aggregate-repository-registry) | Customer/Supplier (Technology Stack = supplier) | Baseline consumption (sync/read) | `.domain/technology-stack/domain.md#aggregate-technology-registry` | Consumes tech baselines to flag deprecated tech and enforce versions. |
| [Monitoring](../monitoring/domain.md#aggregate-progress-signal) | Customer/Supplier (Monitoring = customer) | Adoption metrics feed | `.domain/technology-stack/domain.md#domain-service-adoption-tracking` | Portfolio adoption trends and tech trends feed dashboards. |
| [Backlog](../backlog/domain.md#aggregate-backlog-entry) | Customer/Supplier (Backlog = customer) | Recommendation -> task creation | `.domain/technology-stack/domain.md#domain-service-deprecation-management` | Adoption gaps generate tech-upgrade backlog items. |

## Notes

- Technology Stack is the authoritative baseline owner; Dev PC and Repository
  Management both consume its baselines and report adoption back.
- External registry lookups sit behind an anti-corruption adapter so registry
  formats never leak into the `Technology` model.
