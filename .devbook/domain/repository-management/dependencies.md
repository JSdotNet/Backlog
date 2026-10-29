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
| [Technology Stack](../technology-stack/domain.md#technology-registry) | Customer/Supplier (Repository Management = customer) | Baseline consumption (sync/read) | `.devbook/domain/technology-stack/domain.md#technology-registry` | Consumes tech/dependency baselines to flag deprecated tech and enforce versions. |
| GitHub (external) | ACL | REST API | `.devbook/domain/repository-management/domain.md#repository-scan` | Repository metadata, issues, PRs, branch protection, and security alerts. |
| Package registries (NuGet, npm, PyPI, etc.) | ACL | Polling | `.devbook/domain/repository-management/domain.md#repository-scan` | Latest package versions used to detect outdated dependencies. |
| [Devbook](../devbook/context.md) | Customer/Supplier (Repository Management = customer) | ADR cross-link by id | `.devbook/domain/devbook/context.md` | Architecture/technology decisions are cross-linked from repo metadata. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Technology Stack](../technology-stack/domain.md#technology-registry) | Customer/Supplier (Repository Management = supplier) | Consumes tech-stack scans | `.devbook/domain/repository-management/domain.md#repository-scan` | Relies on per-repo technology snapshots for portfolio adoption. |
| [Monitoring](../monitoring/domain.md#progress-signal) | Customer/Supplier (Repository Management = supplier) | Subscribes to health/freshness feed | `.devbook/domain/repository-management/domain.md#repository-registry` | Relies on health scores, package freshness, and issue backlog for dashboards. |
| [Tasks](../tasks/domain.md#task) | Customer/Supplier (Repository Management = supplier) | Recommendation -> task creation | `.devbook/domain/repository-management/domain.md#repository-registry` | Low-health repos generate tasks for updates/cleanup. |
| [Dev PC Management](../dev-pc-management/domain.md#machine-registry) | Customer/Supplier (Repository Management = supplier) | Shared repo registry (`config/repos.json`) | `.devbook/domain/repository-management/domain.md#repository` | Aligns registered repos with local clone paths for developer workflows. |
| [Tasks](../tasks/features.md#search-filter-and-organize) | Customer/Supplier (Repository Management = supplier) | Reads the archived flag from the registry | `.devbook/domain/repository-management/domain.md#archived-repository` | Hides an archived repository from the scope, the Board's columns and the pickers, and pauses the Connected Target filed under it. |
| [Roadmap](../roadmap/features.md#reading-the-plan-by-repository) | Customer/Supplier (Repository Management = supplier) | Reads the archived flag from the registry | `.devbook/domain/repository-management/domain.md#archived-repository` | Greys and tags an archived repository's band, and hides its open work. |
| [Devbook](../devbook/features.md#area-selection-and-scope) | Customer/Supplier (Repository Management = supplier) | Reads the archived flag from the registry | `.devbook/domain/repository-management/domain.md#archived-repository` | Opens an archived repository's devbook read-only and fetches no snapshot of it on its own. |
| [Sessions](../sessions/features.md#pull-request-readiness) | Customer/Supplier (Repository Management = supplier) | Reads the archived flag from the registry | `.devbook/domain/repository-management/domain.md#archived-repository` | Leaves an archived repository out of the Pull requests pane's and the In progress view's GitHub reads. |
| [Productivity](../productivity/features.md#personal-productivity-dashboard) | Customer/Supplier (Repository Management = supplier) | Reads the archived flag from the registry | `.devbook/domain/repository-management/domain.md#archived-repository` | Leaves an archived repository out of the dashboard's GitHub activity, and still counts its finished work. |
| [Progress](../progress/features.md#pull-request-status) | Customer/Supplier (Repository Management = supplier) | Reads the archived flag from the registry | `.devbook/domain/repository-management/domain.md#archived-repository` | Leaves an archived repository out of the Goals view's pull request reads. |

## Notes

- The repository registry aligns with the workspace `config/repos.json` registry
  so `repo_id` resolves consistently across Tasks, Dev PC Management, and
  Repository Management.
- GitHub and package-registry access sit behind anti-corruption adapters so
  external schemas never leak into the `Repository` model.
- Technology baseline authority stays with Technology Stack; this context only
  consumes baselines and reports adoption back.
