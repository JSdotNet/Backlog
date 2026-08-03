# Domain Model: Monitoring & Dashboard

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

## Signal flow

```mermaid
sequenceDiagram
    participant Backlog as Backlog
    participant Inbox as Inbox
    participant GitHub as GitHub
    participant AppInsights as Application Insights
    participant DevPC as Dev PC Management
    participant Monitoring as Monitoring
    participant Dashboard as Dashboard

    Backlog->>Monitoring: StatusChanged (item_id, new_status, changed_at)
    Inbox->>Monitoring: QueueDepthChanged (depth, age_max, processing_rate)
    GitHub->>Monitoring: IssueUpdated (backlog_item_id, url, status)
    AppInsights->>Monitoring: MetricReceived (project, metric, value, severity)
    DevPC->>Monitoring: MachineStatusChanged (machine_id, status, last_heartbeat)
    DevPC->>Monitoring: ComplianceUpdated (machine_id, compliance_score)

    Note over Monitoring: Signals aggregated and correlated

    Monitoring->>Dashboard: ProgressSignals + QueueHealth + InfraDashboard
    Note over Dashboard: Follow-up actions identified
    Dashboard->>Inbox: FollowUpCaptured (title, source: monitoring)
```

## Relationship notes

- `ProgressSignal` is an immutable aggregate root; `SignalPayload` is its owned
  value object whose keys depend on `SignalType`. Corrections are new signals,
  never mutations.
- Signals reference their subject by id/URL (`subject_ref`) only — Monitoring
  never holds foreign aggregates, so it stays a read/observer context.
- Dashboards and rollups are produced by the Signal Aggregation and Dashboard
  services from the signal stream; they are not persisted aggregates here.
- The only write Monitoring makes into another context is `FollowUpCaptured` to
  the Inbox, closing the observe → act loop.
