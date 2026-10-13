# Invariants

```meta
status: draft
type: invariants
```

What each aggregate and domain service on [`domain.md`](domain.md) enforces, one chapter each: each rule, where it is enforced, and the unit test that proves it. Progress is not built yet, so no rule names a test.

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
```

A series always belongs to one repository or more; removing its last repository is refused (`progress.series_without_repository`).
A series over no repository has no tasks to gather and no view to replace.

Enforced at: constructor, `RemoveRepository()`

### Invariant: A series has at most one open sprint

```meta
status: draft
type: invariant
```

At most one sprint of a series is open at a time; starting a sprint while another is open is refused (`progress.sprint_already_open`).

Enforced at: `StartSprint()`, `CloseSprint()`

### Invariant: Sprints are numbered without gaps

```meta
status: draft
type: invariant
```

A new sprint's number is one more than the highest number in the series, and the first sprint is number 1.

Enforced at: `StartSprint()`, `CloseSprint()`

### Invariant: A sprint's window matches the series' cadence

```meta
status: draft
type: invariant
```

A sprint's window starts on the series' start weekday and lasts exactly the series' sprint length, 7 or 14 days (`progress.window_off_cadence`).

Enforced at: `Sprint Window`

### Invariant: Sprints of a series do not overlap

```meta
status: draft
type: invariant
```

A new sprint starts after the last day of the sprint before it (`progress.sprints_overlap`).

Enforced at: `StartSprint()`, `CloseSprint()`

### Invariant: A closed sprint does not change

```meta
status: draft
type: invariant
```

A closed sprint's members, plan links, goal, notes, marks and outcomes are kept as they were at closing; a change to them is refused (`progress.sprint_closed`).
What a sprint held when it ended is the record of how it ended.

Enforced at: all mutations of a `Sprint`

### Invariant: Closing settles every unfinished member

```meta
status: draft
type: invariant
```

A sprint closes only when every member that is not done has exactly one close outcome of Carry, Back to the backlog or Drop (`progress.unsettled_item`).

Enforced at: `CloseSprint()`

### Invariant: Closing starts the next sprint

```meta
status: draft
type: invariant
```

Closing a sprint starts the series' next sprint in the same change, with the next goal given at closing and every carried task as a directly added member.

Enforced at: `CloseSprint()`

### Invariant: A note is one line

```meta
status: draft
type: invariant
```

A sprint item's note holds no line break (`progress.note_not_one_line`).

Enforced at: `Sprint Item`

### Invariant: A name pattern carries the number

```meta
status: draft
type: invariant
```

A name pattern contains `{n}` once and is not blank otherwise (`progress.pattern_without_number`).
Without the number, two sprints of a series would carry the same name.

Enforced at: `Name Pattern`

### Invariant: A goal share is a whole percentage

```meta
status: draft
type: invariant
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
```

Giving a series a repository that another series already has is refused (`progress.repository_in_other_series`).
A repository with two cadences would have two open sprints, and its view could show neither honestly.

Enforced at: `Assign()`
