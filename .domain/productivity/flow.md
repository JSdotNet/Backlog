# Flow: Productivity

```meta
status: draft
```

> Lifecycle and process flows for this bounded context: how activities become
> productivity insight over time.

## AI-assisted activity recording

```mermaid
sequenceDiagram
    participant Backlog as Backlog Management
    participant Productivity as Productivity
    participant Monitor as Monitoring & Dashboard

    Backlog->>Productivity: AIWorkLogged
    Productivity->>Productivity: Append Productivity Entry
    Productivity->>Productivity: Recompute derived summaries
    Productivity-->>Monitor: ProductivityRecorded
```

- Backlog owns the work item and emits activity evidence.
- Productivity owns the measurement ledger and derived summaries.
- Monitoring may display productivity signals but does not change productivity
  records.