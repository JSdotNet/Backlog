# Progress

```meta
status: draft
index: root
type: context
deployment: module
related: [.devbook/arc42/05-building-block-view.md#desktop-app, .devbook/arc42/adr/0028-progress-is-its-own-boundary-and-tasks-knows-no-sprints.md]
```

Progress owns the sprints a team works in: fixed time boxes laid over a set of
repositories, what each one set out to do, and how it ended.

Inside the boundary:

- the [Sprint Series](domain.md#sprint-series) a set of repositories works in,
  and its cadence, naming and goal rules;
- each [Sprint](domain.md#sprint) of a series, with its dates and its goal text;
- which tasks and plans belong to a sprint, which of them are goal tasks, and
  the note and open question a person keeps on each;
- closing a sprint, and what happened to each unfinished item when it closed.

Outside it: the task itself. [Tasks](../tasks/domain.md#task) owns a task's
status, tags and linked pull requests, and holds no knowledge of sprints.
Progress names a task by its id and a plan by its
[plan tag](../roadmap/domain.md#roadmap-tag), and reads both from their owners
on every reading. A sprint never changes a task: dropping an item, or sending it
back to the backlog, is recorded in the sprint alone. Why the boundary sits here
is local [ADR 0028](../../arc42/adr/0028-progress-is-its-own-boundary-and-tasks-knows-no-sprints.md). Its relationships are in [dependencies.md](dependencies.md).

A repository that belongs to no series keeps today's
[In progress](../sessions/features.md#in-progress-by-task) view, unchanged.

## Progress area

```meta
status: draft
type: feature-flag
key: progress
default: off
related: [".devbook/domain/progress/features.md#setting-up-a-sprint-series", ".devbook/domain/progress/features.md#following-a-sprint", ".devbook/arc42/08-crosscutting-concepts.md#feature-enablement"]
```

The flag switches the whole context. Turning it on offers sprint series in the
settings and shows the sprint view for the repositories that belong to one.
Switching it off withdraws every part of Progress. The repositories show the
In progress view again, and the briefs leave out their sprint lines. The series
and sprints already recorded are kept, and come back when the flag is turned on.

It ships off and marked `DEV`, the settings screen's mark for a feature still
under construction
([Feature Enablement](../../arc42/08-crosscutting-concepts.md#feature-enablement)).
The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a
person's choice is kept in `features.json` beside the app's other per-device
choices. The flag is retired when Progress leaves `DEV`.
