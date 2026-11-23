# Domain

```meta
type: domain
related: [.devbook/arc42/adr/0055-calendar-is-a-surface-of-its-own-that-reads-layers.md, .devbook/arc42/adr/0056-the-calendar-owns-its-grid-and-tasks-is-one-layer.md]
```

The Calendar owns no aggregate, domain service or domain event: everything it shows
belongs to another context. With no aggregate to hold a rule, it has no invariants page.
It owns the words below.

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

What one source draws on the Calendar's days. There are three: the **Tasks layer**, the
**Plans layer** and the **Events layer**. Each is read through a
[port the Calendar owns](context.md#dependencies), and the Calendar draws them all on
its own grid. A layer writes only back through its source.

### Layer Choice

```meta
type: term
aliases: [Show tasks, Show plans, Show events]
related: [.devbook/domain/calendar/context.md#show-tasks, .devbook/domain/calendar/context.md#show-plans, .devbook/domain/calendar/context.md#show-events]
```

The choice that turns one layer on or off. **Show tasks** switches the Tasks layer,
**Show plans** the Plans layer and **Show events** the Events layer. A choice is
offered only while its layer's source is there to answer.

### Range

```meta
type: term
aliases: [Month | Week]
related: [.devbook/domain/calendar/context.md#calendar-range]
```

How many days the Calendar shows: a **Month**, in whole weeks from Monday, or a
**Week** of seven days. Not to be confused with a plan's window on the roadmap.
