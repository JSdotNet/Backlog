# Progress

```meta
type: domain
status: draft
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context; each chapter's
> `type` records which of those it is. An Aggregate's owned Entities, Value
> Objects, and Enums are chapters directly beneath it, typed `entity`,
> `value-object`, and `enum`. Value Objects/Enums shared across multiple
> aggregates get their own chapter at the end instead of being duplicated.

What Progress owns and what it leaves to Tasks and Roadmap Planning is in
[context.md](context.md). The model holds no copy of a task. A sprint names its
tasks by id and its plans by their plan tag, and asks their owners for status on
every reading.

## Sprint Series

```meta
type: aggregate
status: draft
related: [.devbook/domain/repository-management/domain.md#repository-registry, .devbook/domain/progress/domain.invariants.md#sprint-series, .devbook/domain/progress/features.md#setting-up-a-sprint-series]
aliases: [SprintSeries, series, sprint series]
```

The cadence a set of repositories works to, and the consistency boundary for
every sprint in it. A series holds:

- its [Series Repositories](#series-repositories);
- its [Sprint Length](#sprint-length) and the weekday its sprints start on;
- its [Name Pattern](#name-pattern);
- its [Brief Visibility](#brief-visibility);
- its [Goal Rules](#goal-rule) and its [Goal Threshold](#goal-threshold);
- its [Sprints](#sprint), numbered from 1.

The series is the root rather than the sprint, because **closing a sprint
changes two sprints at once**. It ends one and starts the next with the carried
items in it. A boundary drawn around one sprint would leave the carry-over to
whatever called both. Numbering and the "one open sprint" rule are also rules
across sprints, so they need the series to hold them.

A series exists only while it has at least one repository. The rules it holds
are in [its invariants](domain.invariants.md#sprint-series).

### Sprint

```meta
type: entity
status: draft
aliases: [sprint, time box, iteration]
related: [.devbook/domain/progress/features.md#following-a-sprint, .devbook/domain/progress/features.md#closing-a-sprint]
```

One time box of a series. It holds:

- its `number`, one more than the sprint before it;
- its [Sprint Window](#sprint-window), the start and end dates;
- its `goal`, free text saying what the sprint sets out to do;
- its [Plan Links](#plan-link) and its [Sprint Items](#sprint-item);
- whether it is open or closed.

Its name is not stored. It is the series' [Name Pattern](#name-pattern) with the
number filled in, so renaming the pattern renames every sprint of the series.

A sprint's **members** are the tasks a person added to it directly, and every
task its linked plans bring in. A linked plan brings in every task carrying that
plan tag in the series' repositories, finished or not. A task that is both added
directly and brought in by a plan is one member.

### Plan Link

```meta
type: value-object
status: draft
aliases: [PlanLink, linked plan]
related: [.devbook/domain/roadmap/domain.md#roadmap-tag]
```

A whole plan linked to a sprint by its [plan tag](../roadmap/domain.md#roadmap-tag).
Equality is by the tag. The link stores the tag alone. Which tasks it brings in is
read from Tasks on every reading, so a task filed under the plan later joins the
sprint without anyone adding it.

### Sprint Item

```meta
type: entity
status: draft
aliases: [SprintItem, item, sprint row]
related: [.devbook/domain/tasks/domain.md#task, .devbook/domain/progress/features.md#following-a-sprint]
```

What the sprint itself records about one member task, identified by the task's
id. It holds:

- whether the task was added directly, rather than only brought in by a plan;
- whether it is marked as a goal task;
- an optional one-line `note`;
- an `open question` mark;
- its [Close Outcome](#close-outcome), once the sprint closed with it unfinished.

A task brought in only by a plan has no item until something is recorded on it.
The note and the open-question mark belong to Progress, not to the task: they
say something about the task in this sprint, and the next sprint starts without
them.

A row shows the authors of the task's linked pull requests as its owner. There is
no owner field, in the item or anywhere else in this context.

### Sprint Window

```meta
type: value-object
status: draft
aliases: [SprintWindow, sprint dates]
```

The first and last day of a sprint, both inclusive, as calendar dates. The window
starts on the series' start weekday and is as long as the series'
[Sprint Length](#sprint-length). Equality is by value.

### Series Repositories

```meta
type: value-object
status: draft
aliases: [series repositories]
related: [.devbook/domain/repository-management/domain.md#repository-alias]
```

The repositories a series belongs to, as a set of
[repository aliases](../repository-management/domain.md#repository-alias),
compared without regard to case. A repository belongs to at most one series, so
the set is also what the [Series Assignment](#series-assignment) checks.

### Sprint Length

```meta
type: enum
status: draft
aliases: [SprintLength, length]
```

How long each sprint of a series lasts: `OneWeek` (7 days) or `TwoWeeks` (14 days).
A change of length applies from the next sprint; the open one keeps its window.

### Name Pattern

```meta
type: value-object
status: draft
aliases: [NamePattern, sprint name]
```

The text a sprint's name is made from, with `{n}` standing for its number. The
default is `Sprint {n}`, which names the third sprint "Sprint 3".

### Brief Visibility

```meta
type: value-object
status: draft
aliases: [BriefVisibility]
related: [.devbook/domain/progress/features.md#sprints-in-the-briefs]
```

Two choices a series makes: whether its open sprint shows in the daily brief,
and whether it shows in the weekly brief. Each is on or off on its own.

### Goal Rule

```meta
type: value-object
status: draft
aliases: [GoalRule, goal rules]
related: [.devbook/domain/progress/features.md#setting-the-sprint-goal]
```

One way a series picks a sprint's goal tasks, of one [Goal Rule Kind](#goal-rule-kind).
A rule of kind `InPlans` names its plan tags, and one of kind `Tagged` names its
task tags. A series holds any combination of rules. A member is a goal task when
any rule picks it, and it counts once however many rules do.

### Goal Rule Kind

```meta
type: enum
status: draft
aliases: [GoalRuleKind]
```

- `InPlans`: the members brought in by the chosen plans.
- `MarkedAsGoal`: the members whose [Sprint Item](#sprint-item) is marked as a goal task.
- `Tagged`: the members carrying one of the chosen task tags.
- `Everything`: every member of the sprint.

### Goal Threshold

```meta
type: value-object
status: draft
aliases: [GoalThreshold, goal is met when]
```

When a sprint's goal is met: when every goal task is [done](#done), or when a set
share of them is, such as 80%. The share is a whole percentage from 1 to 100, and
100% is the same as every goal task. The default is every goal task.

### Close Outcome

```meta
type: enum
status: draft
aliases: [CloseOutcome, carry, back to the backlog, drop]
related: [.devbook/domain/progress/features.md#closing-a-sprint]
```

What closing a sprint did with one unfinished member:

- `Carry`: the task joins the next sprint, as a directly added member.
- `BackToBacklog`: the task leaves the sprint and joins no other.
- `Drop`: the task is recorded as dropped in this sprint only.

None of the three changes the task. Its status, tags and plan are what they were.

## Series Assignment

```meta
type: domain-service
status: draft
related: [.devbook/domain/progress/domain.invariants.md#series-assignment, .devbook/domain/progress/features.md#setting-up-a-sprint-series]
aliases: [SeriesAssignment]
```

Gives a repository to a series, and holds the one rule that spans series: a
repository belongs to at most one. It is a service because no single series can
see the others. Its rules are in
[its invariants](domain.invariants.md#series-assignment).

## Sprint Progress

```meta
type: domain-service
status: draft
related: [.devbook/domain/tasks/domain.md#task-status, .devbook/domain/tasks/domain.md#projection-ref, .devbook/domain/progress/features.md#following-a-sprint]
aliases: [SprintProgress]
```

Reads a sprint against its members' current state. It resolves the members from
the direct items and the plan links, picks the goal tasks by the series'
[Goal Rules](#goal-rule), decides which are [done](#done), and says whether the
goal is met by the [Goal Threshold](#goal-threshold). It stores nothing: every
answer is read afresh, so a task finished elsewhere moves the sprint at once.

## Ubiquitous Language

```meta
type: ubiquitous-language
status: draft
```

### Done

```meta
type: term
status: draft
related: [.devbook/domain/tasks/domain.md#task-status]
```

A member is done in a sprint when its task's status is Done, or when every pull
request linked to the task is merged. Either is enough. A task with no linked pull
request is done only by its status.

### Goal Task

```meta
type: term
status: draft
```

A member of a sprint that one of the series' [Goal Rules](#goal-rule) picks. The
sprint's goal is met or not by the goal tasks alone.
