# Dependencies

```meta
type: dependencies
status: draft
related: [.devbook/domain/context-map.md, .devbook/arc42/adr/0035-goals-and-impediments-replace-sprints.md]
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

A pull request's state, its checks, its approvals and the approvals its base branch's
protection rule or rulesets require are read from GitHub by the same infrastructure the
pull requests list uses, in a read of its own beside the In progress view's slimmer one. That is a transport rather than a relationship with another
context: Progress names a pull request by its repository and number, and asks GitHub
for nothing else. Where no GitHub is configured, or GitHub does not answer, a pull
request reads as unknown and a task is done by its status alone.

Goals and impediments travel to the person's other devices through the sync client, on
the task feed, as whole documents under the kind tokens `goal` and `impediment`
([Syncing goals and impediments](features.md#syncing-goals-and-impediments)). That is
a transport too: the sync client routes a document to Progress by kind and never reads
inside, and which copy wins is decided per document by its own stamp. The phone's
impediment changes reach the feed through the device outbox.

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| None | | | | The briefs no longer read Progress. Their earlier block is retired by local [ADR 0035](../../arc42/adr/0035-goals-and-impediments-replace-sprints.md), and no goal block replaces it yet. |
