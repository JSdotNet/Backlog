# Dependencies: Monitoring & Dashboard

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the integration pattern for each
> relationship (synchronous call, domain/integration event, shared kernel,
> anti-corruption layer, etc.).

## Outbound dependencies

| Depends on (context/module) | Integration pattern | Why |
|---|---|---|
| [Backlog](../backlog/domain.md#aggregate-backlog-entry) | Subscribes to status/projection events | Backlog status changes and projections produce progress signals. |
| [Inbox](../inbox/domain.md#aggregate-inbox-item) | Subscribes to queue-health signals; emits `FollowUpCaptured` | Inbox queue depth/age and automation health feed dashboards; follow-ups create new inbox items. |
| [Dev PC Management](../dev-pc-management/domain.md#aggregate-machine-registry) | Subscribes to `MachineStatusChanged` / `ComplianceUpdated` | Machine status, compliance, uptime, and session metrics feed the infrastructure dashboard. |
| [Repository Management](../repository-management/domain.md#aggregate-repository-registry) | Subscribes to health/scan signals | Repo health scores, package freshness, and issue backlog feed the dashboard. |
| GitHub (external) | Polling / webhook (ACL) | Issue status feeds progress signals and is compared against backlog status. |
| Application Insights (external) | Metric pull (ACL) | App performance/error metrics populate the project dashboard. |
| Copilot sessions (external / Dev PC) | Read (optional) | Session activity/status is shown alongside related issues/backlog items. |

## Inbound dependents (known)

| Consumer (context/module) | Integration pattern | What it relies on |
|---|---|---|
| [Inbox](../inbox/domain.md#aggregate-inbox-item) | Receives `FollowUpCaptured` | Relies on Monitoring emitting well-formed follow-up items. |

## Notes

- Monitoring is an observer/read context: it consumes signals from many contexts
  and only writes back to the Inbox via `FollowUpCaptured`.
- External integrations (GitHub, Application Insights, Copilot) sit behind
  anti-corruption adapters so their schemas never leak into the signal model.
- GitHub issue ↔ backlog mismatch detection is shared with Backlog; see
  `.domain/backlog/dependencies.md`.
