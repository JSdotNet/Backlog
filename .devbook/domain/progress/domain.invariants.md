# Invariants

```meta
status: draft
type: invariants
```

What each aggregate and domain service on [`domain.md`](domain.md) enforces, one chapter each: each rule, where it is enforced, and the unit test that proves it. Each rule names the unit tests in `Backlog.Modules.Progress.UnitTests` that prove it.

## Sprint Series

```meta
status: draft
type: invariants
related: [.devbook/domain/progress/domain.md#sprint-series]
```

> The rules the Sprint Series holds its sprints, items and settings to, including those its [Sprint Window](domain.md#sprint-window), [Name Pattern](domain.md#name-pattern) and [Goal Threshold](domain.md#goal-threshold) enforce.

### Invariant: A series has at least one repository

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.A_series_over_no_repository_is_refused, unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.Removing_a_series_last_repository_is_refused]
```

A series always belongs to one repository or more; removing its last repository is refused (`progress.series_without_repository`).
A series over no repository has no tasks to gather and no view to replace.

Enforced at: constructor, `RemoveRepository()`

### Invariant: A series has at most one open sprint

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.Starting_a_sprint_while_one_is_open_is_refused
```

At most one sprint of a series is open at a time; starting a sprint while another is open is refused (`progress.sprint_already_open`).

Enforced at: `StartSprint()`, `CloseSprint()`

### Invariant: Sprints are numbered without gaps

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.Sprints_are_numbered_from_one_without_gaps
```

A new sprint's number is one more than the highest number in the series, and the first sprint is number 1.

Enforced at: `StartSprint()`, `CloseSprint()`

### Invariant: A sprint lasts one or two weeks

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.A_sprint_lasts_one_or_two_weeks, unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.A_sprint_of_any_other_length_is_refused]
```

A series' sprint length is one week or two weeks, and no window of another length can be made (`progress.length_not_allowed`).

Enforced at: constructor, `ChangeCadence()`, `Sprint Window`

### Invariant: A sprint's window matches the series' cadence

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.A_sprint_starts_on_the_series_start_weekday, unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.A_sprint_window_lasts_exactly_the_series_length]
```

A sprint's window starts on the series' start weekday and lasts exactly the series' sprint length, 7 or 14 days (`progress.window_off_cadence`).

Enforced at: `Sprint Window`

### Invariant: Sprints of a series do not overlap

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.A_sprint_starting_inside_the_one_before_it_is_refused, unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.Each_sprint_starts_the_day_after_the_one_before_it_ends]
```

A new sprint starts after the last day of the sprint before it (`progress.sprints_overlap`).

Enforced at: `StartSprint()`, `CloseSprint()`

### Invariant: Sprints of a series follow without a gap

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.A_sprint_leaving_a_gap_after_the_one_before_it_is_refused, unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.A_changed_weekday_starts_the_next_sprint_on_the_first_such_day_after_the_last]
```

A new sprint starts on the first start weekday after the last day of the sprint before it, and no later (`progress.sprints_not_contiguous`).
With an unchanged cadence that is the day after; a weekday changed since leaves only the days before that weekday between them.

Enforced at: `StartSprint()`, `CloseSprint()`

### Invariant: A closed sprint does not change

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.A_closed_sprint_refuses_every_change, unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesPlanningTests.A_closed_sprint_refuses_a_change_through_the_port]
```

A closed sprint's members, plan links, goal, notes, marks and outcomes are kept as they were at closing; a change to them is refused (`progress.sprint_closed`).
What a sprint held when it ended is the record of how it ended.

Enforced at: all mutations of a `Sprint`

### Invariant: Closing settles every unfinished member

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.Closing_with_an_unfinished_member_without_an_outcome_is_refused, unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesPlanningTests.Closing_is_refused_until_every_unfinished_member_has_an_outcome]
```

A sprint closes only when every member that is not done has exactly one close outcome of Carry, Back to the backlog or Drop (`progress.unsettled_item`).

Enforced at: `CloseSprint()`

### Invariant: Closing starts the next sprint

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.Closing_starts_the_next_sprint_with_the_next_goal_and_only_the_carried_tasks, unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.A_carried_item_starts_the_next_sprint_without_its_note_or_question]
```

Closing a sprint starts the series' next sprint in the same change, with the next goal given at closing and every carried task as a directly added member.

Enforced at: `CloseSprint()`

### Invariant: A note is one line

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.A_note_with_a_line_break_is_refused
```

A sprint item's note holds no line break (`progress.note_not_one_line`).

Enforced at: `Sprint Item`

### Invariant: A name pattern carries the number

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.A_name_pattern_without_the_number_once_is_refused
```

A name pattern contains `{n}` once and is not blank otherwise (`progress.pattern_without_number`).
Without the number, two sprints of a series would carry the same name.

Enforced at: `Name Pattern`

### Invariant: A goal share is a whole percentage

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.A_goal_share_outside_one_to_a_hundred_is_refused
```

A goal threshold's share is a whole number from 1 to 100 (`progress.share_out_of_range`).

Enforced at: `Goal Threshold`

## Series Assignment

```meta
status: draft
type: invariants
related: [.devbook/domain/progress/domain.md#series-assignment]
```

> The rule that spans every series.

### Invariant: A repository belongs to at most one series

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.A_repository_belongs_to_at_most_one_series, unit:dotnet:Backlog.Modules.Progress.UnitTests.SprintSeriesInvariantTests.A_new_series_over_a_repository_another_series_has_is_refused]
```

Giving a series a repository that another series already has is refused (`progress.repository_in_other_series`).
A repository with two cadences would have two open sprints, and its view could show neither honestly.

Enforced at: `Assign()`

### Invariant: A synced repository keeps to the series set up first

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.SeriesClaimTests.The_series_set_up_first_keeps_the_repository, unit:dotnet:Backlog.Modules.Progress.UnitTests.SeriesClaimTests.A_switched_on_series_keeps_the_repository_from_a_switched_off_one, unit:dotnet:Backlog.Modules.Progress.UnitTests.SeriesClaimTests.Switching_on_a_series_whose_repository_another_has_is_refused, unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.SprintSyncBetweenDevicesTests.Two_desktops_that_set_up_a_series_for_one_repository_offline_keep_it_in_the_first]
```

Two desktops that each give one repository to a series while offline both keep their series after syncing, which is the one way a repository comes to be in two.
When that leaves a repository in two switched-on series, the series that holds it is decided by the series' documents alone, so every desktop decides it the same way:
a switched-on series before a switched-off one, then the series set up first, then the lower id.
Every other switched-on series that has the repository is switched off with its [Off Reason](domain.md#series-switch), keeping its sprints and settings.
Switching a series on while another switched-on series has one of its repositories is refused (`progress.repository_in_other_series`).

Enforced at: `ResolveClaims()`, `HolderOf()`, `SwitchOn()`
