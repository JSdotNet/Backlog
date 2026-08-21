# Monitoring & Dashboard

```meta
type: domain
status: draft
order: ["features.md", "model.md", "flow.md", "dependencies.md", "naming.md"]
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context; each chapter's
> `type` records which of those it is. An Aggregate's owned Entities, Value
> Objects, and Enums are chapters directly beneath it, typed `entity`,
> `value-object`, and `enum`. Value Objects/Enums shared across multiple
> aggregates get their own chapter at the end instead of being duplicated.

Monitoring tracks progress across projects and repos, surfaces items that need
attention, and provides multi-layered dashboards. It aggregates Progress Signals
from [Backlog](../backlog/domain.md#backlog-entry),
[Inbox](../inbox/domain.md#inbox-item), GitHub, Application Insights,
[Dev PC Management](../dev-pc-management/domain.md#machine-registry),
and [Repository Management](../repository-management/domain.md#repository-registry),
and can run as a standalone team service.

## Progress Signal

```meta
type: aggregate
status: draft
related: [.domain/backlog/domain.md#backlog-entry, .domain/inbox/domain.md#inbox-item, .arc42/08-crosscutting-concepts.md#shared-data-types]
```

An immutable, timestamped event indicating a change somewhere in the system,
captured from a domain or external source. The aggregate guarantees each signal
records its `Signal Type`, source, subject reference, and payload at detection
time, and is never mutated after capture — corrections arrive as new signals.
Signals are the raw material dashboards and attention rules read from.

The Progress Signal aggregate is a single immutable record with no owned child
entities; its variable payload is held in the `Signal Payload` value object.

### Signal Payload

```meta
type: value-object
status: draft
```

The type-specific body of a signal (e.g. `{item_id, new_status, changed_at}` for
a status change, `{queue_name, depth, age_max, processing_rate}` for queue depth,
`{machine_id, status, last_heartbeat}` for machine status). Immutable; equality
by value.

### Signal Type

```meta
type: enum
status: draft
```

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

## Signal Aggregation

```meta
type: domain-service
status: draft
```

Ingests signals from every source, correlates them (e.g. GitHub errors against
backlog delays), detects staleness/attention conditions against configurable
thresholds, and computes rollups including team-level aggregates. It is a service
because it spans many signal sources and produces cross-cutting views rather than
mutating one aggregate. Invocation semantics: event-triggered read-side service consuming published signals from other contexts.

## Dashboard

```meta
type: domain-service
status: draft
related: [.arc42/08-crosscutting-concepts.md#observability]
```

Composes multi-layer dashboards — project (Application Insights), backlog/GitHub
progress, inbox/queue health, Copilot sessions, and infrastructure (PC status,
repo health) — with role-based visibility for personal vs. team views. It emits
`FollowUpCaptured` back to the Inbox when a dashboard follow-up should become a
new item. It is a service because it reads across aggregates and contexts to
present, rather than own, state. Invocation semantics: query/composition service invoked when a dashboard view or follow-up decision is requested.

## FollowUpCaptured

```meta
type: domain-event
status: draft
related: [.domain/monitoring/domain.md#dashboard, .domain/inbox/domain.md#inbox-item]
```

Published by `Dashboard` when an observed condition should become a new Inbox item.
It is the only write-back contract from Monitoring into the core workflow.

### Payload

- `follow_up_title` - generated title for the new item.
- `body_md` - dashboard summary or remediation notes.
- `source` - monitoring source or dashboard that raised the follow-up.
- `related_subject_ref` - backlog item, repository, machine, or issue reference.
- `captured_at` - time the follow-up was raised.

### Consumers

- Inbox, which creates a new `Inbox Item` for human or automated triage.

### Published language rules

- Monitoring owns the observation payload; Inbox owns the resulting item lifecycle.
- `FollowUpCaptured` is the only feedback-loop write allowed from Monitoring into
  another bounded context.

## Shared Enums

```meta
type: shared-enums
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Monitoring has a single aggregate; `Signal Type` is documented under it. This
chapter is reserved for future cross-aggregate enums.
