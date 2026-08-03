# Domain: Monitoring & Dashboard

> One chapter per Aggregate or Domain Service in this bounded context.
> Aggregate chapters include sub-chapters for their owned Entities, Value
> Objects, and Enums. Value Objects/Enums shared across multiple aggregates
> get their own chapter at the end instead of being duplicated.

Monitoring tracks progress across projects and repos, surfaces items that need
attention, and provides multi-layered dashboards. It aggregates Progress Signals
from [Backlog](../backlog/domain.md#aggregate-backlog-entry),
[Inbox](../inbox/domain.md#aggregate-inbox-item), GitHub, Application Insights,
[Dev PC Management](../dev-pc-management/domain.md#aggregate-machine-registry),
and [Repository Management](../repository-management/domain.md#aggregate-repository-registry),
and can run as a standalone team service.

## Aggregate: Progress Signal

```meta
status: draft
related: [.domain/backlog/domain.md#aggregate-backlog-entry, .domain/inbox/domain.md#aggregate-inbox-item]
```

An immutable, timestamped event indicating a change somewhere in the system,
captured from a domain or external source. The aggregate guarantees each signal
records its `Signal Type`, source, subject reference, and payload at detection
time, and is never mutated after capture — corrections arrive as new signals.
Signals are the raw material dashboards and attention rules read from.

### Entities

The Progress Signal aggregate is a single immutable record with no owned child
entities; its variable payload is held in the `Signal Payload` value object.

### Value Objects

#### Signal Payload

The type-specific body of a signal (e.g. `{item_id, new_status, changed_at}` for
a status change, `{queue_name, depth, age_max, processing_rate}` for queue depth,
`{machine_id, status, last_heartbeat}` for machine status). Immutable; equality
by value.

### Enums

#### Signal Type

Kind of change a signal represents:

- `status_change` — a backlog item changed status.
- `github_sync` — a GitHub issue linked to a backlog item updated.
- `app_insights` — an Application Insights metric/alert (error, latency).
- `queue_depth` — inbox/processing queue depth and rate.
- `inbox_age` — oldest unprocessed inbox item age.
- `automation_run` — an inbox automation executed (success/failure, items created).
- `copilot_session` — an active Copilot session update.
- `machine_status` — a dev PC heartbeat/state change.
- `team_aggregate` — a team-level rollup signal.

## Domain Service: Signal Aggregation

```meta
status: draft
```

Ingests signals from every source, correlates them (e.g. GitHub errors against
backlog delays), detects staleness/attention conditions against configurable
thresholds, and computes rollups including team-level aggregates. It is a service
because it spans many signal sources and produces cross-cutting views rather than
mutating one aggregate.

## Domain Service: Dashboard

```meta
status: draft
```

Composes multi-layer dashboards — project (Application Insights), backlog/GitHub
progress, inbox/queue health, Copilot sessions, and infrastructure (PC status,
repo health) — with role-based visibility for personal vs. team views. It emits
`FollowUpCaptured` back to the Inbox when a dashboard follow-up should become a
new item. It is a service because it reads across aggregates and contexts to
present, rather than own, state.

## Shared Enums

```meta
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Monitoring has a single aggregate; `Signal Type` is documented under it. This
chapter is reserved for future cross-aggregate enums.
