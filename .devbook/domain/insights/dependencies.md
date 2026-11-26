# Insights

```meta
type: dependencies
```

> Dependencies this bounded context has on other bounded contexts or modules, and
> known dependents. Use explicit DDD relationship semantics, integration
> mechanism details, and contract references.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Tasks](../tasks/domain.md#task) | OHS + Published Language (Tasks = supplier) | Subscribes to `AIWorkLogged` | `.devbook/domain/tasks/domain.md#aiworklogged` | AI-assisted activity evidence linked to task work, without reading Task internals. |
| [Sessions](../sessions/domain.md#session-log) | Customer/Supplier (Sessions = supplier) | Infrastructure adapter reads the Session Log directly; a port answers [Hours worked](features.md#hours-worked) | `.devbook/domain/sessions/domain.md#session-log`; for Hours worked, each session's [human turns](../sessions/domain.md#human-turn) and its runs | What the agents have been doing, and how long the person worked. |
| [Roadmap Planning](../roadmap/domain.md#day-override) | Shared Kernel (`WorkingHours` in `Backlog.SharedKernel`) | Reads the [day overrides](../roadmap/domain.md#day-override) inside the working week | `.devbook/arc42/adr/0019-roadmap-counts-the-working-week.md` | Finds each date's [office hours](domain.md#office-hours) for Hours worked. Proposed, not built yet. |
| [Roadmap Planning](../roadmap/domain.md#roadmap-item) | Customer/Supplier (Roadmap Planning = supplier) | Infrastructure adapter `RoadmapPlanProgressSource` answers `IPlanProgressSource` | `.devbook/domain/roadmap/domain.md#roadmap-item` | Each plan's done and planned effort and the pace in use, for the Tasks tab and Needs you. |
| [Devbook](../devbook/domain.md#sync-verdict) | Customer/Supplier (Devbook = supplier) | Infrastructure adapter `SyncVerdictDriftUnitSource` answers `IDriftSources` | `.devbook/domain/devbook/domain.md#sync-verdict` | The sync units that drifted, for the Devbook tab and Needs you. |
| [Repositories](../repositories/domain.md#repository-registry) | Customer/Supplier (Repositories = supplier) | `SettingsRepositoryDirectory` answers `IRepositoryDirectory` | `.devbook/domain/repositories/domain.md#repository-registry` | The registered repositories the dashboard's scope offers, archived ones skipped. |
| [Dev PC Management](../dev-pc-management/domain.md#machine) | Customer/Supplier (Dev PC Management = supplier) | Not built: the machine filter reads machines off the session records today | `.devbook/domain/dev-pc-management/domain.md#machine` | The machine a figure came from, once Dev PCs owns the Machine (local ADR 0073 §7). |
| Anthropic Claude (external) | Conformist (Insights accepts Anthropic's report shape) | Reads the Admin API usage and cost reports | `.devbook/domain/insights/features.md#ai-vendor-usage-import` | Measured token and cost evidence, read with an organization's Admin API key. |
| GitHub Copilot (external) | Conformist (Insights accepts GitHub's report shape) | Reads organization Copilot seat activity and metrics reports | `.devbook/domain/insights/features.md#ai-vendor-usage-import` | Copilot activity evidence, readable only by an organization's owner. |

- **Sessions, the integration:** `AgentSessionAssistantSessionSource` maps `IAgentSessionSource` onto this context's `IAssistantSessionSource`. For Hours worked, an `Infrastructure.FileSystem` adapter over Sessions' agent-activity source answers a port in `Backlog.Modules.Dashboard.Abstractions`.
- **Sessions, the Sessions part:** To show what the agents have been doing, the part reads session facts synchronously instead of subscribing to an event. It reads since a horizon, not as the capped list, so a count is never the session limit. It needs machine id and name, assistant, started-at and last activity, the sources it could not read, and whether the read reached the horizon.
- **Sessions, the Hours worked part:** The part reads the person's [working stretches](../roadmap/domain.md#working-stretch) from each session's human turns and runs, on this machine and the paired ones. It splits the [actual hours](../roadmap/domain.md#actual-hours) by [office hours](domain.md#office-hours). It counts stretches as Roadmap Planning defines them, so the dashboard and the roadmap axis agree. Proposed, not built yet.
- **Roadmap Planning:** The roadmap sets the overrides and carries them in its synced `planning-pace` document. A blocked date has no office hours, and an unblocked date has its weekday's stored times. The hour grids do not read the overrides.
- **Anthropic Claude and GitHub Copilot:** The reports are organization-scoped. Anthropic does not offer them to individual accounts, and GitHub publishes no endpoint for an individual subscriber's usage.

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Tasks](../tasks/domain.md#task) | Customer/Supplier (Insights = supplier) | Query productivity summaries by subject or period | `.devbook/domain/insights/domain.md#productivity-analysis` | AI-assisted progress trends in roadmap and backlog views, without calculating metrics. |
| [Roadmap Planning](../roadmap/domain.md#working-week) | Shared Kernel (`WorkingHours` in `Backlog.SharedKernel`) | Reads the [working week](context.md#working-week) for placement and carries it in its synced `planning-pace` document | `.devbook/arc42/adr/0019-roadmap-counts-the-working-week.md` | Relies on seven independent days. Proposed, not built yet. |

- **Roadmap Planning:** Each of the seven days is worked or not, with its own start and end. A change to that shape changes how every effort-sized bar is counted.

## Notes

- Insights treats AI usage as personal insight, not surveillance or team performance scoring.
- Tasks owns work status; Insights records contribution evidence and derived metrics only.
- Both vendor dependencies are organization-scoped by the vendors' own design. Without an organization there is no usage history to read, which `.devbook/domain/insights/features.md#local-usage-accumulation` exists to answer.
- The working stretch and actual hours are Roadmap Planning's terms. Insights keeps no second definition, so a change to the 30-minute gap changes both views together.