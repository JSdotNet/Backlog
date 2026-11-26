# Repositories

```meta
type: dependencies
related: [.devbook/domain/context-map.md, .devbook/arc42/adr/0042-repository-management-owns-pull-request-state.md]
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Devbook](../devbook/context.md) | Customer/Supplier (Repositories = customer) | Devbook's reader over every registered repository's devbook database, read-only (local ADR 0015); not built | `.devbook/domain/devbook/features.md#technology-view` | Each repository's `tech/` ratings and versions, for [portfolio technology adoption](features.md#portfolio-technology-adoption) and to flag a technology rated `hold` or `retired`. |
| GitHub (external) | ACL | REST API | `.devbook/domain/repositories/domain.md#repository-scan` | Repository metadata, issues, pull requests, branch protection, and security alerts. |
| GitHub (external), for pull request state | ACL | `Backlog.Infrastructure.GitHub`, under the account each repository resolves to (local ADR 0031) | `.devbook/domain/repositories/domain.md#pull-request-status`, `.devbook/domain/repositories/domain.md#merge-command` | Reads each open pull request with its checks, reviews and base branch rules; sends merge commands. |
| Package registries (NuGet, npm, PyPI, etc.) | ACL | Polling | `.devbook/domain/repositories/domain.md#repository-scan` | Latest package versions used to detect outdated dependencies. |
| [Devbook](../devbook/context.md) | Customer/Supplier (Repositories = customer) | ADR cross-link by id | `.devbook/domain/devbook/context.md` | Architecture/technology decisions are cross-linked from repository metadata. |

- **GitHub (external), for pull request state:** GitHub's answer is translated into the one status and kept in one cache with the freshness rules of local [ADR 0042 §3](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#3-one-set-of-commands-and-one-cache). No GitHub shape reaches a consumer, and no module screen calls a pull request member of the GitHub adapter itself. Archived repositories are skipped here, once, for every reader. `GitHubPullRequestSource` in `Infrastructure.GitHub/Repositories/` answers this context's `IPullRequestSource`, and `GitHubRegisteredRepositories` its `IRegisteredRepositories`.

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Work](../work/domain.task.md#task) | Customer/Supplier (Repositories = supplier) | A recommendation creates a task | `.devbook/domain/repositories/domain.md#repository-registry` | Low-health repositories generate tasks for updates/cleanup. |
| [Dev PC Management](../dev-pc-management/domain.md#machine-registry) | Customer/Supplier (Repositories = supplier) | Shared repository registry (`config/repos.json`) | `.devbook/domain/repositories/domain.md#repository` | Aligns registered repositories with local clone paths for developer workflows. |
| [Work](../work/features.md#search-filter-and-organize) | Customer/Supplier (Repositories = supplier) | Reads the archived flag from the registry | `.devbook/domain/repositories/domain.md#archived-repository` | Hides an archived repository from the scope, the Board's columns and pickers; pauses its Connected Target. |
| [Work, the roadmap](../work/features.md#reading-the-plan-by-repository) | Customer/Supplier (Repositories = supplier) | Reads the archived flag from the registry | `.devbook/domain/repositories/domain.md#archived-repository` | Greys and tags an archived repository's band, and hides its open work. |
| [Devbook](../devbook/features.md#area-selection-and-scope) | Customer/Supplier (Repositories = supplier) | Reads the archived flag from the registry | `.devbook/domain/repositories/domain.md#archived-repository` | Opens an archived repository's devbook read-only and fetches no snapshot of it on its own. |
| [Sessions](../sessions/features.md#the-detail-of-one-session) | Customer/Supplier (Repositories = supplier) | The desktop shell composes the session card and asks this context's contract for its verdict. | `.devbook/domain/repositories/domain.md#readiness-verdict`, `.devbook/domain/repositories/domain.md#pull-request-status` | The session card wears the pane's verdict, and the side panel shares one read with Progress. |
| [Work](../work/dependencies.md) | Customer/Supplier (Repositories = supplier) | An adapter under `Infrastructure.FileSystem/Tasks/` answers Tasks' own port, `ILinkedPullRequestStates`, from this context's contract | `.devbook/domain/repositories/domain.md#pull-request-status`, `.devbook/domain/repositories/domain.md#readiness-verdict`, `.devbook/domain/repositories/domain.md#merge-command` | A task row reads a linked pull request's state and verdict, and merges through the same commands. |
| [Insights](../insights/features.md#personal-productivity-dashboard) | Customer/Supplier (Repositories = supplier) | Reads the archived flag from the registry | `.devbook/domain/repositories/domain.md#archived-repository` | Leaves an archived repository out of the dashboard's GitHub activity, and still counts its finished work. |
| [Goals](../goals/features.md#pull-request-status) | Customer/Supplier (Repositories = supplier) | `RepositoriesGoalPullRequests` in `Infrastructure.FileSystem/Goals/` answers Goals' own port, `IGoalPullRequestReader`, from this context's contract | `.devbook/domain/repositories/domain.md#pull-request-status`, `.devbook/domain/repositories/domain.md#archived-repository` | Goal rows show the published status, and a merged pull request makes its task done. |

- **Sessions:** The session card is the session list's pull request card. A task's side panel reads its linked pull requests through the same cache, one read per pull request (local [ADR 0042 §6](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#6-the-pull-requests-pane-and-the-session-card-read-the-contract)). `SessionPullRequestStatuses` draws the card and `WorkInProgressReader` reads the side panel, both in the shell and through `IPullRequests`. Sessions references nothing of this context.
- **Tasks, for linked pull requests:** A task row's badge, its merge offer and a prompt entry's auto-Done read the state and verdict (local [ADR 0042 §4](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#4-tasks-reads-pull-requests-through-its-own-port)). The adapter is `LinkedPullRequestStates`. Tasks keeps its own small record and never references this context.
- **Goals:** Every pull request row on a goal shows the status this context publishes (local [ADR 0042 §5](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#5-progresss-reader-goes-through-the-contract)). An archived repository's pull requests are not read.

## Notes

- The repository registry aligns with the workspace `config/repos.json` registry so `repo_id` resolves consistently across Tasks, Dev PC Management and Repositories.
- GitHub and package-registry access sit behind anti-corruption adapters, so external schemas never leak into the `Repository` model.
- No context owns a technology baseline (local ADR 0073 §5). A repository's ratings and approved versions are its own devbook `tech/` folder; this context reads them and writes none.
- Pull request state has one owner, this context, and one contract every reader goes through (local ADR 0042). Tasks and Goals never reference it: an adapter under `src/Infrastructure/` answers each one's own port, as `ILinkedSessionStates` does for Sessions.
- This context depends on no other context for pull request state. The pane's Mine view and the task card in its detail panel need the task a pull request belongs to. The desktop shell asks Tasks' lookup for it and composes the card, so Tasks' edge to this context stays one-way.
- The history of pull request activity the dashboard and Insights read answers what merged and when, not whether an open pull request is ready. ADR 0042 leaves it there.
