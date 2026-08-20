# Domain: Dev PC Management

```meta
status: draft
order: ["features.md", "model.md", "flow.md", "dependencies.md", "naming.md"]
```

> One chapter per Aggregate or Domain Service in this bounded context.
> Aggregate chapters include sub-chapters for their owned Entities, Value
> Objects, and Enums. Value Objects/Enums shared across multiple aggregates
> get their own chapter at the end instead of being duplicated.

Dev PC Management supports multiple development PCs from a single interface:
register machines, wake them remotely, start remote desktop sessions, monitor
tool versions and compliance against the
[Technology Stack](../technology-stack/domain.md#aggregate-technology-registry)
baseline, and trigger remote updates.

It used to track Copilot sessions too, back when Copilot was the only agent the
machines ran. That subject is
[Sessions](../sessions/domain.md#aggregate-session-log) now — "which
agent worked where, for how long" turned out to be a different question in a
different language from "how is this PC configured", and it needed to describe a
second agent without a parallel list. What stays here is the machine; what left is
everything about what ran on it.

## Aggregate: Machine Registry

```meta
status: draft
related: [.domain/technology-stack/domain.md#aggregate-technology-registry, .domain/monitoring/domain.md#aggregate-progress-signal, .arc42/08-crosscutting-concepts.md#shared-data-types]
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
queued updates, and daily uptime/compliance/update history. Invariants:
`pending_updates` is bounded per machine; a completed or failed update moves from
`pending_updates` to `updates_history`.

It holds no sessions. An `Environment` in
[Sessions](../sessions/naming.md#term-environment) corresponds to a
Machine when the two name the same box, but that is a lookup rather than a shared
identity — a Machine is registered, wakeable and compliance-tracked, and an
environment is wherever an agent can run.

### Value Objects

#### Tool Version

An installed tool's version: `tool_name`, `version`, `last_checked_at`. Equality
by `(tool_name, version)`.

#### Pending Update

A queued tool update: `tool_name`, `target_version`, `requested_by`,
`requested_at`, `status`, optional `error_message`. On completion/failure it is
archived to an `Update Record`.

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
related: [.arc42/06-runtime-view.md#remote-pc-wake-and-status-update, .arc42/07-deployment-view.md#cloud-deployment-azure]
```

Coordinates out-of-band actions on registered machines: Wake-on-LAN (local and
cloud-relayed) with wake verification, remote desktop brokering (RDP/VNC,
optional relay for NAT/firewall), and authorization/encryption of every wake and
connect. It is a service because these actions span the cloud relay, connection
brokering, and the target OS rather than a single machine's stored state. Invocation semantics: command-invoked orchestration service.

## Domain Service: Remote Update

```meta
status: draft
related: [.domain/technology-stack/domain.md#aggregate-technology-registry]
```

Queues and executes tool updates across machines (single, targeted, or bulk),
runs them via native package managers on the desktop component, reports progress
(in-progress/completed/failed) with rollback, and requires authorization. It is a
service because it orchestrates queued work against offline/online machines and
external package managers. Invocation semantics: command-invoked orchestration service.

## Domain Event: MachineStatusChanged

```meta
status: draft
related: [.domain/dev-pc-management/domain.md#aggregate-machine-registry, .domain/monitoring/domain.md#aggregate-progress-signal]
```

Published when a registered machine changes runtime status.

### Payload

- `machine_id` - machine identifier.
- `previous_status` - prior `Machine Status`.
- `new_status` - new `Machine Status`.
- `last_heartbeat` - latest heartbeat observed.
- `changed_at` - time the change was recognized.

### Consumers

- Monitoring & Dashboard, which turns machine-state changes into infrastructure signals.

### Published language rules

- Runtime status is owned here; consumers do not infer compliance or session state
  from this event unless that meaning is explicitly carried in the payload.

## Domain Event: ComplianceUpdated

```meta
status: draft
related: [.domain/dev-pc-management/domain.md#aggregate-machine-registry, .domain/monitoring/domain.md#aggregate-progress-signal, .domain/technology-stack/domain.md#aggregate-technology-registry]
```

Published when a machine's compliance score is recalculated against the current
team tools baseline.

### Payload

- `machine_id` - machine identifier.
- `compliance_score` - current score.
- `baseline_version` - baseline snapshot used for the calculation.
- `out_of_date_tools` - tools outside the baseline.
- `updated_at` - time the score was recalculated.

### Consumers

- Monitoring & Dashboard, which shows compliance health.
- Technology Stack, which can correlate adoption lag against approved baselines.

### Published language rules

- Compliance meaning is anchored to the supplied baseline snapshot; consumers do
  not reinterpret the score without the corresponding baseline context.

## Shared Enums

```meta
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Dev PC Management has a single aggregate; `Machine Status` is documented under
it. This chapter is reserved for future cross-aggregate enums.
