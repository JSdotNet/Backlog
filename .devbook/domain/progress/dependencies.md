# Progress

```meta
type: dependencies
status: draft
related: [.devbook/domain/context-map.md, .devbook/arc42/adr/0028-progress-is-its-own-boundary-and-tasks-knows-no-sprints.md]
```

> Dependencies this bounded context has on other bounded contexts or modules, and
> known dependents. Note the DDD relationship pattern, integration mechanism, and
> published contract for each relationship.

Progress publishes no event. Every relationship below is a read, on the read path,
and none of them writes to the other side.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Tasks](../tasks/domain.md#task) | Customer/Supplier (Progress = customer) | In-process read of tasks by id, and of every task filed under a plan tag in a set of repositories | `.devbook/domain/tasks/domain.md#task`, `.devbook/domain/tasks/domain.md#projection-ref` | A sprint names its members by task id and its plans by tag. It reads each member's status, tags, plan tag, repository and `pull-request` Projection Refs to decide whether it is [done](domain.md#done) and who owns its row. Progress keeps no copy and never writes a task. |
| [Roadmap Planning](../roadmap/domain.md#roadmap-tag) | Customer/Supplier (Progress = customer) | Reads the plan tag vocabulary | `.devbook/domain/roadmap/domain.md#roadmap-tag` | The plans a person can link to a sprint, or name in an `InPlans` goal rule, are the plans the roadmap knows by tag. Progress relies on the tag never changing when a plan is renamed. |
| [Repository Management](../repository-management/domain.md#repository-registry) | Customer/Supplier (Progress = customer) | Registry lookup by repository alias | `.devbook/domain/repository-management/domain.md#repository-alias` | A series names its repositories by alias, and a plan link gathers tasks only in those repositories. Progress conforms to the registry's identity. The Sprints settings page reads the registry's aliases through `ISprintRepositories` to offer the repositories in no series. |

A linked pull request's merged state and its author are read from GitHub by the
same infrastructure reads the pull requests list uses. That is a transport rather
than a relationship with another context: Progress reaches a pull request only
through the task that links it.

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Monitoring & Dashboard](../monitoring/domain.md#dashboard) | Customer/Supplier (Progress = supplier) | In-process read of each series' open sprint | `.devbook/domain/progress/domain.md#sprint`, `.devbook/domain/progress/domain.md#brief-visibility` | The daily and the weekly brief name the open sprint of every series that chose them, with its goal and whether the goal is met. Breaks if a series stops saying which briefs it shows in. |
