# Domain Model: Dev PC Management

```meta
status: draft
```

> Structural view of the domain model for this bounded context: aggregates,
> entities, value objects, and their relationships. Keep this in sync with
> `domain.md` (which describes responsibilities/invariants in prose) — this
> file focuses on structure and relationships.

## Model diagram

```mermaid
classDiagram
    class MachineRegistry {
        <<aggregate root>>
        +Map~MachineId,Machine~ machines
        +TeamToolsBaseline baselines
        +FleetAnalytics analytics
    }
    class Machine {
        <<entity>>
        +MachineId machine_id
        +String machine_name
        +String os
        +String os_version
        +String ip_local
        +String ip_public
        +String mac_address
        +MachineStatus status
        +Timestamp last_heartbeat
        +Version desktop_version
        +Boolean baseline_compliance
    }
    class ToolVersion {
        <<value object>>
        +String tool_name
        +Version version
        +Timestamp last_checked_at
    }
    class PendingUpdate {
        <<value object>>
        +String tool_name
        +Version target_version
        +String status
    }
    class ActiveSession {
        <<value object>>
        +SessionId session_id
        +Timestamp started_at
        +String github_issue_url
        +String backlog_item_id
    }
    class SessionRecord {
        <<value object>>
        +SessionId session_id
        +Timestamp ended_at
        +Duration duration
    }
    class UptimeMetric {
        <<value object>>
        +Date date
        +Percentage uptime_pct
    }
    class ComplianceSnapshot {
        <<value object>>
        +Date date
        +Percentage compliance_score
    }
    class UpdateRecord {
        <<value object>>
        +Date date
        +String tool_name
        +String status
    }
    class TeamToolsBaseline {
        <<value object>>
        +Map~ToolName,ToolBaseline~ tools
    }
    class FleetAnalytics {
        <<value object>>
        +Map fleet_health
        +TrendMetrics fleet_uptime_trends
    }
    class MachineStatus {
        <<enumeration>>
        online
        sleeping
        offline
    }

    MachineRegistry "1" *-- "0..*" Machine : manages
    MachineRegistry "1" *-- "1" TeamToolsBaseline : enforces
    MachineRegistry "1" *-- "1" FleetAnalytics : aggregates
    Machine "1" *-- "0..*" ToolVersion : has installed
    Machine "1" *-- "0..*" PendingUpdate : queues
    Machine "1" *-- "0..*" ActiveSession : tracks active
    Machine "1" *-- "0..*" SessionRecord : archives
    Machine "1" *-- "0..*" UptimeMetric : records daily
    Machine "1" *-- "0..*" ComplianceSnapshot : records daily
    Machine "1" *-- "0..*" UpdateRecord : logs
    Machine --> MachineStatus : has status
```

## Relationship notes

- `MachineRegistry` is the single aggregate root (global singleton). `Machine` is
  its owned entity; everything else is an immutable value object.
- `TeamToolsBaseline` is a local copy of versions consumed from Technology Stack;
  compliance is computed against it, not against a live foreign aggregate.
- Active vs. archived sessions are deliberately split (`ActiveSession` in
  `copilot_sessions`, `SessionRecord` in `session_history`); likewise queued vs.
  executed updates (`PendingUpdate` → `UpdateRecord`).
- `ToolBaseline` and `TrendMetrics` (see `domain.md`) are nested value objects of
  `TeamToolsBaseline` and `FleetAnalytics` respectively.
