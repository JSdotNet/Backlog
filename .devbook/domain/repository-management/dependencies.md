# Repository Management

```meta
type: dependencies
status: draft
related: [.devbook/domain/context-map.md, .devbook/arc42/adr/0042-repository-management-owns-pull-request-state.md]
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Technology Stack](../technology-stack/domain.md#technology-registry) | Customer/Supplier (Repository Management = customer) | Baseline consumption (sync/read) | `.devbook/domain/technology-stack/domain.md#technology-registry` | Consumes tech/dependency baselines to flag deprecated tech and enforce versions. |
| GitHub (external) | ACL | REST API | `.devbook/domain/repository-management/domain.md#repository-scan` | Repository metadata, issues, PRs, branch protection, and security alerts. |
| GitHub (external), for pull request state | ACL | `Backlog.Infrastructure.GitHub`, under the account each repository resolves to (local ADR 0031) | `.devbook/domain/repository-management/domain.md#pull-request-status`, `.devbook/domain/repository-management/domain.md#merge-command` | Reads each open pull request with its checks, reviews and base branch rules, and sends the merge commands. GitHub's answer is translated into the one status and kept in one cache with the freshness rules of local [ADR 0042 §3](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#3-one-set-of-commands-and-one-cache); no GitHub shape reaches a consumer. Archived repositories are skipped here, once, for every reader. **Not built.** Today the shell's pull requests pane, Tasks' screen and Progress's adapter each call the GitHub adapter themselves. |
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
| [Sessions](../sessions/features.md#the-detail-of-one-session) | Customer/Supplier (Repository Management = supplier) | The desktop shell composes the session list's pull request card and asks this context's contract for its verdict. A task's side panel reads its linked pull requests through the same cache. Sessions references nothing of this context. | `.devbook/domain/repository-management/domain.md#readiness-verdict`, `.devbook/domain/repository-management/domain.md#pull-request-status` | The session card wears the same verdict as the pane, and the side panel shares one read per pull request with Progress (local [ADR 0042 §6](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#6-the-pull-requests-pane-and-the-session-card-read-the-contract)). **Not built.** Today the shell computes the card's verdict itself and the side panel keeps a cache of its own. |
| [Tasks](../tasks/dependencies.md) | Customer/Supplier (Repository Management = supplier) | An adapter under `Infrastructure.FileSystem/Tasks/` answers Tasks' own port, `ILinkedPullRequestStates`, from this context's contract | `.devbook/domain/repository-management/domain.md#pull-request-status`, `.devbook/domain/repository-management/domain.md#readiness-verdict`, `.devbook/domain/repository-management/domain.md#merge-command` | A task row's badge, its merge offer and a prompt entry's auto-Done read a linked pull request's state and verdict, and the row merges through the same commands (local [ADR 0042 §4](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#4-tasks-reads-pull-requests-through-its-own-port)). Tasks keeps its own small record and never references this context. **Not built.** Today Tasks' screen reads and merges through the GitHub adapter. |
| [Productivity](../productivity/features.md#personal-productivity-dashboard) | Customer/Supplier (Repository Management = supplier) | Reads the archived flag from the registry | `.devbook/domain/repository-management/domain.md#archived-repository` | Leaves an archived repository out of the dashboard's GitHub activity, and still counts its finished work. Its pull request activity history is not this context's pull request state. |
| [Progress](../progress/features.md#pull-request-status) | Customer/Supplier (Repository Management = supplier) | `GitHubGoalPullRequests` in `Infrastructure.FileSystem/Progress/` answers Progress's own port, `IGoalPullRequestReader`, from this context's contract | `.devbook/domain/repository-management/domain.md#pull-request-status`, `.devbook/domain/repository-management/domain.md#archived-repository` | Every pull request row on a goal shows the status this context publishes, and a task is done by its merged pull request (local [ADR 0042 §5](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#5-progresss-reader-goes-through-the-contract)). An archived repository's pull requests are not read. **Not built.** Today the adapter calls the GitHub adapter directly, with a cache of its own. |

## Notes

- The repository registry aligns with the workspace `config/repos.json` registry
  so `repo_id` resolves consistently across Tasks, Dev PC Management, and
  Repository Management.
- GitHub and package-registry access sit behind anti-corruption adapters so
  external schemas never leak into the `Repository` model.
- Technology baseline authority stays with Technology Stack; this context only
  consumes baselines and reports adoption back.
- Pull request state has one owner, this context, and one contract every reader
  goes through (local ADR 0042, proposed). Tasks and Progress never reference it:
  an adapter under `src/Infrastructure/` answers each one's own port, as
  `ILinkedSessionStates` does for Sessions.
- This context depends on no other context for pull request state. The pane's Mine
  view and the task card in its detail panel need the task a pull request belongs
  to. The desktop shell asks Tasks' lookup for it and composes the card, so this
  context holds no edge to Tasks, and Tasks' edge to it stays one-way.
- The pull request activity history the dashboard and Productivity read is not
  this context's pull request state. It answers what merged and when, not whether
  an open pull request is ready, and ADR 0042 leaves it where it is.
