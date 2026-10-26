# Progress

```meta
status: draft
index: root
type: context
deployment: module
related: [.devbook/arc42/05-building-block-view.md#desktop-app, .devbook/arc42/adr/0035-goals-and-impediments-replace-sprints.md]
```

Progress owns what a person is working towards and what stands in the way: the
goals their work is attached to, the impediments that hold it back, and the
stand-up that reads both over an iteration.

Inside the boundary:

- each [Goal](domain.md#goal), with its title, outcome line, due date and the
  plans, tasks and pull requests attached to it;
- how far a goal has come, counted per item, and whether it is
  [At risk or On track](domain.md#goal-forecast);
- each [Impediment](domain.md#impediment), its state and history, and the tasks it
  holds back;
- the [Iteration](domain.md#iteration) a person reads their work in, and the
  [stand-up](domain.md#stand-up) that reads goals, finished work, planned work and
  impediments over it.

Outside it: the task itself. [Tasks](../tasks/domain.md#task) owns a task's status,
due date, effort, the mark that it is blocked and its linked pull requests, and holds
no knowledge of goals or impediments. Progress names a task by its id, a plan by its
[plan tag](../roadmap/domain.md#roadmap-tag) and a pull request by its repository and
number, and reads all three from their owners on every reading. Marking a task blocked
raises an impediment, because Progress reacts to the mark; Tasks does not call
Progress and stores no impediment id. The pace a stand-up plans against is
[Roadmap Planning](../roadmap/context.md#story-points-a-week)'s. Why goals and
impediments sit here is local
[ADR 0035](../../arc42/adr/0035-goals-and-impediments-replace-sprints.md). The
relationships are in [dependencies.md](dependencies.md).

Progress used to lay sprint series over repositories. ADR 0035 retires them, with
their settings and the parts of local
[ADR 0028](../../arc42/adr/0028-progress-is-its-own-boundary-and-tasks-knows-no-sprints.md)
that described them, and the plan `+goals-standup` retired their code.

Goals, impediments and the stand-up are always on. The `goals` flag that switched the
whole context while it was built was retired with the plan's last entry, and with it
the In progress view it kept for a person who had it off
(local [ADR 0035 §10](../../arc42/adr/0035-goals-and-impediments-replace-sprints.md#10-the-view-is-goals-always-on)).

## Iteration length

```meta
status: draft
type: setting
key: goals.iteration-length
scope: user
default: week
related: [.devbook/domain/progress/features.md#choosing-the-iteration, .devbook/domain/progress/domain.md#iteration]
```

How long the window is that a person reads their goals and stand-up in. The values
are `day`, `week`, `two-weeks` and `month`; [Iteration](domain.md#iteration) says
which days each covers. The choice belongs to the person, so it travels with them to
every device. It also sets the length whose last four iterations a
[forecast](domain.md#goal-forecast) reads the done rate from.

## Two-week start

```meta
status: draft
type: setting
key: goals.iteration-anchor
scope: user
related: [.devbook/domain/progress/features.md#choosing-the-iteration, .devbook/domain/progress/domain.md#iteration]
```

The date two-week iterations are counted from. Each two-week window starts on this
date or a whole number of fourteen days from it. It has no value until the person
first chooses `two-weeks`, which asks for it and offers the Monday of the current
week. Changing it moves every two-week window, and changes no goal or impediment.
