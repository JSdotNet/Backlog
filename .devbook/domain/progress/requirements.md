# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it.

Progress is partly built: a requirement names its tests where it has them. The scenarios use a
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

### Requirement: A switched-off series' repositories keep the In progress view

```meta
type: requirement
status: draft
```

The system SHALL treat the repositories of a switched-off series as in no series when it is asked which series a repository works to, and SHALL keep them out of every other series.

#### Scenario: Switching a series off

- **Given** the repository `backlog` belongs to the series "Core", which has an open sprint
- **When** the person switches "Core" off
- **Then** `backlog` reads as in no series, and "Core" keeps its repositories and its open sprint

#### Scenario: A switched-off series still holds its repository

- **Given** the series "Core" is switched off and has `backlog`
- **When** the person adds `backlog` to the series "Docs"
- **Then** the change is refused and `backlog` stays in "Core"

### Requirement: A deleted series frees its repositories

```meta
type: requirement
status: draft
```

The system SHALL, once the person confirms deleting a series, remove the series and every sprint it recorded, leave every task as it was, and treat its repositories as in no series.

#### Scenario: Deleting a series with sprints

- **Given** the series "Core" over `backlog` and `docs`, with Sprint 4 open
- **When** the person deletes "Core" and confirms
- **Then** "Core" and its four sprints are gone, every task of Sprint 4 is unchanged, and `backlog` and `docs` show under "Not in a sprint"

#### Scenario: Cancelling the delete

- **Given** the series "Core" over `backlog`
- **When** the person deletes "Core" and cancels
- **Then** "Core" stays as it was

#### Scenario: A freed repository joins another series

- **Given** the series "Core" over `backlog` was deleted
- **When** the person adds `backlog` to the series "Docs"
- **Then** `backlog` belongs to "Docs"

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

#### Scenario: Adding a removed task again starts it clean

- **Given** a task added directly to Sprint 4, marked as a goal task and given a note, that no linked plan brings in
- **When** the person removes it from Sprint 4 and adds it again
- **Then** it is in Sprint 4 with no goal mark, no note and no open question

#### Scenario: Removing a task a linked plan still brings in

- **Given** a task added directly to Sprint 4 and marked as a goal task, whose plan is linked to Sprint 4
- **When** the person removes it from Sprint 4
- **Then** it is still in Sprint 4, brought in by its plan, and keeps its goal mark

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

### Requirement: The daily strip shows only for the open sprint of a series in the daily

```meta
type: requirement
status: draft
related: [.devbook/domain/progress/features.md#the-daily-strip]
```

The system SHALL draw the Since yesterday strip on the sprint view only while the open sprint is shown and its series shows in the daily brief.

#### Scenario: A series in the daily

- **Given** a series that shows in the daily brief, with Sprint 4 open
- **When** the person opens the sprint view on Sprint 4
- **Then** the Since yesterday strip is drawn above the goal card

#### Scenario: A series not in the daily

- **Given** a series that does not show in the daily brief
- **When** the person opens the sprint view
- **Then** no Since yesterday strip is drawn

### Requirement: Since yesterday is since the previous working day

```meta
type: requirement
status: draft
related: [.devbook/domain/progress/features.md#the-daily-strip]
```

The system SHALL count as since yesterday what happened on the previous working day or later, Friday counting as Monday's previous working day.

#### Scenario: Monday reads back to Friday

- **Given** a member done on Friday 16 October and another done on Thursday 15 October
- **When** the person reads the strip on Monday 19 October
- **Then** Since yesterday names the member done on Friday and not the one done on Thursday

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
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesPlanningTests.Closing_starts_the_next_sprint_with_only_the_carried_task_and_leaves_every_task_alone, unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesPlanningTests.Closing_early_starts_the_next_sprint_after_the_closed_ones_window_not_after_today, unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintViewTests.Before_its_first_working_day_an_open_sprint_reads_when_it_starts_and_how_many_working_days_it_has]
```

The system SHALL start the next sprint when it closes one, with the next goal given at closing and every carried task as a member.

#### Scenario: Closing Sprint 4

- **Given** Sprint 4 has one member marked Carry, one marked Back to the backlog and one marked Drop
- **When** the person closes Sprint 4 with the next goal "Ship the sprint view"
- **Then** Sprint 5 is open from Monday 26 October to Sunday 8 November 2026, with the goal "Ship the sprint view"
- **And** Sprint 5 has the carried task as a member, and neither of the other two

#### Scenario: Closing Sprint 4 early

- **Given** Sprint 4 runs from Monday 12 October to Sunday 25 October 2026 and today is Monday 19 October
- **When** the person closes Sprint 4
- **Then** Sprint 5 is open from Monday 26 October to Sunday 8 November 2026
- **And** Sprint 4 keeps its window to Sunday 25 October
- **And** the sprint view reads "Starts Mon 26 Oct · 10 working days" until Monday 26 October

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

## Syncing sprints between devices

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#syncing-sprints-between-devices]
```

The scenarios use two paired desktops, A and B, with sync on.

### Requirement: A change reaches the other desktop

```meta
type: requirement
status: draft
```

The system SHALL hold, on every paired desktop, the same series a change was made to on one of them, once both have synced.

#### Scenario: A note written on one desktop

- **Given** the series "Core" on A and B, with Sprint 4 open
- **When** the person writes the note "Waiting on review" on a member on A, and A and then B sync
- **Then** B shows the member with the note "Waiting on review"

### Requirement: The newer change wins per series

```meta
type: requirement
status: draft
```

The system SHALL keep, for each series, the change saved last when two desktops changed that series before either synced, and SHALL keep changes to two different series made on two desktops.

#### Scenario: One series changed on both desktops

- **Given** the series "Core" on A and B
- **When** the person writes a note on A, then writes another on the same member on B, and both sync
- **Then** both desktops show the note written on B

#### Scenario: Two series changed on two desktops

- **Given** the series "Core" and "Docs" on A and B
- **When** the person changes Sprint 4's goal of "Core" on A and of "Docs" on B, and both sync
- **Then** both desktops show both new goals

### Requirement: A pulled series shows at once

```meta
type: requirement
status: draft
```

The system SHALL redraw, on the desktop a series arrives at, every screen that shows that series, without the person moving away or pressing Refresh, and SHALL keep what the person has typed or chosen in a dialog left open.

#### Scenario: A task added on one desktop

- **Given** the series "Core" on A and B, with Sprint 4 open in B's sprint view and in B's Settings › Sprints
- **When** the person adds a task to Sprint 4 and renames the series "Platform" on A, and A and then B sync
- **Then** B's sprint view lists the task, its Sprint section reads "In Sprint 4", its weekly brief counts it, and its settings card reads "Platform"

#### Scenario: A close under way on the other desktop

- **Given** B's Close dialog open on Sprint 4, one item set to Drop and the next goal typed
- **When** a task is added to Sprint 4 on A, and A and then B sync
- **Then** B's dialog lists the new item on Carry, and still shows Drop and the typed goal

### Requirement: A desktop with no series sends none

```meta
type: requirement
status: draft
```

The system SHALL send no series from a desktop that never set one up.

#### Scenario: A newly paired desktop

- **Given** the series "Core" on A, and B newly paired with no series
- **When** B syncs
- **Then** B holds "Core" as A has it, and A still holds its own "Core"

### Requirement: A deleted series is deleted on every desktop

```meta
type: requirement
status: draft
```

The system SHALL remove a series from every paired desktop once one desktop deleted it and both have synced, unless the series was changed on another desktop after the deletion.

#### Scenario: Deleted on one desktop

- **Given** the series "Core" on A and B
- **When** the person deletes "Core" on A, and A and then B sync
- **Then** neither desktop holds "Core", and its repositories are in no series on both

#### Scenario: A change older than the deletion

- **Given** the series "Core" on A and B
- **When** the person writes a note on B, then deletes "Core" on A, and both sync
- **Then** neither desktop holds "Core"

### Requirement: A repository two desktops gave to two series stays in the first

```meta
type: requirement
status: draft
```

The system SHALL, when syncing leaves a repository in two switched-on series, keep it working to the series set up first and switch the other off with a line saying why, on every paired desktop alike, without asking the person and without losing a sprint.

#### Scenario: Two desktops set up a series for one repository offline

- **Given** A and B are both offline, A sets up the series "Core" over `backlog`, and later B sets up the series "Main" over `backlog`
- **When** A and B both sync, in either order
- **Then** both desktops show `backlog` working to "Core", and "Main" switched off with the line `Switched off: backlog already works to "Core", which was set up first.`, and "Main" keeps every sprint it recorded

#### Scenario: The switched-off series cannot be switched back on

- **Given** "Main" was switched off because `backlog` works to "Core"
- **When** the person switches "Main" on
- **Then** the change is refused and "Main" stays off

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
