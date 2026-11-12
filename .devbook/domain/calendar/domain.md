# Domain

```meta
type: domain
related: [.devbook/arc42/adr/0055-calendar-is-a-surface-of-its-own-that-reads-layers.md]
```

The Calendar owns no aggregate, domain service or domain event: everything it shows
belongs to another context. It owns the words below.

## Ubiquitous Language

```meta
type: ubiquitous-language
```

### Calendar Layer

```meta
type: term
aliases: [layer, Tasks layer, Plans layer, Events layer]
related: [.devbook/domain/calendar/features.md#calendar-view, .devbook/domain/tasks/features.md#tasks-on-the-calendar, .devbook/domain/events/features.md#events-on-the-calendar, .devbook/domain/calendar/context.md]
```

What one source draws on the Calendar's days. There are three: the **Tasks layer**,
always drawn, the **Plans layer** and the **Events layer**. The Plans layer is read
through a port the Calendar owns, and the Events layer through Tasks' port until it
[moves](context.md). The Tasks layer is drawn by a part Tasks offers, which the Calendar
hosts. A layer writes only back through its source.

### Layer Choice

```meta
type: term
aliases: [Show plans, Show events]
related: [.devbook/domain/calendar/context.md#show-plans, .devbook/domain/calendar/context.md#show-events]
```

The choice that turns one layer on or off. **Show plans** switches the Plans layer and
**Show events** the Events layer. The Tasks layer has none.

### Range

```meta
type: term
aliases: [Month | Week]
related: [.devbook/domain/calendar/context.md#calendar-range]
```

How many days the Calendar shows: a **Month**, in whole weeks from Monday, or a
**Week** of seven days. Not to be confused with a plan's window on the roadmap.
