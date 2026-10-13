# Invariants

```meta
status: draft
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate: each rule, where it is enforced, and the unit test that proves it.

## Knowledge Note

```meta
status: draft
type: invariants
related: [.devbook/domain/devbook/domain.md#knowledge-note]
```

> The rules a Knowledge Note holds its area and its links to.

### Invariant: A note sits in one area

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainDatabaseTests.A_note_s_folder_is_its_area_and_no_devbook_convention_applies, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainDatabaseTests.Para_repos_and_tasks_are_node_attributes]
```

A note sits in exactly one area, its [PARA Category](domain.md#para-category): the top folder it sits in, or the area its `para` field names instead.
A chapter inside a note is a node of its own, and may name an area of its own.

Enforced at: `DevbookGraphBuilder.ApplyBrainFields()` (one `para` per node)

### Invariant: Links change through the note

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

A note's links to tasks and to other notes change only through the note.

Enforced at: open

### Invariant: The change time moves forward

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

A note's `updated_at` moves forward on every change.

Enforced at: open
