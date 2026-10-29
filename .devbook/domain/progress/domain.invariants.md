# Invariants

```meta
status: draft
type: invariants
```

What each aggregate and domain service on [`domain.md`](domain.md) enforces, one chapter each: each rule, where it is enforced, and the unit test that proves it once the rule is built.

## Goal

```meta
status: draft
type: invariants
related: [.devbook/domain/progress/domain.md#goal]
```

> The rules a goal holds itself and its attachments to, including those its [Goal Attachment](domain.md#goal-attachment) enforces.

### Invariant: A goal has a title

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalInvariantTests.A_goal_without_a_title_is_refused
```

A goal's title is not blank (`progress.goal_without_title`).

Enforced at: constructor, `Rename()`

### Invariant: An outcome is one line

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalInvariantTests.An_outcome_of_more_than_one_line_is_refused
```

A goal's outcome holds no line break (`progress.outcome_not_one_line`).

Enforced at: constructor, `ChangeOutcome()`

### Invariant: An item is attached to a goal once

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalInvariantTests.Attaching_an_item_again_changes_nothing_and_keeps_its_first_day, unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalInvariantTests.An_attachment_is_equal_by_kind_and_identity_alone]
```

A goal holds at most one attachment of the same kind and identity; attaching it again changes nothing.

Enforced at: `Attach()`

### Invariant: A pull request attachment names its repository

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalInvariantTests.A_pull_request_without_a_repository_or_a_number_is_refused
```

A pull request attachment carries a repository and a positive number (`progress.pull_request_without_repository`).
A bare number names a different pull request in every repository.

Enforced at: `Goal Attachment`

### Invariant: A session attachment names its agent and session id

```meta
status: draft
type: invariant
related: [.devbook/domain/sessions/domain.md#session-identity, .devbook/arc42/adr/0037-a-session-attaches-to-a-goal-as-its-own-item.md]
```

A session attachment carries an agent and a non-blank session id (`progress.session_without_identity`).
A session id is unique only within the agent that issued it.

Enforced at: `Goal Attachment`

## Goal Assignment

```meta
status: draft
type: invariants
related: [.devbook/domain/progress/domain.md#goal-assignment]
```

> The rule that spans every goal.

### Invariant: An item belongs to at most one goal

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalInvariantTests.Attaching_an_item_another_goal_holds_is_refused_naming_that_goal, unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalInvariantTests.A_plan_or_a_pull_request_belongs_to_one_goal_too, unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalInvariantTests.Moving_an_item_takes_it_out_of_the_other_goal, unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalInvariantTests.Attaching_to_the_goal_that_holds_it_already_is_no_conflict, unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalInvariantTests.A_task_another_goal_holds_through_its_plan_may_be_attached_directly]
```

Attaching a plan, task, pull request or session that another goal already holds is refused (`progress.item_in_other_goal`), naming that goal, unless the attach asks to move it (`moveFromOtherGoal`): then the item leaves the other goal in the same change.
An item counted in two goals would move both when it is done.

Enforced at: `Assign()`

## Impediment

```meta
status: draft
type: invariants
related: [.devbook/domain/progress/domain.md#impediment]
```

> The rules an impediment holds its state and history to, including those its [State Change](domain.md#state-change) enforces.

### Invariant: An impediment has a title

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Progress.UnitTests.ImpedimentInvariantTests.An_impediment_has_a_title
```

An impediment's title is not blank (`progress.impediment_without_title`).

Enforced at: constructor, `Rename()`

### Invariant: An impediment starts New

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Progress.UnitTests.ImpedimentInvariantTests.An_impediment_starts_New_and_its_history_opens_with_that_step
```

A new impediment is in state New, and its history opens with that step.

Enforced at: constructor

### Invariant: An impediment moves forward along its states

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.ImpedimentInvariantTests.An_impediment_moves_forward_along_its_states, unit:dotnet:Backlog.Modules.Progress.UnitTests.ImpedimentInvariantTests.Any_other_move_is_refused_and_changes_nothing, unit:dotnet:Backlog.Modules.Progress.UnitTests.ImpedimentInvariantTests.Moving_an_escalated_impediment_back_to_Reported_is_refused]
```

An impediment moves only from New to Reported, from Reported to Escalated or Being handled, and from Escalated to Being handled; any other move is refused (`progress.impediment_move_not_allowed`).
Escalated is the one state a move may skip.

Enforced at: `MoveTo()`

### Invariant: Any open impediment may be resolved

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Progress.UnitTests.ImpedimentInvariantTests.Any_open_impediment_may_be_resolved
```

An impediment in any state but Resolved may move to Resolved.

Enforced at: `MoveTo()`

### Invariant: A resolved impediment stays resolved

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Progress.UnitTests.ImpedimentInvariantTests.A_resolved_impediment_stays_resolved
```

A resolved impediment moves to no other state (`progress.impediment_resolved`).
A blocker that comes back is a new impediment with its own history.

Enforced at: `MoveTo()`

### Invariant: Every move is recorded

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.ImpedimentInvariantTests.Every_move_records_its_day_and_who_it_is_with, unit:dotnet:Backlog.Modules.Progress.UnitTests.ImpedimentInvariantTests.The_history_days_never_go_backwards]
```

Each change of state adds one state change with its state and day, and the history's days never go backwards: a move dated before the last step is refused (`progress.impediment_move_before_last_step`).

Enforced at: `MoveTo()`, `State Change`

### Invariant: An item is held back once by one impediment

```meta
status: draft
type: invariant
related: [.devbook/domain/progress/domain.md#holds-back]
tests: unit:dotnet:Backlog.Modules.Progress.UnitTests.ImpedimentInvariantTests.A_task_is_held_back_once_and_released_by_id
```

An impediment holds back each item at most once by its kind and identity; linking the same task, the same pull request or the same plan again changes nothing.
A task id, a pull request and a plan tag never equal one another, whatever their values.

Enforced at: `HoldBack()`

### Invariant: A held-back item names its whole identity

```meta
status: draft
type: invariant
related: [.devbook/domain/progress/domain.md#holds-back, .devbook/domain/progress/domain.invariants.md#invariant-a-pull-request-attachment-names-its-repository]
```

A held-back task carries a task id, a held-back pull request a repository and a positive number, and a held-back plan a non-blank plan tag without its sigil (`progress.held_back_item_without_identity`).
A bare pull request number names a different pull request in every repository.

Enforced at: `Holds Back`

### Invariant: Unlinking an item it does not hold back changes nothing

```meta
status: draft
type: invariant
related: [.devbook/domain/progress/domain.md#holds-back]
```

Releasing an item the impediment does not hold back leaves its held-back items and its history unchanged.

Enforced at: `Release()`

### Invariant: Linking and unlinking leave the state and history alone

```meta
status: draft
type: invariant
related: [.devbook/domain/progress/domain.md#holds-back, .devbook/domain/progress/domain.md#state-change]
```

Holding back or releasing an item adds no state change and leaves the impediment's state as it was.
What an impediment holds back is not a step in its history.

Enforced at: `HoldBack()`, `Release()`

## Stand-up Reading

```meta
status: draft
type: invariants
related: [.devbook/domain/progress/domain.md#stand-up-reading, .devbook/domain/progress/domain.md#ready-to-resolve, .devbook/domain/progress/domain.md#settled]
```

> The rules the reading keeps when it says whether an impediment is [Ready to resolve](domain.md#ready-to-resolve).

### Invariant: Ready to resolve needs every held-back item settled

```meta
status: draft
type: invariant
```

An open impediment reads Ready to resolve only when every item it holds back is [settled](domain.md#settled).
One item that is not settled, or that cannot be read, keeps it from reading Ready to resolve.

Enforced at: `Stand-up Reading`

### Invariant: An impediment that holds back nothing is never ready to resolve

```meta
status: draft
type: invariant
```

An impediment that holds back no item never reads Ready to resolve.
With nothing linked, nothing can tell that it is out of the way.

Enforced at: `Stand-up Reading`

### Invariant: A resolved impediment is never ready to resolve

```meta
status: draft
type: invariant
```

Only an impediment that is not Resolved reads Ready to resolve.

Enforced at: `Stand-up Reading`

### Invariant: Reading Ready to resolve moves nothing

```meta
status: draft
type: invariant
related: [.devbook/domain/progress/domain.md#impediment-state]
```

Reading an impediment as Ready to resolve leaves its state, its history and its held-back items as they were, and stores nothing.
Only a person resolves an impediment.

Enforced at: `Stand-up Reading`

## Iteration

```meta
status: draft
type: invariants
related: [.devbook/domain/progress/domain.md#shared-value-objects, .devbook/domain/progress/domain.md#iteration]
```

> The rules every iteration window keeps, whatever its length. The
> [Iteration](domain.md#iteration) is the one shared value object in this context, so
> this chapter holds the rules of the `## Shared Value Objects` grouping under its name.

### Invariant: An iteration has one of four lengths

```meta
status: draft
type: invariant
```

An iteration's length is Day, Week, Two weeks or Month (`progress.length_not_allowed`).

Enforced at: `Iteration`

### Invariant: A week runs Monday to Friday

```meta
status: draft
type: invariant
```

A Week window starts on a Monday and ends on the Friday after it.

Enforced at: `Iteration`

### Invariant: Two-week windows keep to their start

```meta
status: draft
type: invariant
```

A Two weeks window starts on the two-week start or a whole number of fourteen days from it, and lasts fourteen days; without a start there is no such window (`progress.two_weeks_without_start`).

Enforced at: `Iteration`

### Invariant: A month is a calendar month

```meta
status: draft
type: invariant
```

A Month window starts on the first day of a calendar month and ends on its last day.

Enforced at: `Iteration`

### Invariant: Last comes just before next

```meta
status: draft
type: invariant
```

The last window is the window of the same length directly before the next window, and no window of that length lies between them.

Enforced at: `Iteration`
