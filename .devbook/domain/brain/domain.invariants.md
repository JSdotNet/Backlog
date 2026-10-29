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
related: [.devbook/domain/brain/domain.md#knowledge-note]
```

> The rules a Knowledge Note holds its area and its links to, and the areas its
> Brain declares. The planning fields it carries as any devbook chapter are
> [Devbook's](../devbook/domain.invariants.md#shared-value-objects).

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

### Invariant: An area is one plain folder, declared once

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_folder_that_is_not_one_plain_segment_is_skipped, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_folder_declared_twice_counts_once]
```

Every area `CONVENTIONS.md` declares is a distinct folder of one plain segment: a path, `..`, a dot-folder, or a second spelling of a folder already declared is skipped.
An area is one top folder of the Brain, so a declaration that leaves the Brain or names a folder twice cannot be one.

Enforced at: `BrainConventions.Declared()`, through `IsAreaFolder()` and a case-insensitive set of the folders already kept

### Invariant: Declared areas keep the order written

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_table_declares_the_areas_in_its_order, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_table_s_order_column_wins_over_the_row_order]
```

The declared areas keep the order `CONVENTIONS.md` writes them in, unless a table's `Order` column numbers them, in which case the numbered rows come first by number and the rest follow in the order written.

Enforced at: `BrainConventions.Declared()`

### Invariant: Discovered areas follow the declared ones

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.Discovered_areas_follow_the_declared_ones_by_folder_name, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_top_folder_is_an_area_once_a_note_sits_anywhere_below_it, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.Only_a_plain_undeclared_unignored_folder_is_probed, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.With_nothing_declared_the_para_set_leads_the_discovered_areas, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_declared_area_matches_its_folder_whatever_the_case]
```

Every top folder of the Brain that is one plain segment, holds at least one `.md` note below it outside a dot-folder, is not ignored and is not declared is an area, listed after the declared areas by folder name, ordinal and case-insensitive.
A note filed into a new top folder is never hidden for want of a line in `CONVENTIONS.md` (local ADR 0026).

Enforced at: `BrainConventions`, in `Backlog.Modules.Devbook.Abstractions`

### Invariant: An ignored folder is never an area

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.An_ignored_folder_is_never_an_area_even_when_declared, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.Ignoring_every_declared_area_does_not_bring_para_back, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.An_ignore_list_names_its_folders_in_backticks_or_bare, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.An_ignore_table_reads_its_folder_column]
```

A folder `CONVENTIONS.md` lists under `## Ignore`, matched case-insensitively, is never an area, even when `## Areas` declares it.
Ignore wins over a declaration, so one line keeps a folder of templates or attachments off the tabs.

Enforced at: `BrainConventions`, in `Backlog.Modules.Devbook.Abstractions`

