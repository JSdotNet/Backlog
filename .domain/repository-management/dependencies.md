# Repository Management

```meta
type: dependencies
status: draft
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Technology Stack](../technology-stack/domain.md#technology-registry) | Customer/Supplier (Repository Management = customer) | Baseline consumption (sync/read) | `.domain/technology-stack/domain.md#technology-registry` | Consumes tech/dependency baselines to flag deprecated tech and enforce versions. |
| GitHub (external) | ACL | REST API | `.domain/repository-management/domain.md#repository-scan` | Repository metadata, issues, PRs, branch protection, and security alerts. |
| Package registries (NuGet, npm, PyPI, etc.) | ACL | Polling | `.domain/repository-management/domain.md#repository-scan` | Latest package versions used to detect outdated dependencies. |
| [Second Brain](../second-brain/domain.md#knowledge-note) | Customer/Supplier (Repository Management = customer) | ADR cross-link by id | `.domain/second-brain/domain.md#knowledge-note` | Architecture/technology decisions are cross-linked from repo metadata. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Technology Stack](../technology-stack/domain.md#technology-registry) | Customer/Supplier (Repository Management = supplier) | Consumes tech-stack scans | `.domain/repository-management/domain.md#repository-scan` | Relies on per-repo technology snapshots for portfolio adoption. |
| [Monitoring](../monitoring/domain.md#progress-signal) | Customer/Supplier (Repository Management = supplier) | Subscribes to health/freshness feed | `.domain/repository-management/domain.md#repository-registry` | Relies on health scores, package freshness, and issue backlog for dashboards. |
| [Backlog](../backlog/domain.md#backlog-entry) | Customer/Supplier (Repository Management = supplier) | Recommendation -> task creation | `.domain/repository-management/domain.md#repository-registry` | Low-health repos generate backlog items for updates/cleanup. |
| [Dev PC Management](../dev-pc-management/domain.md#machine-registry) | Customer/Supplier (Repository Management = supplier) | Shared repo registry (`config/repos.json`) | `.domain/repository-management/naming.md#repository` | Aligns registered repos with local clone paths for developer workflows. |

## Notes

- The repository registry aligns with the workspace `config/repos.json` registry
  so `repo_id` resolves consistently across Backlog, Dev PC Management, and
  Repository Management.
- GitHub and package-registry access sit behind anti-corruption adapters so
  external schemas never leak into the `Repository` model.
- Technology baseline authority stays with Technology Stack; this context only
  consumes baselines and reports adoption back.
