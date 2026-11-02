# Dependencies

```meta
type: dependencies
status: draft
related: [.devbook/domain/context-map.md, .devbook/arc42/adr/0035-goals-and-impediments-replace-sprints.md, .devbook/arc42/adr/0042-repository-management-owns-pull-request-state.md]
```

> Dependencies this bounded context has on other bounded contexts or modules, and
> known dependents. Note the DDD relationship pattern, integration mechanism, and
> published contract for each relationship.

Progress publishes no event. Every relationship below is a read on the read path,
except the reaction to a blocked task, and none of them writes to the other side.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Tasks](../tasks/domain.md#task) | Customer/Supplier (Progress = customer) | In-process read of tasks by id and of every task filed under a plan tag; a reaction to a task saved with the blocked mark | `.devbook/domain/tasks/domain.md#task`, `.devbook/domain/tasks/domain.md#readiness`, `.devbook/domain/tasks/domain.md#projection-ref` | A goal counts tasks and plan entries. It reads each task's status, readiness, due date, `effort:`, the day it was completed, its blocked mark and its `pull-request` Projection Refs to decide whether it is [done](domain.md#done), planned or held back. Progress keeps no copy and never writes a task. |
| [Roadmap Planning](../roadmap/domain.md#roadmap-tag) | Customer/Supplier (Progress = customer) | Reads the plan tag vocabulary, each plan's order, and the person's pace | `.devbook/domain/roadmap/domain.md#roadmap-tag`, `.devbook/domain/roadmap/context.md#story-points-a-week`, `.devbook/domain/roadmap/domain.md#working-week` | A goal attaches a plan by tag, and Planned takes a plan's first ready entry in its order. The points tag scales the pace to a window, and an impediment's age counts working days. Progress relies on the tag never changing when a plan is renamed. |
| [Repository Management](../repository-management/domain.md#repository-registry) | Customer/Supplier (Progress = customer) | Registry lookup by repository alias | `.devbook/domain/repository-management/domain.md#repository-alias`, `.devbook/domain/repository-management/domain.md#archived-repository` | A pull request attachment names its repository, and No goal narrows to the header's repository scope. An archived repository's pull requests are not read. |
| [Repository Management](../repository-management/features.md#pull-request-status), for pull request state | Customer/Supplier (Progress = customer) | Progress's own port, `IGoalPullRequestReader`, answered by `GitHubGoalPullRequests` in `Infrastructure.FileSystem/Progress/` over Repository Management's contract | `.devbook/domain/repository-management/domain.md#pull-request-status` | Every pull request row on a goal shows its [status](features.md#pull-request-status), and a task is [done](domain.md#done) by its merged pull request. Progress names a pull request by its repository and number and asks for nothing else. It shares one cache with a task's side panel, so a pull request is read once for both (local [ADR 0042 §5](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#5-progresss-reader-goes-through-the-contract)). Where no GitHub is configured, or GitHub does not answer, a pull request reads as unknown and a task is done by its status alone. **Not built.** Today the adapter calls the GitHub adapter directly, with a cache of its own, which local ADR 0035 §7 drew as an edge from GitHub to Progress. |
| [Sessions](../sessions/domain.md#agent-session) | Customer/Supplier (Progress = customer) | In-process read of the Session Log by session identity | `.devbook/domain/sessions/domain.md#session-identity`, `.devbook/domain/sessions/domain.md#session-state`, `.devbook/domain/sessions/domain.md#working-location` | A goal attaches a session (local [ADR 0037](../../arc42/adr/0037-a-session-attaches-to-a-goal-as-its-own-item.md)). Progress reads its title, its state and its branch and resolved repository, to find the pull request on its branch and say whether it is doing. Its linked tasks come from Tasks, by the `session` Projection Refs. A session no record answers for reads as to do. With the Sessions area off, no session is read. **Not built.** |

Goals and impediments travel to the person's other devices through the sync client, on
the task feed, as whole documents under the kind tokens `goal` and `impediment`
([Syncing goals and impediments](features.md#syncing-goals-and-impediments)). That is
a transport: the sync client routes a document to Progress by kind and never reads
inside, and which copy wins is decided per document by its own stamp. The phone's
impediment changes reach the feed through the device outbox.

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| None | | | | The briefs no longer read Progress. Their earlier block is retired by local [ADR 0035](../../arc42/adr/0035-goals-and-impediments-replace-sprints.md), and no goal block replaces it yet. |
