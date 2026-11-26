# Dependencies

```meta
type: dependencies
related: [.devbook/domain/context-map.md, .devbook/arc42/adr/0035-goals-and-impediments-replace-sprints.md, .devbook/arc42/adr/0042-repository-management-owns-pull-request-state.md]
```

> Dependencies this bounded context has on other bounded contexts or modules, and
> known dependents. Note the DDD relationship pattern, integration mechanism, and
> published contract for each relationship.

Goals publishes no event, and is the customer in every Customer/Supplier row. Every relationship below is a read that writes nothing to the other side. Goals no longer reacts to a task's blocked mark: that policy is [retired](domain.md#blocked-task-policy).

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Work](../work/domain.task.md#task) | Customer/Supplier | In-process read of tasks by id and of every task filed under a plan tag | `.devbook/domain/work/domain.task.md#task`, `.devbook/domain/work/domain.task.md#readiness`, `.devbook/domain/work/domain.task.md#projection-ref` | A goal counts tasks and plan entries, and an impediment reads what it holds back. |
| [Work](../work/domain.plan.md#roadmap-tag) | Customer/Supplier | Reads the plan tag vocabulary, each plan's order, and the person's pace | `.devbook/domain/work/domain.plan.md#roadmap-tag`, `.devbook/domain/work/context.md#story-points-a-week`, `.devbook/domain/work/domain.md#working-week` | A goal attaches a plan by tag, and Planned takes a plan's first ready entry in order. |
| [Repositories](../repositories/domain.md#repository-registry) | Customer/Supplier | Registry lookup by repository alias | `.devbook/domain/repositories/domain.md#repository-alias`, `.devbook/domain/repositories/domain.md#archived-repository` | A pull request attachment names its repository, and No goal narrows to the repository scope. |
| [Repositories](../repositories/features.md#pull-request-status), for pull request state | Customer/Supplier | Goals' own port, `IGoalPullRequestReader`, answered by an adapter | `.devbook/domain/repositories/domain.md#pull-request-status` | Pull request rows on a goal show their [status](features.md#pull-request-status), and a merged pull request makes a task [done](domain.md#done). |
| [Sessions](../sessions/domain.md#agent-session) | Customer/Supplier | In-process read of the Session Log by session identity | `.devbook/domain/sessions/domain.md#session-identity`, `.devbook/domain/sessions/domain.md#session-state`, `.devbook/domain/sessions/domain.md#working-location` | A goal attaches a session (local [ADR 0037](../../arc42/adr/0037-a-session-attaches-to-a-goal-as-its-own-item.md)). **Not built.** |

- **Tasks:** A goal decides whether a task is [done](domain.md#done), planned or blocked from its status, readiness, due date, `effort:`, completion day, blocked mark and `pull-request` Projection Refs.
- **Tasks, for an impediment:** An impediment reads the status of each task, and of every entry under a plan, it holds back to decide whether that item is [settled](domain.md#settled). A task no longer found is not settled. Goals keeps no copy and never writes a task.
- **Work, the plan:** The points tag scales the pace to a window, and an impediment's age counts working days. Goals relies on the tag never changing when a plan is renamed.
- **Repositories, registry:** No goal narrows to the header's repository scope. An archived repository's pull requests are not read.
- **Repositories, pull request state:** The adapter `RepositoriesGoalPullRequests` in `Infrastructure.FileSystem/Goals/` answers the port `IGoalPullRequestReader` over Repositories' contract. Goals asks for nothing but a pull request's repository and number. It shares one cache with a task's side panel, so a pull request is read once for both (local
  [ADR 0042 §5](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#5-progresss-reader-goes-through-the-contract)).

  The same read covers every pull request an open impediment holds back, attached to a goal or not, so its state decides whether the item is [settled](domain.md#settled). Where no GitHub is configured, or GitHub does not answer, a pull request reads as unknown. A task is then done by its status alone, and a held-back pull request, like one in an archived repository, is not settled. The adapter keeps nothing and replaces the edge from GitHub to Goals that local ADR 0035 §7 drew.
- **Sessions:** Goals reads a session's title, state, branch and resolved repository, to find the pull request on its branch and say whether it is doing. Its linked tasks come from Tasks, by the `session` Projection Refs. A session no record answers for reads as to do. With the Sessions area off, no session is read.

Goals and impediments travel to the person's other devices through the sync client, on the task feed. Each is a whole document under the kind token `goal` or `impediment` ([Syncing goals and impediments](features.md#syncing-goals-and-impediments)). The client routes a document to Goals by kind and never reads inside; each document's own stamp decides which copy wins. The phone's impediment changes reach the feed through the device outbox. The phone reads a held-back pull request's state as a desktop last read it, so it never asks GitHub.

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| None | | | | The briefs no longer read Progress. Local [ADR 0035](../../arc42/adr/0035-goals-and-impediments-replace-sprints.md) retired their block, with no replacement yet. |
