# Dev PC Management

```meta
type: domain
status: draft
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context; each chapter's
> `type` records which of those it is. An Aggregate's owned Entities, Value
> Objects, and Enums are chapters directly beneath it, typed `entity`,
> `value-object`, and `enum`. Value Objects/Enums shared across multiple
> aggregates get their own chapter at the end instead of being duplicated.

Dev PC Management supports multiple development PCs from a single interface:
register machines, wake them remotely, start remote desktop sessions, monitor
tool versions and compliance against the
[Technology Stack](../technology-stack/domain.md#technology-registry)
baseline, and trigger remote updates.

It used to track Copilot sessions too, back when Copilot was the only agent the
machines ran. That subject is
[Sessions](../sessions/domain.md#session-log) now — "which
agent worked where, for how long" turned out to be a different question in a
different language from "how is this PC configured", and it needed to describe a
second agent without a parallel list. What stays here is the machine; what left is
everything about what ran on it.

## Machine Registry

```meta
type: aggregate
status: draft
related: [.domain/technology-stack/domain.md#technology-registry, .domain/monitoring/domain.md#progress-signal, .arc42/08-crosscutting-concepts.md#shared-data-types]
```

The global singleton that owns all registered machines, the team tool baseline,
and fleet analytics. Invariants: a machine must be registered before it can
receive updates or remote connections; status transitions are validated (offline
→ online requires an explicit heartbeat); baseline compliance is computed from
each machine's installed tools versus the team baseline; and fleet analytics are
aggregated from daily snapshots. All machine mutations go through the root.

### Machine

```meta
type: entity
status: draft
```

A single development PC identified by `MachineId` (e.g. "work-laptop-001"). Holds
network identity (`ip_local`, `ip_public`, `mac_address`, `rdp_port`), `status`,
`last_heartbeat`, `desktop_version`, installed tool versions, compliance flag,
queued updates, and daily uptime/compliance/update history. Invariants:
`pending_updates` is bounded per machine; a completed or failed update moves from
`pending_updates` to `updates_history`.

It holds no sessions. An `Environment` in
[Sessions](../sessions/naming.md#environment) corresponds to a
Machine when the two name the same box, but that is a lookup rather than a shared
identity — a Machine is registered, wakeable and compliance-tracked, and an
environment is wherever an agent can run.

### Tool Version

```meta
type: value-object
status: draft
```

An installed tool's version: `tool_name`, `version`, `last_checked_at`. Equality
by `(tool_name, version)`.

### Pending Update

```meta
type: value-object
status: draft
```

A queued tool update: `tool_name`, `target_version`, `requested_by`,
`requested_at`, `status`, optional `error_message`. On completion/failure it is
archived to an `Update Record`.

### Uptime Metric

```meta
type: value-object
status: draft
```

Daily uptime: `date`, `uptime_pct`, `last_heartbeat`, `downtime_min`.

### Compliance Snapshot

```meta
type: value-object
status: draft
```

Daily tool compliance: `date`, `compliant_tools_count`, `outdated_tools_count`,
`compliance_score`. Computed:
`compliance_score = compliant / (compliant + outdated) * 100`.

### Update Record

```meta
type: value-object
status: draft
```

A completed tool update: `date`, `tool_name`, `target_version`, `status`
(success/failed/rollback), `duration_sec`, `initiated_by` (auto/manual/user-id).

### Tool Baseline

```meta
type: value-object
status: draft
```

Version requirement for one tool: `tool_name`, `min_version`,
`recommended_version`.

### Team Tools Baseline

```meta
type: value-object
status: draft
```

The team's required versions for all tracked tools: `tools`
(name → Tool Baseline), `last_updated`, `updated_by`. Consumed from the
Technology Stack domain (see `dependencies.md`).

### Fleet Health Snapshot

```meta
type: value-object
status: draft
```

Aggregate fleet status: `date`, `total_machines`, `online_count`,
`offline_count`, `average_uptime_pct`, `average_compliance_score`.

### Trend Metrics

```meta
type: value-object
status: draft
```

Multi-period rolling averages: 7-day, 30-day, 90-day.

### Fleet Analytics

```meta
type: value-object
status: draft
```

Aggregated fleet metrics: daily `fleet_health`, `fleet_uptime_trends`,
`fleet_compliance_trends`.

### Machine Status

```meta
type: enum
status: draft
```

Current state of a machine: `online`, `sleeping`, `offline`.

## Remote Control

```meta
type: domain-service
status: draft
related: [.arc42/06-runtime-view.md#remote-pc-wake-and-status-update, .arc42/07-deployment-view.md#cloud-deployment-azure]
```

Coordinates out-of-band actions on registered machines: Wake-on-LAN (local and
cloud-relayed) with wake verification, remote desktop brokering (RDP/VNC,
optional relay for NAT/firewall), and authorization/encryption of every wake and
connect. It is a service because these actions span the cloud relay, connection
brokering, and the target OS rather than a single machine's stored state. Invocation semantics: command-invoked orchestration service.

## Remote Update

```meta
type: domain-service
status: draft
related: [.domain/technology-stack/domain.md#technology-registry]
```

Queues and executes tool updates across machines (single, targeted, or bulk),
runs them via native package managers on the desktop component, reports progress
(in-progress/completed/failed) with rollback, and requires authorization. It is a
service because it orchestrates queued work against offline/online machines and
external package managers. Invocation semantics: command-invoked orchestration service.

## MachineStatusChanged

```meta
type: domain-event
status: draft
related: [.domain/dev-pc-management/domain.md#machine-registry, .domain/monitoring/domain.md#progress-signal]
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

## ComplianceUpdated

```meta
type: domain-event
status: draft
related: [.domain/dev-pc-management/domain.md#machine-registry, .domain/monitoring/domain.md#progress-signal, .domain/technology-stack/domain.md#technology-registry]
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
type: shared-enums
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Dev PC Management has a single aggregate; `Machine Status` is documented under
it. This chapter is reserved for future cross-aggregate enums.
