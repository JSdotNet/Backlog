# Dependencies: Monitoring & Dashboard

```meta
status: draft
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Backlog](../backlog/domain.md#aggregate-backlog-entry) | OHS + Published Language (Backlog = supplier) | Subscribes to status/projection events | `.domain/backlog/domain.md#domain-event-statuschanged`, `.domain/backlog/domain.md#domain-event-entryprojected`, `.domain/backlog/domain.md#domain-event-entrycompleted` | Backlog status changes and projections produce progress signals. |
| [Inbox](../inbox/domain.md#aggregate-inbox-item) | Customer/Supplier (Monitoring = customer) | Queue-health feed and follow-up write-back | `.domain/inbox/features.md#feature-queue-health`, `.domain/monitoring/domain.md#domain-event-followupcaptured` | Inbox queue depth/age and automation health feed dashboards; follow-ups create new inbox items. |
| [Dev PC Management](../dev-pc-management/domain.md#aggregate-machine-registry) | OHS + Published Language (Dev PC Management = supplier) | Subscribes to `MachineStatusChanged` / `ComplianceUpdated` | `.domain/dev-pc-management/domain.md#domain-event-machinestatuschanged`, `.domain/dev-pc-management/domain.md#domain-event-complianceupdated` | Machine status, compliance, uptime, and session metrics feed the infrastructure dashboard. |
| [Repository Management](../repository-management/domain.md#aggregate-repository-registry) | Customer/Supplier (Monitoring = customer) | Health and scan feed | `.domain/repository-management/domain.md#aggregate-repository-registry` | Repo health scores, package freshness, and issue backlog feed the dashboard. |
| GitHub (external) | ACL | Polling / webhook | `.domain/backlog/domain.md#domain-event-statuschanged` | Issue status feeds progress signals and is compared against backlog status. |
| Application Insights (external) | ACL | Metric pull | `.domain/monitoring/features.md#sub-feature-project-dashboard-application-insights` | App performance/error metrics populate the project dashboard. |
| Copilot sessions (external / Dev PC) | ACL | Read through Dev PC tracking adapters | `.domain/dev-pc-management/domain.md#domain-service-copilot-session-tracking` | Session activity/status is shown alongside related issues/backlog items. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#aggregate-inbox-item) | OHS + Published Language (Monitoring = supplier) | Receives `FollowUpCaptured` | `.domain/monitoring/domain.md#domain-event-followupcaptured` | Relies on Monitoring emitting well-formed follow-up items. |

## Notes

- Monitoring is an observer/read context: it consumes signals from many contexts
  and only writes back to the Inbox via `FollowUpCaptured`.
- External integrations (GitHub, Application Insights, Copilot) sit behind
  anti-corruption adapters so their schemas never leak into the signal model.
- GitHub issue <-> backlog mismatch detection is shared with Backlog; see
  `.domain/backlog/dependencies.md`.
