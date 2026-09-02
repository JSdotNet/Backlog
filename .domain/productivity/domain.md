# Productivity

```meta
type: domain
status: draft
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context; each chapter's
> `type` records which of those it is. An Aggregate's owned Entities, Value
> Objects, and Enums are chapters directly beneath it, typed `entity`,
> `value-object`, and `enum`. Value Objects/Enums shared across multiple
> aggregates get their own chapter at the end instead of being duplicated.

Productivity tracks how the person uses AI-assisted work tools and turns those
activity signals into personal productivity insight. It measures contribution,
time saved, flow, and outcomes; it does not own task work, Copilot sessions,
repository state, or completion decisions.

## Productivity Ledger

```meta
type: aggregate
status: draft
related: [.domain/tasks/domain.md#aiworklogged, .domain/monitoring/domain.md#progress-signal]
```

The personal record of productivity-relevant activity. The ledger is append-only:
each recorded activity is preserved as a `Productivity Entry`, corrections arrive
as new entries, and metrics are derived from the ledger rather than edited in
place. Invariants: each entry has a source, activity kind, time range or
timestamp, and optional subject reference; AI-assisted entries identify the AI
tool or session when available; summaries never mutate source entries.

### Productivity Entry

```meta
type: entity
status: draft
```

An individual recorded activity, identified within the ledger. It captures when
the activity happened, which work subject it relates to, which AI tool was used
if any, and what outcome was produced.

### Work Subject Ref

```meta
type: value-object
status: draft
```

An opaque reference to the work being measured, such as a task, pull
request, commit, issue, note, or Copilot session. Equality is by `subject_type`
and `subject_id`.

### Productivity Metric

```meta
type: value-object
status: draft
```

A derived measurement such as AI-assisted tasks completed, estimated time saved,
prompt reuse, review cycles avoided, or focus streak. Metrics are recomputed from
entries and never become the source of truth.

### Activity Kind

```meta
type: enum
status: draft
```

The type of productivity activity being measured: `planning`, `coding`,
`review`, `research`, `summarization`, `documentation`, `automation`, or
`other`.

### AI Tool

```meta
type: enum
status: draft
```

The AI-assisted channel used for the activity, such as Copilot CLI, GitHub
Copilot App, IDE chat, or another assistant. The concrete values remain
configurable because tools change faster than the domain concept.

## Productivity Analysis

```meta
type: domain-service
status: draft
related: [.domain/productivity/domain.md#productivity-ledger]
```

Computes productivity summaries from ledger entries: AI-assisted work volume,
time-saved estimates, completion trends, prompt reuse, and work distribution by
tool, repository, environment, or activity kind. It is a service because the
behavior is query/composition-oriented and derives insight across many entries
rather than enforcing a single entry invariant. Invocation semantics:
query/composition-oriented when the person opens a productivity view or requests
a report.

## ProductivityRecorded

```meta
type: domain-event
status: draft
related: [.domain/productivity/domain.md#productivity-ledger, .domain/monitoring/domain.md#progress-signal]
```

Published when a productivity-relevant activity is appended to the Productivity
Ledger.

### Payload

- `productivity_entry_id` - ledger entry identifier.
- `subject_ref` - optional work subject reference.
- `activity_kind` - activity category.
- `ai_tool` - optional AI tool name when the activity is AI-assisted.
- `started_at` - optional start time.
- `completed_at` - activity completion or record time.
- `outcome_ref` - optional resulting artifact reference.

### Consumers

- Monitoring & Dashboard, which can display productivity trends alongside other
  work signals.

### Published language rules

- The event is an activity record, not a performance judgment.
- Consumers use the published fields only and do not infer hidden productivity
  scores from the source tool.

## Shared Enums

```meta
type: shared-enums
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Productivity currently has a single aggregate, so `Activity Kind` and `AI Tool`
are documented under it. This chapter is reserved for future cross-aggregate
enums.