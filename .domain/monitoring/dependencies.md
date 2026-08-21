# Monitoring & Dashboard

```meta
type: dependencies
status: draft
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Backlog](../backlog/domain.md#backlog-entry) | OHS + Published Language (Backlog = supplier) | Subscribes to status/projection events | `.domain/backlog/domain.md#statuschanged`, `.domain/backlog/domain.md#entryprojected`, `.domain/backlog/domain.md#entrycompleted` | Backlog status changes and projections produce progress signals. |
| [Inbox](../inbox/domain.md#inbox-item) | Customer/Supplier (Monitoring = customer) | Queue-health feed and follow-up write-back | `.domain/inbox/features.md#queue-health`, `.domain/monitoring/domain.md#followupcaptured` | Inbox queue depth/age and automation health feed dashboards; follow-ups create new inbox items. |
| [Dev PC Management](../dev-pc-management/domain.md#machine-registry) | OHS + Published Language (Dev PC Management = supplier) | Subscribes to `MachineStatusChanged` / `ComplianceUpdated` | `.domain/dev-pc-management/domain.md#machinestatuschanged`, `.domain/dev-pc-management/domain.md#complianceupdated` | Machine status, compliance, uptime, and session metrics feed the infrastructure dashboard. |
| [Repository Management](../repository-management/domain.md#repository-registry) | Customer/Supplier (Monitoring = customer) | Health and scan feed | `.domain/repository-management/domain.md#repository-registry` | Repo health scores, package freshness, and issue backlog feed the dashboard. |
| GitHub (external) | ACL | Polling / webhook | `.domain/backlog/domain.md#statuschanged` | Issue status feeds progress signals and is compared against backlog status. |
| Application Insights (external) | ACL | Metric pull | `.domain/monitoring/features.md#project-dashboard-application-insights` | App performance/error metrics populate the project dashboard. |
| [Sessions](../sessions/domain.md#session-log) | Customer/Supplier (Sessions = supplier) | Reads the session record an environment holds | `.domain/sessions/domain.md#session-log` | Session activity and state are shown alongside related issues/backlog items, and a `stalled` session is what an inactivity alert is raised on. Not built: this row replaced an ACL through Dev PC tracking adapters when the session subject moved to its own context. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#inbox-item) | OHS + Published Language (Monitoring = supplier) | Receives `FollowUpCaptured` | `.domain/monitoring/domain.md#followupcaptured` | Relies on Monitoring emitting well-formed follow-up items. |

## Notes

- Monitoring is an observer/read context: it consumes signals from many contexts
  and only writes back to the Inbox via `FollowUpCaptured`.
- External integrations (GitHub, Application Insights, Copilot) sit behind
  anti-corruption adapters so their schemas never leak into the signal model.
- GitHub issue <-> backlog mismatch detection is shared with Backlog; see
  `.domain/backlog/dependencies.md`.
