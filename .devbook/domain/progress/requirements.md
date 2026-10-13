# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it.

Progress is not built yet, so no requirement names a test. The scenarios use a
series over the repositories `backlog` and `docs`, with two-week sprints starting
on a Monday. Sprint 4 runs from Monday 12 October to Sunday 25 October 2026.

## Setting up a sprint series

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#setting-up-a-sprint-series]
```

### Requirement: A repository joins one series only

```meta
type: requirement
status: draft
```

The system SHALL refuse to give a repository to a series while another series has it.

#### Scenario: A repository already in a series

- **Given** the repository `backlog` belongs to the series "Core"
- **When** the person adds `backlog` to the series "Docs"
- **Then** the change is refused and `backlog` stays in "Core"

### Requirement: A repository in no series keeps the In progress view

```meta
type: requirement
status: draft
```

The system SHALL show the In progress view, unchanged, for a repository that belongs to no series.

#### Scenario: A repository left out of every series

- **Given** the repository `tools` belongs to no series and the Progress area is on
- **When** the person scopes the header to `tools`
- **Then** the In progress view shows, with no sprint in it

### Requirement: A sprint is named by the pattern

```meta
type: requirement
status: draft
```

The system SHALL name each sprint by its series' name pattern with `{n}` replaced by the sprint's number.

#### Scenario: The default pattern

- **Given** a series with the name pattern `Sprint {n}`
- **When** its fourth sprint is shown
- **Then** it is named "Sprint 4"

#### Scenario: A renamed pattern renames every sprint

- **Given** a series whose sprints 1 to 4 were shown as "Sprint 1" to "Sprint 4"
- **When** the person changes the pattern to `Core {n}`
- **Then** the same sprints show as "Core 1" to "Core 4"

## Filling a sprint

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#filling-a-sprint]
```

### Requirement: A linked plan brings in every task of the plan

```meta
type: requirement
status: draft
```

The system SHALL count as members of a sprint every task filed under a linked plan's tag in the series' repositories, finished or not.

#### Scenario: Finished and unfinished tasks of a plan

- **Given** the plan `+progress-sprints` has three tasks in `backlog`, one of them Done
- **When** the person links `+progress-sprints` to Sprint 4
- **Then** Sprint 4 has all three tasks as members

#### Scenario: A task outside the series' repositories

- **Given** the plan `+progress-sprints` has a task in `tools`, which is not one of the series' repositories
- **When** the person links `+progress-sprints` to Sprint 4
- **Then** the task in `tools` is not a member of Sprint 4

#### Scenario: A task filed under the plan later

- **Given** Sprint 4 links `+progress-sprints`
- **When** a new task in `docs` is filed under `+progress-sprints`
- **Then** the new task is a member of Sprint 4

### Requirement: Membership leaves the task alone

```meta
type: requirement
status: draft
```

The system SHALL leave a task's status, tags and plan unchanged when it is added to or removed from a sprint.

#### Scenario: Removing a task from a sprint

- **Given** an In progress task added directly to Sprint 4
- **When** the person removes it from Sprint 4
- **Then** the task is still In progress, with the same tags and plan

## Setting the sprint goal

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#setting-the-sprint-goal]
```

### Requirement: A goal task counts once

```meta
type: requirement
status: draft
```

The system SHALL count a member once among the goal tasks, however many goal rules pick it.

#### Scenario: Picked by two rules

- **Given** a series with the rules "tasks in chosen plans" for `+progress-sprints` and "tasks marked as goal"
- **And** a member of Sprint 4 in `+progress-sprints` that is also marked as goal
- **When** the sprint is shown
- **Then** that member counts as one goal task

### Requirement: The goal is met at the threshold

```meta
type: requirement
status: draft
```

The system SHALL show a sprint's goal as met when the share of done goal tasks reaches the series' goal threshold.

#### Scenario: Every goal task

- **Given** a series whose goal is met when every goal task is done
- **And** Sprint 4 has five goal tasks, four of them done
- **When** the sprint is shown
- **Then** its goal is not met

#### Scenario: A share of the goal tasks

- **Given** a series whose goal is met when 80% of the goal tasks are done
- **And** Sprint 4 has five goal tasks, four of them done
- **When** the sprint is shown
- **Then** its goal is met

### Requirement: Merged pull requests make a task done

```meta
type: requirement
status: draft
```

The system SHALL count a member as done when its task is Done, or when every pull request linked to the task is merged.

#### Scenario: Every linked pull request merged

- **Given** an In progress task with two linked pull requests, both merged
- **When** the sprint is shown
- **Then** the task counts as done

#### Scenario: One linked pull request still open

- **Given** an In progress task with two linked pull requests, one merged and one open
- **When** the sprint is shown
- **Then** the task does not count as done

## Following a sprint

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#following-a-sprint]
```

### Requirement: A row's owner is its pull requests' author

```meta
type: requirement
status: draft
```

The system SHALL show as a row's owner the authors of the task's linked pull requests, and no owner for a task with none.

#### Scenario: A task with a linked pull request

- **Given** a member whose task links one pull request, opened by `jsdotnet`
- **When** the sprint is shown
- **Then** the row shows `jsdotnet` as its owner

#### Scenario: A task with no linked pull request

- **Given** a member whose task links no pull request
- **When** the sprint is shown
- **Then** the row shows no owner

### Requirement: A note stays in its sprint

```meta
type: requirement
status: draft
```

The system SHALL keep a sprint item's note and open-question mark in that sprint only.

#### Scenario: A carried item starts clean

- **Given** a member of Sprint 4 with a note and an open-question mark
- **When** Sprint 4 closes with the member carried
- **Then** the member is in Sprint 5 with no note and no open-question mark
- **And** Sprint 4 still shows the note and the mark

## Closing a sprint

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#closing-a-sprint]
```

### Requirement: Closing settles every unfinished item

```meta
type: requirement
status: draft
```

The system SHALL close a sprint only once every unfinished member has an outcome of Carry, Back to the backlog or Drop.

#### Scenario: An item without an outcome

- **Given** Sprint 4 has two unfinished members, one marked Carry
- **When** the person closes Sprint 4
- **Then** the sprint stays open and the member without an outcome is pointed out

### Requirement: Closing starts the next sprint

```meta
type: requirement
status: draft
```

The system SHALL start the next sprint when it closes one, with the next goal given at closing and every carried task as a member.

#### Scenario: Closing Sprint 4

- **Given** Sprint 4 has one member marked Carry, one marked Back to the backlog and one marked Drop
- **When** the person closes Sprint 4 with the next goal "Ship the sprint view"
- **Then** Sprint 5 is open from Monday 26 October to Sunday 8 November 2026, with the goal "Ship the sprint view"
- **And** Sprint 5 has the carried task as a member, and neither of the other two

### Requirement: No outcome changes the task

```meta
type: requirement
status: draft
```

The system SHALL leave a task's status, tags and plan unchanged whichever close outcome it is given.

#### Scenario: A dropped task

- **Given** an In progress member of Sprint 4 marked Drop
- **When** Sprint 4 closes
- **Then** the task is still In progress
- **And** Sprint 4 records it as dropped

## Sprints in the briefs

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#sprints-in-the-briefs]
```

### Requirement: A series shows only in the briefs it chose

```meta
type: requirement
status: draft
```

The system SHALL show a series' open sprint in the daily brief and in the weekly brief only when the series chose that brief.

#### Scenario: Weekly only

- **Given** a series that shows in the weekly brief and not in the daily brief
- **When** the person reads the daily brief and then the weekly brief
- **Then** only the weekly brief names the series' open sprint
