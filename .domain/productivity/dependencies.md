# Dependencies: Productivity

```meta
status: draft
```

> Dependencies this bounded context has on other bounded contexts or modules, and
> known dependents. Use explicit DDD relationship semantics, integration
> mechanism details, and contract references.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Backlog Management](../backlog/domain.md#aggregate-backlog-entry) | OHS + Published Language (Backlog = supplier) | Subscribes to `AIWorkLogged` | `.domain/backlog/domain.md#domain-event-aiworklogged` | Productivity needs AI-assisted activity evidence linked to backlog work without reading Backlog Entry internals. |
| [Monitoring & Dashboard](../monitoring/domain.md#aggregate-progress-signal) | Customer/Supplier (Productivity = customer for session signals) | Consumes Copilot session progress signals | `.domain/monitoring/domain.md#aggregate-progress-signal` | Productivity may correlate AI sessions with work outcomes when Monitoring already observes those sessions. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Monitoring & Dashboard](../monitoring/domain.md#aggregate-progress-signal) | OHS + Published Language (Productivity = supplier) | Subscribes to `ProductivityRecorded` | `.domain/productivity/domain.md#domain-event-productivityrecorded` | Relies on productivity activity signals for dashboard views and attention rules. |
| [Backlog Management](../backlog/domain.md#aggregate-backlog-entry) | Customer/Supplier (Productivity = supplier) | Query productivity summaries by subject or period | `.domain/productivity/domain.md#domain-service-productivity-analysis` | Roadmap and backlog views can show AI-assisted progress trends without calculating metrics themselves. |

## Notes

- Productivity treats AI usage as personal insight, not surveillance or team
  performance scoring.
- Backlog remains the owner of work status; Productivity records contribution
  evidence and derived metrics only.