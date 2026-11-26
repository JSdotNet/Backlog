# Insights

```meta
type: domain
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context; each chapter's
> `type` records which of those it is. An Aggregate's owned Entities, Value
> Objects, and Enums are chapters directly beneath it, typed `entity`,
> `value-object`, and `enum`. Value Objects/Enums shared across multiple
> aggregates get their own chapter at the end instead of being duplicated.

Insights tracks how the person and their agents work and turns that activity
into personal insight. It measures contribution, time saved, flow, and outcomes;
it does not own task work, plans, agent sessions (Sessions), repository state, or
completion decisions.

## Productivity Ledger

```meta
type: aggregate
related: [.devbook/domain/tasks/domain.md#aiworklogged, .devbook/domain/insights/domain.invariants.md#productivity-ledger]
aliases: [ProductivityLedger]
```

The personal record of productivity-relevant activity. Each recorded activity is kept as a `Productivity Entry`, and metrics are derived from the ledger, never edited in place. Its rules are in [its invariants](domain.invariants.md#productivity-ledger).

### Productivity Entry

```meta
type: entity
aliases: [ProductivityEntry, productivity_entry_id]
```

One recorded activity, identified within the ledger: when it happened, which work subject it relates to, which AI tool was used if any, and what outcome it produced.

### Work Subject Ref

```meta
type: value-object
aliases: [WorkSubjectRef, subject_ref]
```

An opaque reference to the work being measured; an entry may carry one. Equality is by `subject_type` and `subject_id`. It can refer to:

- a task;
- a pull request;
- a commit;
- an issue;
- a note;
- a Copilot session.

### Productivity Metric

```meta
type: value-object
```

A derived measurement, recomputed from entries and never the source of truth. Examples are:

- AI-assisted tasks completed;
- estimated time saved;
- prompt reuse;
- review cycles avoided;
- focus streak.

### Activity Kind

```meta
type: enum
aliases: [ActivityKind]
```

The type of productivity activity being measured, one of:

- `planning`;
- `coding`;
- `review`;
- `research`;
- `summarization`;
- `documentation`;
- `automation`;
- `other`.

### AI Tool

```meta
type: enum
```

The AI-assisted channel used for the activity. The values stay configurable because tools change faster than the domain concept. Examples are Copilot CLI, GitHub Copilot App, IDE chat and another assistant.

## Productivity Analysis

```meta
type: domain-service
related: [.devbook/domain/insights/domain.md#productivity-ledger]
```

Computes productivity summaries from ledger entries:

- AI-assisted work volume;
- time-saved estimates;
- completion trends;
- prompt reuse;
- work distribution by tool, repository, environment or activity kind.

It is a service because it derives insight across many entries rather than enforcing a single entry invariant. Invocation semantics: query/composition-oriented, when the person opens a productivity view or requests a report.

## Dashboard

```meta
type: domain-service
related: [.devbook/domain/insights/features.md#personal-productivity-dashboard, .devbook/domain/insights/features.md#what-needs-you, .devbook/arc42/08-crosscutting-concepts.md#observability]
```

Composes the dashboard from what other contexts already hold, one tab per question:

- shipping, from GitHub pull request and issue activity;
- planned work, from Tasks and Roadmap Planning;
- agent sessions and hours worked, from Sessions;
- devbook drift, from Devbook's sync verdicts;
- spend, from each [spend provider](#spend-provider).

It reads every source on its own, so one that cannot answer leaves the others standing, and it raises [What needs you](features.md#what-needs-you) from the tabs' own figures. It writes into no other context and keeps no state of its own beyond the settings on [context.md](context.md). It is a service because it reads across contexts and owns no aggregate.

Invocation semantics: query/composition service, invoked when the dashboard pane opens or a part is refreshed.

## ProductivityRecorded

```meta
type: domain-event
related: [.devbook/domain/insights/domain.md#productivity-ledger]
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

- None outside Insights. The dashboard reads the ledger in the same context.

### Published language rules

- The event is an activity record, not a performance judgment.
- Consumers use the published fields only and do not infer hidden productivity
  scores from the source tool.

## Shared Enums

```meta
type: shared-enums
```

> Enums used by more than one aggregate in this bounded context.

Insights currently has a single aggregate, so `Activity Kind` and `AI Tool`
are documented under it. This chapter is reserved for future cross-aggregate
enums.

## Ubiquitous Language

```meta
type: ubiquitous-language
```

### AI Productivity Tracking

```meta
type: term
aliases: [AI productivity, AI-assisted productivity]
related: [.devbook/domain/insights/features.md#ai-productivity-tracking]
```

The product capability for understanding how AI-assisted work contributes to the
person's outcomes.

### Office Hours

```meta
type: term
aliases: [office hours, inside office hours, outside office hours]
related: [.devbook/domain/insights/context.md#working-week, .devbook/domain/roadmap/domain.md#working-week, .devbook/domain/roadmap/domain.md#day-override, .devbook/domain/roadmap/domain.md#actual-hours, .devbook/domain/insights/features.md#hours-worked, .devbook/domain/insights/requirements.md#hours-worked]
```

The span of time on one date that the person meant to work: that date's start and end in the [working week](context.md#working-week), with the [day overrides](../roadmap/domain.md#day-override) applied. A blocked date has no office hours. An unblocked date has the start and end its weekday stores. A day whose end is not after its start has none either.

The dashboard's [Hours worked](features.md#hours-worked) part splits the person's [actual hours](../roadmap/domain.md#actual-hours) by it into time **inside office hours** and time **outside office hours**. The split is to the minute, not by the hour grids' rule of outlining a whole hour that office hours only touch. Each chart figure is that time in hours, rounded half to even to the hundredth from the duration itself: 26 minutes 42 seconds is 0.445 hours and reads 0.44.

The term exists because the split needs one date's span, overrides applied. *Working hours* is an alias of Roadmap Planning's [Working Week](../roadmap/domain.md#working-week), the weekly pattern and its overrides together, not the hours of one date.

### Spend Provider

```meta
type: term
aliases: [spend provider, SpendProvider]
related: [.devbook/domain/insights/context.md#monthly-spend-budget, .devbook/domain/insights/features.md#spend-against-a-monthly-budget]
```

An assistant whose spend the dashboard reads, kept apart from the others all the
way to the screen. There are three: **Anthropic**, **Copilot** and **Azure AI
Foundry**, and the dashboard names them that way wherever it shows them. The
Anthropic provider is the Claude spend, named for the company because the report
is Anthropic's bill. The providers report on different terms: an estimate, a
metered balance, or a bill in the subscription's currency. Their figures are
therefore never added into one total, except where a budget line has to be a sum.
