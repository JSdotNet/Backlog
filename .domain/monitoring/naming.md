# Naming: Monitoring & Dashboard

```meta
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their
> aliases. Each term links to where it is modeled (`related`); the surface
> names it is also known by are recorded in the `aliases` metadata field so a
> synonym can always be resolved back to one canonical concept.

## Term: Progress Signal

```meta
status: draft
aliases: [ProgressSignal, Signal]
related: [.domain/monitoring/domain.md#aggregate-progress-signal]
```

An immutable observation emitted by another context and recorded here.
Corrections are new signals, never mutations.

## Term: Signal Type

```meta
status: draft
aliases: [SignalType]
related: [.domain/monitoring/domain.md#signal-type]
```

Classification of a signal (status_change, github_sync, app_insights,
queue_depth, inbox_age, automation_run, copilot_session, machine_status,
team_aggregate).

## Term: Signal Payload

```meta
status: draft
aliases: [SignalPayload]
related: [.domain/monitoring/domain.md#signal-payload]
```

The owned value object carrying a signal's values; its keys depend on the
Signal Type.

## Term: Dashboard

```meta
status: draft
aliases: [Dashboard]
related: [.domain/monitoring/domain.md#domain-service-dashboard]
```

A derived view produced from the signal stream; dashboards and rollups are not
persisted aggregates.
