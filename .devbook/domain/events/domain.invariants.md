# Invariants

```meta
type: invariants
```

What each aggregate and domain service on [`domain.md`](domain.md) enforces, one
chapter each. Nothing is built yet, so no rule names a test.

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
```

A knowledge event has a title that is not empty (`knowledge_event.title_missing`).

Enforced at: constructor

### Invariant: The end is not before the start

```meta
type: invariant
```

An event's end date is on or after its start date, and an end needs a start (`knowledge_event.dates_out_of_order`).

Enforced at: `SetDates()`

### Invariant: Dates name their source

```meta
type: invariant
```

An event with a start carries a date source, and one with no dates carries none (`knowledge_event.date_source_mismatch`).

Enforced at: `SetDates()`

### Invariant: The state moves only forward

```meta
type: invariant
```

An event moves only to a state later than its own on the path Spotted, Considering, Going, Attended, Harvested, and may skip states on the way (`knowledge_event.state_backward`).
No backward move exists in the first version.

Enforced at: `MoveTo()`

### Invariant: Only a Spotted or Considering event is skipped

```meta
type: invariant
```

An event moves to Skipped only from Spotted or Considering (`knowledge_event.cannot_skip`).

Enforced at: `Skip()`

### Invariant: A skipped event has a reason

```meta
type: invariant
```

A Skipped event carries a skip reason that is not empty (`knowledge_event.skip_reason_missing`).

Enforced at: `Skip()`

### Invariant: Harvested and Skipped are final

```meta
type: invariant
```

A Harvested or Skipped event moves to no other state (`knowledge_event.state_final`).

Enforced at: `MoveTo()`, `Skip()`

### Invariant: A verdict waits for the event

```meta
type: invariant
```

An event takes a verdict only once it is Attended or Harvested (`knowledge_event.verdict_too_early`).

Enforced at: `SetVerdict()`

### Invariant: An event holds at most one harvest task

```meta
type: invariant
```

At most one of an event's Event Task Links has the purpose Harvest (`knowledge_event.harvest_task_exists`).

Enforced at: `LinkHarvestTask()`

### Invariant: A note or task is linked once

```meta
type: invariant
```

An event holds each Note Link and each Event Task Link once, so linking one it already holds changes nothing.
This is what makes applying an attachment request twice harmless.

Enforced at: `LinkNote()`, `LinkTask()`

### Invariant: A time carries its UTC offset

```meta
type: invariant
```

An event date with a time of day also carries its UTC offset (`event_date.offset_missing`).

Enforced at: constructor (Event Date)

### Invariant: A place is a venue or online

```meta
type: invariant
```

A place is either a venue that is not empty or online, never both and never neither (`place.invalid`).

Enforced at: constructor (Place)

### Invariant: A key date has a label

```meta
type: invariant
```

A key date carries a date and a label that is not empty (`key_date.label_missing`).

Enforced at: constructor (Key Date)

### Invariant: A cost is not negative and names its currency

```meta
type: invariant
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
```

An event series has a name that is not empty (`event_series.name_missing`).

Enforced at: constructor

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
