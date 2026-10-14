# Progress

```meta
type: features
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

Every feature here sits behind the [Progress area](context.md#progress-area)
flag.

## Setting up a sprint series

```meta
type: feature
status: draft
feature-flag: .devbook/domain/progress/context.md#progress-area
related: [.devbook/domain/progress/domain.md#sprint-series, .devbook/domain/progress/domain.md#series-assignment, .devbook/domain/progress/requirements.md#setting-up-a-sprint-series]
```

Give a set of repositories one cadence to work to. A person creates a series,
gives it its repositories and chooses how its sprints run. A repository belongs
to at most one series. A repository in no series keeps the
[In progress](../sessions/features.md#in-progress-by-task) view it has today.

### Choosing the cadence

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#sprint-length, .devbook/domain/progress/domain.md#sprint-window]
```

Choose a sprint length of one or two weeks, and the weekday sprints start on.
Every sprint of the series runs from that weekday for that many days. A changed
length or weekday applies from the next sprint.

### Naming the sprints

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#name-pattern]
```

Choose the pattern a sprint's name is made from, with `{n}` for its number. The
default `Sprint {n}` names the fourth sprint "Sprint 4".

### Choosing which briefs show the series

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#brief-visibility, .devbook/domain/progress/features.md#sprints-in-the-briefs]
```

Choose whether the series' open sprint shows in the daily brief, and whether it
shows in the weekly brief.

## Filling a sprint

```meta
type: feature
status: draft
feature-flag: .devbook/domain/progress/context.md#progress-area
related: [.devbook/domain/progress/domain.md#sprint, .devbook/domain/progress/requirements.md#filling-a-sprint]
```

Say what a sprint holds. Its membership is Progress's own: adding a task to a
sprint, or linking a plan, changes nothing on the task.

### Adding a task

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#sprint-item]
```

Add one task to the open sprint by its id. Any task in the series' repositories
can be added, whatever its status. Removing it again takes it out of the sprint
and leaves the task as it was.

### Linking a plan

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#plan-link, .devbook/domain/roadmap/domain.md#roadmap-tag]
```

Link a whole plan to the sprint by its plan tag. The plan brings in every task
filed under that tag in the series' repositories, finished or not. A task filed
under the plan later joins the sprint by itself. Unlinking the plan takes out the
tasks it brought in, except those added directly.

## Setting the sprint goal

```meta
type: feature
status: draft
feature-flag: .devbook/domain/progress/context.md#progress-area
related: [.devbook/domain/progress/domain.md#goal-rule, .devbook/domain/progress/domain.md#goal-threshold, .devbook/domain/progress/requirements.md#setting-the-sprint-goal]
```

Say what the sprint sets out to do, and how to tell it did. A sprint carries a
goal in words. Its series picks the goal tasks by rules and says when the goal
is met.

### Choosing the goal rules

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#goal-rule-kind]
```

Choose any combination of rules for the series:

- the tasks in chosen plans;
- the tasks marked as goal;
- the tasks with chosen tags;
- every task in the sprint.

A task picked by more than one rule counts once.

### Marking a task as goal

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#sprint-item]
```

Mark one sprint item as a goal task. The mark counts while the series has the
"marked as goal" rule.

### Choosing when the goal is met

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#goal-threshold, .devbook/domain/progress/domain.md#done]
```

Choose whether the goal is met when every goal task is done, or when a set share
of them is, such as 80%. A task is done when its status is Done, or when every
pull request linked to it is merged.

## Following a sprint

```meta
type: feature
status: draft
feature-flag: .devbook/domain/progress/context.md#progress-area
related: [.devbook/domain/progress/domain.md#sprint-progress, .devbook/domain/progress/requirements.md#following-a-sprint]
```

See the open sprint of the series in scope: its name, its dates, its goal, how
many goal tasks are done, and whether the goal is met. Each member shows as a row
with its task, whether it is done, and its owner. The owner is the author of the
task's linked pull requests. A task with no linked pull request shows no owner.

### Keeping a note on an item

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#sprint-item]
```

Write a one-line note on a sprint item, and mark it as an open question. Both
belong to the sprint, not to the task, and the next sprint starts without them.

## Closing a sprint

```meta
type: feature
status: draft
feature-flag: .devbook/domain/progress/context.md#progress-area
related: [.devbook/domain/progress/domain.md#close-outcome, .devbook/domain/progress/requirements.md#closing-a-sprint]
```

End the open sprint and start the next one in one step:

1. The person chooses an outcome for each unfinished item.
2. The person writes the next sprint's goal.
3. Progress closes the sprint and starts the next one.

The three outcomes are:

| Outcome | The task | The sprint records |
|---|---|---|
| Carry | Joins the next sprint, as a directly added member | Carried |
| Back to the backlog | Leaves the sprint and joins no other | Returned |
| Drop | Is untouched | Dropped, in this sprint only |

No outcome changes the task. The next sprint is numbered one higher and starts on
the first start weekday after the closed sprint's last day. It starts with no
linked plan: an unfinished task a plan brought in moves on only when it is carried.

## Syncing sprints between devices

```meta
type: feature
status: draft
depends-on: [.devbook/domain/tasks/features.md#multi-device-sync]
related: [.devbook/arc42/adr/0018-roadmap-plan-and-pace-ride-the-task-feed.md, .devbook/arc42/adr/0028-progress-is-its-own-boundary-and-tasks-knows-no-sprints.md, .devbook/domain/progress/domain.md#sprint-series, .devbook/domain/roadmap/features.md#syncing-the-plan-between-devices, .devbook/domain/progress/requirements.md#syncing-sprints-between-devices]
```

Keep every sprint series the same on every desktop the person has paired. A series
travels whole beside the tasks the devices already sync
([Multi-device sync](../tasks/features.md#multi-device-sync)): its cadence, naming,
goal rules and brief choices, every sprint with its dates and goal, which tasks and
plans belong to each, the goal marks, the notes and open questions, and how each
closed sprint ended. It travels only while sync is on and the device is paired.

Each series is replaced whole, never merged, as the roadmap's
[plan](../roadmap/features.md#syncing-the-plan-between-devices) is. When two devices
have both changed one series since they last synced, the **newer change wins on
every device** and the older one is lost. Two different series are two documents,
so a change to one never displaces a change to the other. A device that has **never
set up a series has nothing to send**.

Series travel whether the [Progress area](context.md#progress-area) is on or off,
because the series recorded while it is off are kept. The phone keeps no sprints and
lists none.

## Sprints in the briefs

```meta
type: feature
status: draft
feature-flag: .devbook/domain/progress/context.md#progress-area
related: [.devbook/domain/progress/domain.md#brief-visibility, .devbook/domain/productivity/features.md#the-weekly-brief, .devbook/domain/progress/requirements.md#sprints-in-the-briefs]
```

Read the open sprint of a series in the daily and the weekly brief, for a series
that chose to show there. The brief names the sprint, its goal, how many goal
tasks are done, and whether the goal is met.
