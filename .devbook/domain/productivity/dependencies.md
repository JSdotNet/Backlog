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
| [Tasks](../tasks/domain.md#task) | OHS + Published Language (Tasks = supplier) | Subscribes to `AIWorkLogged` | `.devbook/domain/tasks/domain.md#aiworklogged` | Productivity needs AI-assisted activity evidence linked to task work without reading Task internals. |
| [Sessions](../sessions/domain.md#session-log) | Customer/Supplier (Sessions = supplier) | Infrastructure adapter reads the Session Log directly (`AgentSessionAssistantSessionSource` maps `IAgentSessionSource` onto this context's `IAssistantSessionSource`). For [Hours worked](features.md#hours-worked), a port in `Backlog.Modules.Dashboard.Abstractions`, answered by an `Infrastructure.FileSystem` adapter over Sessions' agent-activity source | `.devbook/domain/sessions/domain.md#session-log`; for Hours worked, each session's [human turns](../sessions/domain.md#human-turn) and its runs | Productivity's Sessions part needs machine id, machine name, assistant, started at, last activity, the sources it could not read, and whether the read reached the horizon it asked for, to show what the agents have been doing — read since a horizon rather than as the capped list, so a count is never the session limit — a synchronous read of session facts, not a subscribed event. The Hours worked part reads the person's [working stretches](../roadmap/domain.md#working-stretch) from each session's human turns and runs, on this machine and the paired ones, and splits the [actual hours](../roadmap/domain.md#actual-hours) by [office hours](domain.md#office-hours). It counts the stretches as Roadmap Planning defines them, so the dashboard and the roadmap axis agree. Proposed, not built yet. |
| [Roadmap Planning](../roadmap/domain.md#day-override) | Shared Kernel (`WorkingHours` in `Backlog.SharedKernel`) | Reads the [day overrides](../roadmap/domain.md#day-override) inside the working week, which the roadmap sets and carries in its synced `planning-pace` document | `.devbook/arc42/adr/0019-roadmap-counts-the-working-week.md` | The Hours worked part applies the overrides to find each date's [office hours](domain.md#office-hours): a blocked date has none, and an unblocked date has its weekday's stored times. The hour grids do not read them. Proposed, not built yet. |
| [Monitoring & Dashboard](../monitoring/domain.md#progress-signal) | Customer/Supplier (Productivity = customer for session signals) | Consumes Copilot session progress signals | `.devbook/domain/monitoring/domain.md#progress-signal` | Productivity may correlate AI sessions with work outcomes when Monitoring already observes those sessions. Distinct from the Sessions row above: this is Monitoring's own `copilot_session` progress signal, not the session facts Productivity now reads directly from Sessions. |
| Anthropic Claude (external) | Conformist (Productivity accepts Anthropic's report shape) | Reads the Admin API usage and cost reports | `.devbook/domain/productivity/features.md#ai-vendor-usage-import` | Measured token and cost evidence for AI-assisted work. Organization-scoped: an Admin API key is required, and Anthropic does not offer these reports to individual accounts. |
| GitHub Copilot (external) | Conformist (Productivity accepts GitHub's report shape) | Reads organization Copilot seat activity and metrics reports | `.devbook/domain/productivity/features.md#ai-vendor-usage-import` | Copilot activity evidence. Organization-scoped and owner-only; GitHub publishes no endpoint for an individual subscriber's own usage. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Monitoring & Dashboard](../monitoring/domain.md#progress-signal) | OHS + Published Language (Productivity = supplier) | Subscribes to `ProductivityRecorded` | `.devbook/domain/productivity/domain.md#productivityrecorded` | Relies on productivity activity signals for dashboard views and attention rules. |
| [Tasks](../tasks/domain.md#task) | Customer/Supplier (Productivity = supplier) | Query productivity summaries by subject or period | `.devbook/domain/productivity/domain.md#productivity-analysis` | Roadmap and backlog views can show AI-assisted progress trends without calculating metrics themselves. |
| [Roadmap Planning](../roadmap/domain.md#working-week) | Shared Kernel (`WorkingHours` in `Backlog.SharedKernel`) | Reads the [working week](context.md#working-week) for placement and carries it in its synced `planning-pace` document | `.devbook/arc42/adr/0019-roadmap-counts-the-working-week.md` | Relies on seven independent days, each worked or not with its own start and end. A change to that shape changes how every effort-sized bar is counted. Proposed, not built yet. |

## Notes

- Productivity treats AI usage as personal insight, not surveillance or team
  performance scoring.
- Tasks remains the owner of work status; Productivity records contribution
  evidence and derived metrics only.
- Both vendor dependencies are organization-scoped by the vendors' own design.
  Without an organization there is no usage history to read, which is what
  `.devbook/domain/productivity/features.md#local-usage-accumulation`
  exists to answer.
- Session facts (machine, assistant, activity) come from Sessions via the
  infrastructure adapter above. Monitoring's own `copilot_session` signal remains
  a separate, narrower observation and is unaffected by this.
- The working stretch and actual hours are Roadmap Planning's terms. Productivity
  reads them as that context defines them and does not keep a second definition,
  so a change to the 30-minute gap changes both views together.