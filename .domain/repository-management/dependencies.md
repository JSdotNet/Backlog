# Dependencies: Repository Management

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the integration pattern for each
> relationship (synchronous call, domain/integration event, shared kernel,
> anti-corruption layer, etc.).

## Outbound dependencies

| Depends on (context/module) | Integration pattern | Why |
|---|---|---|
| [Technology Stack](../technology-stack/domain.md#aggregate-technology-registry) | Baseline consumption (sync/read) | Consumes tech/dependency baselines to flag deprecated tech and enforce versions. |
| GitHub (external) | REST API (ACL) | Repository metadata, issues, PRs, branch protection, and security alerts. |
| Package registries (NuGet, npm, PyPI, etc.) | Polling (ACL) | Latest package versions used to detect outdated dependencies. |
| [Second Brain](../second-brain/domain.md#aggregate-knowledge-note) | Id reference (ADR links) | Architecture/technology decisions are cross-linked from repo metadata. |

## Inbound dependents (known)

| Consumer (context/module) | Integration pattern | What it relies on |
|---|---|---|
| [Technology Stack](../technology-stack/domain.md#aggregate-technology-registry) | Consumes tech-stack scans | Relies on per-repo technology snapshots for portfolio adoption. |
| [Monitoring](../monitoring/domain.md#aggregate-progress-signal) | Subscribes to health/freshness signals | Relies on health scores, package freshness, and issue backlog for dashboards. |
| [Backlog](../backlog/domain.md#aggregate-backlog-entry) | Recommendation → task creation | Low-health repos generate backlog items for updates/cleanup. |
| [Dev PC Management](../dev-pc-management/domain.md#aggregate-machine-registry) | Shared repo registry (`config/repos.json`) | Aligns registered repos with local clone paths for developer workflows. |

## Notes

- The repository registry aligns with the workspace `config/repos.json` registry
  so `repo_id` resolves consistently across Backlog, Dev PC Management, and
  Repository Management.
- GitHub and package-registry access sit behind anti-corruption adapters so
  external schemas never leak into the `Repository` model.
- Technology baseline authority stays with Technology Stack; this context only
  consumes baselines and reports adoption back.
