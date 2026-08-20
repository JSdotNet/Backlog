# Monitoring & Dashboard

```meta
type: naming
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their
> aliases. Each term links to where it is modeled (`related`); the surface
> names it is also known by are recorded in the `aliases` metadata field so a
> synonym can always be resolved back to one canonical concept.

## Progress Signal

```meta
type: term
status: draft
aliases: [ProgressSignal, Signal]
related: [.domain/monitoring/domain.md#progress-signal]
```

An immutable observation emitted by another context and recorded here.
Corrections are new signals, never mutations.

## Signal Type

```meta
type: term
status: draft
aliases: [SignalType]
related: [.domain/monitoring/domain.md#signal-type]
```

Classification of a signal (status_change, github_sync, app_insights,
queue_depth, inbox_age, automation_run, copilot_session, machine_status,
team_aggregate).

## Signal Payload

```meta
type: term
status: draft
aliases: [SignalPayload]
related: [.domain/monitoring/domain.md#signal-payload]
```

The owned value object carrying a signal's values; its keys depend on the
Signal Type.

## Dashboard

```meta
type: term
status: draft
aliases: [Dashboard]
related: [.domain/monitoring/domain.md#dashboard]
```

A derived view produced from the signal stream; dashboards and rollups are not
persisted aggregates.
