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

## Machine Registry

```meta
type: aggregate
status: draft
related: [.devbook/domain/technology-stack/domain.md#technology-registry, .devbook/domain/monitoring/domain.md#progress-signal, .devbook/arc42/08-crosscutting-concepts.md#shared-data-types, .devbook/domain/dev-pc-management/domain.invariants.md#machine-registry]
aliases: [MachineRegistry]
```

The global singleton that owns all registered machines, the team tool baseline,
and fleet analytics.

A machine's baseline compliance is computed from its installed tools against the
team baseline, and fleet analytics are aggregated from daily snapshots. The rules
the registry holds are in [its invariants](domain.invariants.md#machine-registry).

### Machine

```meta
type: entity
status: draft
aliases: [machine_id]
related: [.devbook/domain/dev-pc-management/domain.invariants.md#machine-registry]
```

A single development PC identified by `MachineId` (e.g. "work-laptop-001"). It
holds:

- its network identity: `ip_local`, `ip_public`, `mac_address`, `rdp_port`;
- `status`, `last_heartbeat`, and `desktop_version`, the desktop app's version;
- its installed tool versions and compliance flag;
- its queued updates, and its daily uptime, compliance and update history.

An `Environment` in [Sessions](../sessions/domain.md#environment) corresponds to
a Machine when the two name the same box. That is a lookup rather than a shared
identity: a Machine is registered, wakeable and compliance-tracked, and an
environment is wherever an agent can run. Why a Machine holds no sessions is in
[context.md](context.md).

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
`requested_at`, `status`, optional `error_message`.

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

### Last Tool Run

```meta
type: value-object
status: draft
aliases: [DevToolLastRun, last run]
related: [.devbook/domain/dev-pc-management/features.md#last-run-record]
```

The most recent finished run of the System tools pane on one machine: `kind`
(Update all, Update, Install, Clear stale cache), `started_at`, `finished_at`,
`outcome` (succeeded, failed, partly failed), one Tool Run Step per tool the run
touched, and the full `transcript` of the commands it ran and the message it
ended on. Invariants: a machine holds at most one, kept beside its per-PC tool
choices; each finished run replaces the one before it and no history or trend
is kept; a run still in progress is never recorded; and the outcome is partly
failed exactly when at least one step succeeded and at least one failed. A
missing or unreadable record reads as none.

The transcript is redacted before the record is written, because the record can
sit inside a synced storage root: this machine's MCP token is masked by value —
read, never minted to be masked — and so is every known secret shape a tool's
output can carry, whoever passed it in: a `Bearer` credential, an upper-case
`…_TOKEN=`, `…_API_KEY=` or similar assignment, a non-`Bearer` `Authorization:`
header, an `X-Api-Key:`-style header, a `?token=`-style query value, and a token
its issuer marks by prefix (`ghp_`, `github_pat_`, `sk-`, `xox…-`, `AKIA`, a
signed JWT). Each masked value reads `[redacted]`; a `${…}` placeholder and text
matching no shape are kept byte for byte.

### Tool Run Step

```meta
type: value-object
status: draft
aliases: [DevToolRunStep]
```

One tool's part in a Last Tool Run: `name`, `from_version` (none for an install),
`to_version` (the version the run was taking it to), `outcome` (succeeded or
failed), and `duration`.

### Tool Catalog Entry

```meta
type: value-object
status: draft
aliases: [DevToolApplication, DevToolMcpServer, catalog entry]
related: [.devbook/domain/dev-pc-management/features.md#copilot-tool-catalog, .devbook/domain/dev-pc-management/features.md#overview-tab]
```

One tool the repository's catalog declares: its kind (plugin, marketplace, MCP
server, application), the id it is addressed by, how it is installed, the hosts
it is for, whether it is enabled, and, for an application or an MCP server, an
optional `download` page — where somebody installing it by hand gets it.
Invariants: the id is what every per-PC override and every action addresses, so
an entry without one is refused; a property the entry leaves out means its
default, so a catalog written before `download` existed reads as naming no page;
and the Add tool path writes `download` only as an http or https address, while
a hand-edited value is kept as written and only ever linked when it is one.

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
aliases: [TeamToolsBaseline]
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
aliases: [MachineStatus]
```

Current state of a machine: `online`, `sleeping`, `offline`.

## Remote Control

```meta
type: domain-service
status: draft
related: [.devbook/arc42/06-runtime-view.md#remote-pc-wake-and-status-update, .devbook/arc42/07-deployment-view.md#cloud-deployment-azure]
```

Coordinates out-of-band actions on registered machines:

- [Wake-on-LAN](features.md#wake-on-lan), local and cloud-relayed, with wake
  verification;
- remote desktop brokering, over RDP or VNC, with an optional relay for machines
  behind NAT or a firewall;
- authorization and encryption of every wake and connect.

It is a service because these actions span the cloud relay, connection
brokering, and the target OS rather than a single machine's stored state.
Invocation semantics: command-invoked orchestration service.

## Remote Update

```meta
type: domain-service
status: draft
related: [.devbook/domain/technology-stack/domain.md#technology-registry]
```

Queues and executes tool updates across machines, singly, targeted, or in bulk,
and requires authorization. The desktop app runs each update through the
machine's native package manager and reports its progress, with rollback on
failure. How an update moves through its states is drawn in
[Tool update lifecycle](flow.md#tool-update-lifecycle).

It is a service because it orchestrates queued work against offline and online
machines and external package managers. Invocation semantics: command-invoked
orchestration service.

## MachineStatusChanged

```meta
type: domain-event
status: draft
related: [.devbook/domain/dev-pc-management/domain.md#machine-registry]
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
related: [.devbook/domain/dev-pc-management/domain.md#machine-registry]
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
