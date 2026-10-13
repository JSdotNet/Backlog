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
