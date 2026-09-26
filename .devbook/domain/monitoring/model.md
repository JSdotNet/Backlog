# Monitoring & Dashboard

```meta
type: model
status: draft
```

> Structural view of the domain model for this bounded context: aggregates,
> entities, value objects, and their relationships. Keep this in sync with
> `domain.md` (which describes responsibilities/invariants in prose) — this
> file focuses on structure and relationships.

## Model diagram

```mermaid
classDiagram
    class ProgressSignal {
        <<aggregate root>>
        +Id id
        +SignalType type
        +String source
        +String subject_ref
        +Timestamp detected_at
    }
    class SignalPayload {
        <<value object>>
        +Map values
    }
    class SignalType {
        <<enumeration>>
        status_change
        github_sync
        app_insights
        queue_depth
        inbox_age
        automation_run
        copilot_session
        machine_status
        team_aggregate
    }

    ProgressSignal --> SignalType : classified as
    ProgressSignal "1" *-- "1" SignalPayload : carries
```

## Relationship notes

- `ProgressSignal` is an immutable aggregate root; `SignalPayload` is its owned
  value object whose keys depend on `SignalType`. Corrections are new signals,
  never mutations.
- Signals reference their subject by id/URL (`subject_ref`) only — Monitoring
  never holds foreign aggregates, so it stays a read/observer context.
- Dashboards and rollups are produced by the Signal Aggregation and Dashboard
  services from the signal stream; they are not persisted aggregates here.
