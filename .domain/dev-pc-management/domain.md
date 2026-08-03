# Domain: Dev PC Management

> One chapter per Aggregate or Domain Service in this bounded context.
> Aggregate chapters include sub-chapters for their owned Entities, Value
> Objects, and Enums. Value Objects/Enums shared across multiple aggregates
> get their own chapter at the end instead of being duplicated.

Dev PC Management supports multiple development PCs from a single interface:
register machines, wake them remotely, start remote desktop sessions, monitor
tool versions and compliance against the
[Technology Stack](../technology-stack/domain.md#aggregate-technology-registry)
baseline, trigger remote updates, and track Copilot sessions.

## Aggregate: Machine Registry

```meta
status: draft
related: [.domain/technology-stack/domain.md#aggregate-technology-registry, .domain/monitoring/domain.md#aggregate-progress-signal]
```

The global singleton that owns all registered machines, the team tool baseline,
and fleet analytics. Invariants: a machine must be registered before it can
receive updates or remote connections; status transitions are validated (offline
→ online requires an explicit heartbeat); baseline compliance is computed from
each machine's installed tools versus the team baseline; and fleet analytics are
aggregated from daily snapshots. All machine mutations go through the root.

### Entities

#### Machine

A single development PC identified by `MachineId` (e.g. "work-laptop-001"). Holds
network identity (`ip_local`, `ip_public`, `mac_address`, `rdp_port`), `status`,
`last_heartbeat`, `desktop_version`, installed tool versions, compliance flag,
queued updates, active/archived Copilot sessions, and daily uptime/compliance/
update history. Invariants: `pending_updates` is bounded per machine; a completed
or failed update moves from `pending_updates` to `updates_history`; active
sessions live in `copilot_sessions`, completed ones in `session_history`.

### Value Objects

#### Tool Version

An installed tool's version: `tool_name`, `version`, `last_checked_at`. Equality
by `(tool_name, version)`.

#### Pending Update

A queued tool update: `tool_name`, `target_version`, `requested_by`,
`requested_at`, `status`, optional `error_message`. On completion/failure it is
archived to an `Update Record`.

#### Active Session

An active Copilot session on the machine: `session_id`, `started_at`,
`last_activity_at`, optional `github_issue_url` and `backlog_item_id`.

#### Session Record

A completed/archived Copilot session: `session_id`, `started_at`, `ended_at`,
`duration`, optional `github_issue_url` and `backlog_item_id`.

#### Uptime Metric

Daily uptime: `date`, `uptime_pct`, `last_heartbeat`, `downtime_min`.

#### Compliance Snapshot

Daily tool compliance: `date`, `compliant_tools_count`, `outdated_tools_count`,
`compliance_score`. Computed:
`compliance_score = compliant / (compliant + outdated) * 100`.

#### Update Record

A completed tool update: `date`, `tool_name`, `target_version`, `status`
(success/failed/rollback), `duration_sec`, `initiated_by` (auto/manual/user-id).

#### Tool Baseline

Version requirement for one tool: `tool_name`, `min_version`,
`recommended_version`.

#### Team Tools Baseline

The team's required versions for all tracked tools: `tools`
(name → Tool Baseline), `last_updated`, `updated_by`. Consumed from the
Technology Stack domain (see `dependencies.md`).

#### Fleet Health Snapshot

Aggregate fleet status: `date`, `total_machines`, `online_count`,
`offline_count`, `average_uptime_pct`, `average_compliance_score`.

#### Trend Metrics

Multi-period rolling averages: 7-day, 30-day, 90-day.

#### Fleet Analytics

Aggregated fleet metrics: daily `fleet_health`, `fleet_uptime_trends`,
`fleet_compliance_trends`.

### Enums

#### Machine Status

Current state of a machine: `online`, `sleeping`, `offline`.

## Domain Service: Remote Control

```meta
status: draft
```

Coordinates out-of-band actions on registered machines: Wake-on-LAN (local and
cloud-relayed) with wake verification, remote desktop brokering (RDP/VNC,
optional relay for NAT/firewall), and authorization/encryption of every wake and
connect. It is a service because these actions span the cloud relay, connection
brokering, and the target OS rather than a single machine's stored state.

## Domain Service: Remote Update

```meta
status: draft
related: [.domain/technology-stack/domain.md#aggregate-technology-registry]
```

Queues and executes tool updates across machines (single, targeted, or bulk),
runs them via native package managers on the desktop component, reports progress
(in-progress/completed/failed) with rollback, and requires authorization. It is a
service because it orchestrates queued work against offline/online machines and
external package managers.

## Domain Service: Copilot Session Tracking

```meta
status: draft
related: [.domain/backlog/domain.md#aggregate-backlog-entry, .domain/monitoring/domain.md#aggregate-progress-signal]
```

Records Copilot session start/end per machine, links sessions to GitHub issues or
backlog items when available, alerts on stalled/inactive sessions, and archives
history for audit. It is a service because session identity may originate cloud-
or locally and must be correlated with external work items.

## Shared Enums

```meta
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Dev PC Management has a single aggregate; `Machine Status` is documented under
it. This chapter is reserved for future cross-aggregate enums.
