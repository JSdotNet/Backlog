# Flow: Monitoring & Dashboard

```meta
status: draft
```

> Lifecycle and process flows for this bounded context. Flows describe how
> signals move across contexts over time — complementary to `model.md`
> (structure) and `domain.md` (responsibilities/invariants).

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

- The only write Monitoring makes into another context is `FollowUpCaptured` to
  the Inbox, closing the observe → act loop.
