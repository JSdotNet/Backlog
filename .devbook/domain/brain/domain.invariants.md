# Invariants

```meta
type: invariants
```

What each aggregate and domain service on [`domain.md`](domain.md) enforces, one chapter each: each rule, where it is enforced, and the unit test that proves it.

## Knowledge Note

```meta
type: invariants
related: [.devbook/domain/brain/domain.md#knowledge-note]
```

> The rules a Knowledge Note holds its area and its links to, and the areas its
> Brain declares. The planning fields it carries as any devbook chapter are
> [Devbook's](../devbook/domain.invariants.md#shared-value-objects).

### Invariant: A note sits in one area

```meta
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainDatabaseTests.A_note_s_folder_is_its_area_and_no_devbook_convention_applies, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainDatabaseTests.Para_repos_and_tasks_are_node_attributes]
```

A note sits in exactly one area, its [PARA Category](domain.md#para-category): the top folder it sits in, or the area its `para` field names instead.
A chapter inside a note is a node of its own, and may name an area of its own.

Enforced at: `DevbookGraphBuilder.ApplyBrainFields()` (one `para` per node)

### Invariant: Links change through the note

```meta
type: invariant
```

A note's links to tasks and to other notes change only through the note.
Even [Cross-Linking](#cross-linking) changes them by writing into the note, never by keeping a link anywhere else.

Enforced at: open

### Invariant: The change time moves forward

```meta
type: invariant
```

A note's `updated_at` moves forward on every change.

Enforced at: open

### Invariant: An area is one plain folder, declared once

```meta
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_folder_that_is_not_one_plain_segment_is_skipped, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_folder_declared_twice_counts_once]
```

Every area `CONVENTIONS.md` declares is a distinct folder of one plain segment: a path, `..`, a dot-folder, or a second spelling of a folder already declared is skipped.
An area is one top folder of the Brain, so a declaration that leaves the Brain or names a folder twice cannot be one.

Enforced at: `BrainConventions.Declared()`, through `IsAreaFolder()` and a case-insensitive set of the folders already kept

### Invariant: Declared areas keep the order written

```meta
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_table_declares_the_areas_in_its_order, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_table_s_order_column_wins_over_the_row_order]
```

The declared areas keep the order `CONVENTIONS.md` writes them in, unless a table's `Order` column numbers them, in which case the numbered rows come first by number and the rest follow in the order written.

Enforced at: `BrainConventions.Declared()`

### Invariant: Discovered areas follow the declared ones

```meta
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.Discovered_areas_follow_the_declared_ones_by_folder_name, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_top_folder_is_an_area_once_a_note_sits_anywhere_below_it, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.Only_a_plain_undeclared_unignored_folder_is_probed, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.With_nothing_declared_the_para_set_leads_the_discovered_areas, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_declared_area_matches_its_folder_whatever_the_case]
```

Every undeclared, unignored top folder of one plain segment with a `.md` note below it outside a dot-folder is an area, listed after the declared areas by folder name, ordinal and case-insensitive.
A note filed into a new top folder is never hidden for want of a line in `CONVENTIONS.md` (local ADR 0026).

Enforced at: `BrainConventions`, in `Backlog.Modules.Devbook.Abstractions`

### Invariant: An ignored folder is never an area

```meta
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.An_ignored_folder_is_never_an_area_even_when_declared, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.Ignoring_every_declared_area_does_not_bring_para_back, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.An_ignore_list_names_its_folders_in_backticks_or_bare, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.An_ignore_table_reads_its_folder_column]
```

A folder `CONVENTIONS.md` lists under `## Ignore`, matched case-insensitively, is never an area, even when `## Areas` declares it.
Ignore wins over a declaration, so one line keeps a folder of templates or attachments off the tabs.

Enforced at: `BrainConventions`, in `Backlog.Modules.Devbook.Abstractions`

## Cross-Linking

```meta
type: invariants
related: [.devbook/domain/brain/domain.md#cross-linking, .devbook/arc42/adr/0052-the-app-may-write-links-into-a-brain-note-and-move-a-finished-project-note-to-the-archive.md]
```

> The rules [Cross-Linking](domain.md#cross-linking) keeps when it writes a link
> into an existing note. They narrow [Links change through the note](#invariant-links-change-through-the-note):
> the link still changes through the note, and only by an append.

### Invariant: Only the two lists change

```meta
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.A_task_id_appended_to_a_flow_list_changes_only_that_line, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.A_roadmap_tag_appended_to_an_empty_flow_list_stays_a_flow_list, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.A_block_list_stays_a_block_list_with_its_own_indentation, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.A_missing_field_lands_as_the_last_line_of_the_top_block, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.Every_byte_outside_the_list_is_kept, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.A_block_list_append_keeps_every_other_byte_and_the_byte_order_mark, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.A_bare_field_with_no_items_becomes_a_flow_list, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.A_trailing_comma_in_a_flow_list_gets_no_second_comma, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.A_field_only_a_lower_block_carries_is_added_to_the_top_block, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.A_file_with_mixed_line_endings_keeps_each_one, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.Links_written_at_once_into_one_note_all_land, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainNoteLinksParityTests.An_appended_task_id_is_in_the_file_s_block_as_the_parse_reads_it, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainNoteLinksParityTests.A_missing_roadmap_field_is_added_where_the_parse_reads_it]
```

A link write changes only the `tasks` or `roadmap` list in the note's top `meta` block, and leaves every other byte of the file as it was.
The body, the other fields, their key order, comments, blank lines and the list's flow or block style are the person's.

Enforced at: `BrainNoteLinks.Append()`, which changes only the list's line or adds one line, and `BrainNoteLinkWriter`, which writes the edit back byte for byte as strict UTF-8 with its byte order mark kept, one write at a time

### Invariant: A present value is not added again

```meta
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.A_present_task_id_leaves_the_file_unchanged, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.A_present_roadmap_tag_in_a_block_list_leaves_the_file_unchanged, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.A_repeated_link_writes_once]
```

A task id or Roadmap Tag the list already holds is not appended again, so a repeated link leaves the file unchanged.

Enforced at: `BrainNoteLinks.Append()`, which answers `AlreadyPresent` for a value the list holds, and `BrainNoteLinkWriter`, which then writes nothing

### Invariant: A written link is read back in either list style

```meta
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainBlockListLinkTests, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.DevbookBuilderParityTests.The_app_matches_the_node_writer_on_a_brain]
```

A `tasks`, `roadmap` or `repos` list the Brain's database indexes holds the same values whether the note writes it as a flow list (`[a, b]`) or as a block list (`- a` lines under the field), so a link the app appends in the note's own style is a link the app reads.

Enforced at: `BrainCorpusProfile.BlockListFields`, which `DevbookGraphBuilder` hands `DevbookMarkdown.Parse()` to read block items for, and `readBlockLists` in `tools/devbook/brain.mjs`, the reference `DevbookBuilderParityTests` holds the builder to

### Invariant: An unparsable block is not written

```meta
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.A_missing_or_unparsable_top_block_is_refused_with_its_reason, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.A_file_that_is_not_utf8_is_refused]
```

A note whose top `meta` block is missing or does not parse is not written.

Enforced at: `BrainNoteLinks.Append()`, which refuses with the reason, and `BrainNoteLinkWriter`, which writes nothing on a refusal

