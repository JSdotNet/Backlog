# Productivity

```meta
type: dependencies
status: draft
```

> Dependencies this bounded context has on other bounded contexts or modules, and
> known dependents. Use explicit DDD relationship semantics, integration
> mechanism details, and contract references.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Tasks](../tasks/domain.md#task) | OHS + Published Language (Tasks = supplier) | Subscribes to `AIWorkLogged` | `.domain/tasks/domain.md#aiworklogged` | Productivity needs AI-assisted activity evidence linked to task work without reading Task internals. |
| [Monitoring & Dashboard](../monitoring/domain.md#progress-signal) | Customer/Supplier (Productivity = customer for session signals) | Consumes Copilot session progress signals | `.domain/monitoring/domain.md#progress-signal` | Productivity may correlate AI sessions with work outcomes when Monitoring already observes those sessions. |
| Anthropic Claude (external) | Conformist (Productivity accepts Anthropic's report shape) | Reads the Admin API usage and cost reports | `.domain/productivity/features.md#ai-vendor-usage-import` | Measured token and cost evidence for AI-assisted work. Organization-scoped: an Admin API key is required, and Anthropic does not offer these reports to individual accounts. |
| GitHub Copilot (external) | Conformist (Productivity accepts GitHub's report shape) | Reads organization Copilot seat activity and metrics reports | `.domain/productivity/features.md#ai-vendor-usage-import` | Copilot activity evidence. Organization-scoped and owner-only; GitHub publishes no endpoint for an individual subscriber's own usage. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Monitoring & Dashboard](../monitoring/domain.md#progress-signal) | OHS + Published Language (Productivity = supplier) | Subscribes to `ProductivityRecorded` | `.domain/productivity/domain.md#productivityrecorded` | Relies on productivity activity signals for dashboard views and attention rules. |
| [Tasks](../tasks/domain.md#task) | Customer/Supplier (Productivity = supplier) | Query productivity summaries by subject or period | `.domain/productivity/domain.md#productivity-analysis` | Roadmap and backlog views can show AI-assisted progress trends without calculating metrics themselves. |

## Notes

- Productivity treats AI usage as personal insight, not surveillance or team
  performance scoring.
- Tasks remains the owner of work status; Productivity records contribution
  evidence and derived metrics only.
- Both vendor dependencies are organization-scoped by the vendors' own design.
  Without an organization there is no usage history to read, which is what
  `.domain/productivity/features.md#local-usage-accumulation`
  exists to answer.