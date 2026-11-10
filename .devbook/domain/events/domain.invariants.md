# Invariants

```meta
type: invariants
```

What each aggregate and domain service on [`domain.md`](domain.md) enforces, one chapter each,
with the unit tests that prove each rule built so far. The Attachment Request and the Harvest
Task Policy are not built yet, so their rules name no test.

## Knowledge Event

```meta
type: invariants
related: [.devbook/domain/events/domain.md#knowledge-event]
```

> The rules a knowledge event holds its fields, its lifecycle and its links to,
> including those of the value objects it owns.

### Invariant: A knowledge event has a title

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_knowledge_event_without_a_title_is_refused, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.Renaming_to_a_blank_title_is_refused_and_keeps_the_title, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_title_alone_makes_a_spotted_event_with_every_other_field_empty]
```

A knowledge event has a title that is not empty, kept trimmed (`knowledge_event.title_missing`).

Enforced at: constructor, `Rename()`

### Invariant: The end is not before the start

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.An_end_before_the_start_is_refused, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.An_end_on_the_start_day_is_accepted_whatever_the_times, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.An_end_without_a_start_is_refused, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_start_without_an_end_is_accepted_and_lasts_its_start_day]
```

An event's end date is on or after its start date, and an end needs a start (`knowledge_event.dates_out_of_order`).
The days are compared, not the times: an end on the start day is in order whatever its time.

Enforced at: `SetDates()`

### Invariant: Dates name their source

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_start_without_a_date_source_is_refused, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_date_source_without_dates_is_refused, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.No_dates_and_no_source_clears_the_dates]
```

An event with a start carries a date source, and one with no dates carries none (`knowledge_event.date_source_mismatch`).
Setting no dates and no source clears them.

Enforced at: `SetDates()`

### Invariant: The state moves only forward

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.Every_forward_move_is_accepted_skips_included, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_backward_or_same_state_move_is_refused, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.Skipped_is_reached_only_by_skipping_never_by_a_move]
```

An event moves only to a state later than its own on the path Spotted, Considering, Going, Attended, Harvested, and may skip states on the way (`knowledge_event.state_backward`).
No backward move exists in the first version.
Skipped is not on that path, so a move to it is refused the same way. Skipping with a reason is the only way in.

Enforced at: `MoveTo()`

### Invariant: Only a Spotted or Considering event is skipped

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_spotted_or_considering_event_is_skipped, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_going_or_attended_event_cannot_be_skipped]
```

An event moves to Skipped only from Spotted or Considering (`knowledge_event.cannot_skip`).

Enforced at: `Skip()`

### Invariant: A skipped event has a reason

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.Skipping_without_a_reason_is_refused_and_leaves_the_state, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_skipped_event_keeps_its_reason]
```

A Skipped event carries a skip reason that is not empty, kept trimmed (`knowledge_event.skip_reason_missing`). A refused skip leaves the event as it was.

Enforced at: `Skip()`

### Invariant: Harvested and Skipped are final

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_harvested_or_skipped_event_moves_to_no_other_state, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_harvested_or_skipped_event_is_not_skipped_again]
```

A Harvested or Skipped event moves to no other state (`knowledge_event.state_final`).
This refusal comes before every other: a final event is refused as final, never as a backward move or as one that cannot be skipped.

Enforced at: `MoveTo()`, `Skip()`

### Invariant: A verdict waits for the event

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_verdict_before_the_event_is_attended_is_refused, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.An_attended_or_harvested_event_takes_a_verdict, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_blank_verdict_clears_it, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.Clearing_a_verdict_waits_for_the_event_too]
```

An event takes a verdict only once it is Attended or Harvested (`knowledge_event.verdict_too_early`).
A blank verdict clears it, and clearing waits for the event too. A verdict is kept trimmed.

Enforced at: `SetVerdict()`

### Invariant: An event holds at most one harvest task

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.The_first_harvest_task_is_linked, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_second_harvest_task_is_refused, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.Linking_the_same_harvest_task_again_changes_nothing, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_harvest_link_through_link_task_keeps_the_same_rule, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_task_already_linked_is_not_made_a_second_harvest_task, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.Prep_and_linked_tasks_sit_beside_the_harvest_task]
```

At most one of an event's Event Task Links has the purpose Harvest (`knowledge_event.harvest_task_exists`).

Enforced at: `LinkHarvestTask()`, `LinkTask()`

### Invariant: A note or task is linked once

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.Linking_a_note_twice_leaves_one_link, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.Linking_a_task_twice_leaves_one_link_whatever_its_purpose, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventInvariantTests.A_task_already_linked_for_another_purpose_becomes_the_harvest_task, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventValueTests.Two_note_links_naming_the_same_note_are_equal_whatever_their_titles, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventValueTests.A_brain_note_and_a_talk_note_with_the_same_reference_are_different_notes, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventValueTests.Two_task_links_naming_the_same_task_are_equal_whatever_their_purposes]
```

An event holds each Note Link and each Event Task Link once, so linking one it already holds changes nothing: a note by its kind and reference whatever its title, a task by its id whatever its purpose.
The one change is a task held for Prep or Linked: linked again as the harvest task while the event holds none, it becomes the harvest task.
This is what makes applying an attachment request twice harmless.

Enforced at: `LinkNote()`, `LinkTask()`

### Invariant: A time carries its UTC offset

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventValueTests.A_time_without_its_utc_offset_is_refused, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventValueTests.A_date_alone_is_an_event_date, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventValueTests.A_date_with_a_time_and_its_offset_is_an_event_date, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventValueTests.An_offset_without_a_time_is_kept]
```

An event date with a time of day also carries its UTC offset (`event_date.offset_missing`).
An offset with no time is kept.

Enforced at: constructor (Event Date)

### Invariant: A place is a venue or online

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventValueTests.A_place_that_is_both_a_venue_and_online_is_refused, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventValueTests.A_place_that_is_neither_a_venue_nor_online_is_refused, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventValueTests.A_blank_venue_is_refused]
```

A place is either a venue that is not empty or online, never both and never neither (`place.invalid`).

Enforced at: constructor (Place)

### Invariant: A key date has a label

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventValueTests.A_key_date_without_a_label_is_refused, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventValueTests.A_key_date_keeps_its_date_and_its_label_trimmed]
```

A key date carries a date and a label that is not empty (`key_date.label_missing`).

Enforced at: constructor (Key Date)

### Invariant: A cost is not negative and names its currency

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventValueTests.A_negative_cost_is_refused, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventValueTests.A_cost_without_a_currency_is_refused, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventValueTests.A_free_event_costs_zero]
```

A cost's amount is zero or more, and it names its currency (`cost.invalid`).

Enforced at: constructor (Cost)

## Event Series

```meta
type: invariants
related: [.devbook/domain/events/domain.md#event-series]
```

> The rules an event series holds itself to.

### Invariant: A series has a name

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.EventSeriesInvariantTests.A_series_without_a_name_is_refused, unit:dotnet:Backlog.Modules.Events.UnitTests.EventSeriesInvariantTests.Renaming_to_a_blank_name_is_refused_and_keeps_the_name, unit:dotnet:Backlog.Modules.Events.UnitTests.EventSeriesInvariantTests.Renaming_trims_the_new_name]
```

An event series has a name that is not empty, kept trimmed (`event_series.name_missing`).

Enforced at: constructor, `Rename()`

## Attachment Request

```meta
type: invariants
related: [.devbook/domain/events/domain.md#attachment-request]
```

> The rules an attachment request holds itself to.

### Invariant: A request names one event and one Talk note

```meta
type: invariant
```

An attachment request names exactly one knowledge event and one Inbox note (`attachment_request.incomplete`).

Enforced at: constructor

### Invariant: A request never changes

```meta
type: invariant
```

An attachment request is written once and never changed. A desktop applies it whole, then deletes it.

Enforced at: all mutations

## Harvest Task Policy

```meta
type: invariants
related: [.devbook/domain/events/domain.md#harvest-task-policy]
```

> The rules the Harvest Task Policy will not let a caller break. What it promises
> the person is in [`requirements.md`](requirements.md#harvest-task).

### Invariant: Only a Going event past its end day is due

```meta
type: invariant
```

The policy files a harvest task only for an event that is Going and whose end day is before the desktop's local date.
An event with no dates is never due.

Enforced at: `FileDue()`

### Invariant: Harvested completes only the event's own harvest task

```meta
type: invariant
```

Marking an event Harvested completes the task its Harvest Event Task Link names, and no other task.

Enforced at: `OnHarvested()`
