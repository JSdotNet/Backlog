# Invariants

```meta
status: draft
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate: each rule, where it is enforced, and the unit test that proves it.

## Task

```meta
status: draft
type: invariants
related: [.devbook/domain/tasks/domain.md#task]
```

> The rules the Task holds its status, its [sub-items](domain.md#sub-item) and its history to.

### Invariant: A new task starts in draft

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Tasks.UnitTests.TaskItemLifecycleTests.ManuallyCreatedEntry_DefaultsToDraft_WithNoSourceInbox
```

A task created by hand starts at `draft` with no `source_inbox_id`.

Enforced at: constructor

### Invariant: Status changes follow the lifecycle

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.TaskItemLifecycleTests.InvalidTransitions_Throw, unit:dotnet:Backlog.Modules.Tasks.UnitTests.TaskItemLifecycleTests.ValidTransitions_AreAllowed, unit:dotnet:Backlog.Modules.Tasks.UnitTests.EntryStatusFlowTests.The_graph_is_the_one_the_domain_folder_draws]
```

A status change through `ChangeStatus()` moves only along the [Task lifecycle](flow.md#task-lifecycle).

Enforced at: `ChangeStatus()`

### Invariant: Sub-items change through the task

```meta
status: draft
type: invariant
```

A sub-item changes only through its task.

Enforced at: all mutations (`SubItem`'s constructor and setters are internal)

### Invariant: Projection references change through the task

```meta
status: draft
type: invariant
```

A task's projection references change only through the task.

Enforced at: all mutations (`ProjectionRefs` is read-only, and changes only through `AddProjectionRef()` and `RemoveProjectionRef()`)

### Invariant: The AI work log changes through the task

```meta
status: draft
type: invariant
```

```annotation
kind: question
author: "flow-spec"
date: 2026-10-13
body: |
  Not built: no type in src/ enforces this yet. Which type and transition will?
```

A task's [AI work log](domain.md#ai-work-log) events change only through the task.

Enforced at: open

### Invariant: Usage events change through the task

```meta
status: draft
type: invariant
```

A task's usage events are recorded only through the task.

Enforced at: all mutations (`UsageEvents` is read-only, and grows only through `RecordUsage()`)

### Invariant: Progress follows the sub-items

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.TaskItemSubItemTests.ParentProgress_ReflectsSubItemCompletion, unit:dotnet:Backlog.Modules.Tasks.UnitTests.TaskItemSubItemTests.Progress_IsZero_WhenNoSubItems]
```

A task's progress is the share of its sub-items that are done, and zero when it has none.

Enforced at: `Progress`

### Invariant: A blank area is unfiled

```meta
status: draft
type: invariant
```

A blank or whitespace-only area is stored as no area, which reads as unfiled, and any other area is stored trimmed.

Enforced at: `SetArea()`, which every path that writes an area goes through

### Invariant: Effort is never negative

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.TaskItemEffortTests.SetEffort_Negative_IsRefusedAndLeavesTheValueUnchanged, unit:dotnet:Backlog.Modules.Tasks.UnitTests.TaskItemEffortTests.SetEffort_HoldsAnyNonNegativeValue]
```

A task's Effort is absent or a whole number of zero or more; a negative estimate is refused and leaves the effort as it was.

Enforced at: `SetEffort()`

### Invariant: Readiness is derived and never stored

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskPromptTests.A_task_is_ready_when_everything_it_named_is_finished, unit:dotnet:Backlog.UI.Components.UnitTests.TaskPromptTests.Finishing_one_step_frees_every_row_that_named_it]
```

A task's Readiness is worked out from its `depends_on` and the tasks those ids name on every read, and is never stored on the task.
A stored readiness would go stale the moment a predecessor was completed.

Enforced at: `TaskChain.Resolve()`, a stateless function in `Backlog.UI.Components.Tasks` rather than in the Tasks module; `TaskItem` holds no readiness

### Invariant: A ticked task reads done

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.UI.Components.UnitTests.TaskPromptTests.Done_wins_over_an_outstanding_dependency
```

A [Completed](domain.md#completed) task, one the person has ticked off, reads `done` and waits on nothing, even when a task it names is still outstanding.
Done is a recorded fact where blocked is only a conclusion.

Enforced at: `TaskChain.Resolve()` in `Backlog.UI.Components.Tasks`, over rows whose `Done` the Tasks pane sets from the tick (`completed_on`) and not from `Task Status`

### Invariant: A dependency naming no task still blocks

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.UI.Components.UnitTests.TaskPromptTests.An_id_that_names_nothing_here_leaves_the_task_blocked
```

A task whose `depends_on` names an id that resolves to no task reads `blocked`, the same as one waiting on an unfinished task.
Treating a missing step as satisfied would let a chain claim to be ready when the step is only out of view.

Enforced at: `TaskChain.Resolve()` in `Backlog.UI.Components.Tasks`; `SetDependsOn()` stores ids without checking what they name

### Invariant: Every task in a dependency loop reads blocked

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskPromptTests.Every_task_in_a_cycle_is_blocked_and_flagged_and_nothing_is_next, unit:dotnet:Backlog.UI.Components.UnitTests.TaskPromptTests.A_task_that_names_itself_is_a_cycle_of_one]
```

Every task in a dependency loop, including a task that names itself, reads `blocked` and is flagged as in a loop.
Nothing refuses the loop when it is written, so this is where it surfaces.

Enforced at: `TaskChain.Resolve()` in `Backlog.UI.Components.Tasks`

### Invariant: The first move into progress stamps the start

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.TaskItemStartedOnTests.Moving_to_in_progress_stamps_the_day_it_is_given, unit:dotnet:Backlog.Modules.Tasks.UnitTests.TaskItemStartedOnTests.Setting_the_status_to_in_progress_stamps_the_day_it_is_given, unit:dotnet:Backlog.Modules.Tasks.UnitTests.TaskItemStartedOnTests.Leaving_in_progress_and_coming_back_on_a_later_day_keeps_the_first_day]
```

A status change that puts a task in `in_progress` stamps `started_on` with the caller's local date when the task has none, and never moves a date it already holds.
A task paused back to `ready` or reopened from `done` keeps the day it was first started.

Enforced at: `ChangeStatus()` and `SetStatus()`

### Invariant: A task outside My Day holds no agenda time

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Tasks.UnitTests.AgendaTimeTests.A_task_holds_no_agenda_time_outside_my_day
```

A task with no `in_my_day_on` holds no [Agenda Time](domain.md#agenda-time); one given to it is dropped rather than refused.
An agenda time has no date of its own, so without a My Day date it places the task nowhere.

Enforced at: `SetAgendaTime()`

### Invariant: A new My Day date drops the agenda time

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.AgendaTimeTests.Moving_the_task_to_another_day_drops_its_slot, unit:dotnet:Backlog.Modules.Tasks.UnitTests.AgendaTimeTests.Taking_the_task_out_of_my_day_drops_its_slot, unit:dotnet:Backlog.Modules.Tasks.UnitTests.AgendaTimeTests.Setting_the_same_day_again_keeps_the_slot]
```

Changing or clearing a task's `in_my_day_on` drops its agenda time, and setting the same date again keeps it.
The slot belonged to the old day, so a task moved to tomorrow or taken out of My Day loses it.

Enforced at: `SetInMyDayOn()`

### Invariant: A text save that moves My Day drops the slot carried along

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.AgendaTimeTests.A_save_that_changes_the_day_drops_the_slot_carried_along_unedited, unit:dotnet:Backlog.Modules.Tasks.UnitTests.AgendaTimeTests.A_save_that_changes_the_day_and_types_a_new_slot_keeps_the_new_slot]
```

A text save that changes `myday:` drops the `at:` and `for:` left on the line unchanged, and keeps a slot typed or changed in the same save.
The unchanged tokens are the old day's slot carried along; a changed one is the person's.

Enforced at: `TaskEntryFields.ApplyToExisting()`, which every text save of an existing task goes through

### Invariant: An agenda duration is a positive whole number of minutes

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Tasks.UnitTests.AgendaTimeTests.A_duration_must_be_positive
```

An agenda time's duration is a whole number of minutes greater than zero; zero or less is refused.

Enforced at: constructor (`AgendaTime`)

### Invariant: A start alone is thirty minutes

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.AgendaTimeTests.A_start_without_a_duration_means_thirty_minutes, unit:dotnet:Backlog.Modules.Tasks.UnitTests.AgendaTimeTests.A_start_alone_reads_as_thirty_minutes]
```

An agenda time given a start and no duration runs for 30 minutes.

Enforced at: constructor (`AgendaTime`, `DefaultDurationMinutes`), and `EntryTextParser.Parse()` for an `at:` written without `for:`

### Invariant: A duration without a start is no agenda time

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.AgendaTimeTests.A_duration_without_a_start_is_refused_as_malformed, unit:dotnet:Backlog.Modules.Tasks.UnitTests.AgendaTimeTests.A_save_of_a_lone_duration_sets_no_slot]
```

A duration given without a start is no agenda time: the parser refuses a lone `for:`, and the task is left with no slot.
The start is what places the task in its day.

Enforced at: constructor (`AgendaTime` has no form without a start), and `EntryTextParser.Parse()`

### Invariant: A planned-hours figure is more than none and at most a day

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Tasks.UnitTests.SetPlannedHoursTests.A_figure_out_of_range_is_refused_and_nothing_is_written
```

A [Planned Hours](domain.md#planned-hours) figure is more than zero and at most 24 hours; a figure outside that range is refused and the task's figures stay as they were.

Enforced at: `PlannedHoursBlock.Create()` and `TryCreate()`; `TaskItem` holds only blocks that were created

### Invariant: A planned-hours figure is kept in quarters of an hour

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Tasks.UnitTests.SetPlannedHoursTests.A_figure_is_kept_in_quarters_of_an_hour
```

A planned-hours figure is rounded to the nearest quarter of an hour, so a typed 2.4 is kept as 2.5.
The person then sees again a figure the editor can show.

Enforced at: `PlannedHoursBlock.TryCreate()`

### Invariant: A task holds one planned-hours figure a day

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Tasks.UnitTests.SetPlannedHoursTests.A_day_holds_one_block_and_the_last_figure_wins
```

A task holds at most one planned-hours figure for a day, the last one given winning, and keeps its figures in date order.

Enforced at: `SetPlannedHours()`

### Invariant: Every edit restamps the task

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.TaskItemStampTests.An_edit_moves_the_stamp, unit:dotnet:Backlog.Modules.Tasks.UnitTests.TaskItemStampTests.A_non_edit_leaves_the_stamp_alone, unit:dotnet:Backlog.Modules.Tasks.UnitTests.TaskItemStampTests.Every_public_mutator_is_on_one_of_the_two_lists, unit:dotnet:Backlog.Modules.Tasks.UnitTests.TaskItemEffortTests.An_edit_after_a_stamp_from_a_faster_clock_still_moves_the_stamp_forward]
```

Every edit to a task moves its `updated_at` later than it was, even past a stamp from a machine whose clock runs ahead. Recording a usage event and storage replaying a stored task do not move it.
The later-edit-wins comparison between devices reads this stamp, so an edit that did not restamp would never reach the other device.

Enforced at: all mutations (each calls `Touch()`); `RecordUsage()`, `LoadSubItem()`, `LoadUsageEvent()` and `LoadStamps()` deliberately do not

### Invariant: Unlinking removes every reference to the named artifact

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.UnlinkTaskFromIssueTests.Unlinking_removes_only_the_named_link, unit:dotnet:Backlog.Modules.Tasks.UnitTests.UnlinkTaskFromIssueTests.Unlinking_ignores_case_and_keeps_the_entry_repositories, unit:dotnet:Backlog.Modules.Tasks.UnitTests.UnlinkTaskFromIssueTests.Unlinking_a_session_takes_it_off_under_every_repository]
```

Unlinking removes every [Projection Ref](domain.md#projection-ref) with the named repository, external id and target type, compared without regard to case, and no other. A session is matched under any repository.
GitHub is case-preserving but not case-sensitive, so two spellings name one artifact.

Enforced at: `RemoveProjectionRef()`

### Invariant: Unlinking leaves the task's repositories as they are

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Tasks.UnitTests.UnlinkTaskFromIssueTests.Unlinking_ignores_case_and_keeps_the_entry_repositories
```

Unlinking a Projection Ref leaves the task's `repo_ids` unchanged.
The task still targets the repository after one artifact in it is unlinked.

Enforced at: `RemoveProjectionRef()`, which changes only `ProjectionRefs`

### Invariant: Unlinking what the task does not hold changes nothing

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Tasks.UnitTests.UnlinkTaskFromIssueTests.Unlinking_what_the_entry_does_not_hold_leaves_its_stamp
```

Unlinking a Projection Ref the task does not hold is not a failure, and leaves the task and its `updated_at` as they were.

Enforced at: `RemoveProjectionRef()`, which restamps only when it removed something

### Invariant: A plan id keeps its sigil

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Tasks.UnitTests.ImportPlanTests.The_shared_plan_tag_is_the_sigilled_one_whatever_else_is_shared
```

A task's `import_plan_id` holds the plan's tag as written, sigil included, and matching tasks by plan compares it exactly, so `#slug` and `+slug` are two plans.
One spelling means one comparison inside this context; the sigil is lifted at the roadmap boundary and nowhere here.

Enforced at: `SetImportPlanId()`, which trims and never lifts the sigil; `ImportPlanCommandHandler` and `DependencyResolution` compare plan ids ordinally
