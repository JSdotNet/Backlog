# Monitoring & Dashboard

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

## Progress Signal

```meta
type: aggregate
status: draft
related: [.devbook/domain/tasks/domain.md#task, .devbook/domain/inbox/domain.md#inbox-item, .devbook/arc42/08-crosscutting-concepts.md#shared-data-types]
aliases: [ProgressSignal, Signal]
```

An immutable, timestamped record of a change somewhere in the system, captured
from a domain or an external source. The aggregate guarantees that each signal
records its `Signal Type`, source, subject reference and payload at detection
time. A signal is never changed after capture; a correction arrives as a new
signal. Signals are the raw material that dashboards and attention rules read.

The Progress Signal aggregate is a single immutable record with no owned child
entities; its variable payload is held in the `Signal Payload` value object.

### Signal Payload

```meta
type: value-object
status: draft
aliases: [SignalPayload]
```

The type-specific body of a signal. Its keys depend on the `Signal Type`, for
example:

| Signal type | Payload |
| --- | --- |
| `status_change` | `{item_id, new_status, changed_at}` |
| `queue_depth` | `{queue_name, depth, age_max, processing_rate}` |
| `machine_status` | `{machine_id, status, last_heartbeat}` |

A payload is immutable, and two payloads are equal when their values are.

### Signal Type

```meta
type: enum
status: draft
aliases: [SignalType]
```

Kind of change a signal represents:

- `status_change` — a task changed status.
- `github_sync` — a GitHub issue linked to a task updated.
- `app_insights` — an Application Insights metric or alert, such as an error or
  latency.
- `queue_depth` — Inbox or processing queue depth and rate.
- `inbox_age` — the age of the oldest unprocessed Inbox item.
- `automation_run` — a [capture monitor](../capture/features.md#automation-capture)
  ran: whether it succeeded, and how many items it created.
- `copilot_session` — a session update read from Sessions. `copilot_session` keeps
  its old name although it covers every agent.
- `machine_status` — a dev PC heartbeat or state change.
- `team_aggregate` — a team-level rollup signal.

## Signal Aggregation

```meta
type: domain-service
status: draft
```

Ingests signals from every source and correlates them, for example GitHub errors
against backlog delays. It detects staleness and attention conditions against
configurable thresholds, and computes rollups, team-level aggregates among them. It
is a service because it spans many signal sources and produces cross-cutting views
rather than changing one aggregate.

Invocation semantics: event-triggered read-side service, consuming the signals other
contexts publish.

## Dashboard

```meta
type: domain-service
status: draft
related: [.devbook/arc42/08-crosscutting-concepts.md#observability]
```

Composes dashboards in layers, with role-based visibility for personal and team
views:

- the project layer, from Application Insights;
- backlog and GitHub progress;
- Inbox and queue health;
- agent sessions, from Sessions;
- infrastructure: PC status and repository health.

It emits `FollowUpCaptured` when a dashboard follow-up should become a new Inbox
item. It is a service because it reads across aggregates and contexts to present
state rather than own it.

Invocation semantics: query/composition service, invoked when a dashboard view or a
follow-up decision is requested.

## FollowUpCaptured

```meta
type: domain-event
status: draft
related: [.devbook/domain/monitoring/domain.md#dashboard]
```

Published by `Dashboard` when an observed condition should become a new Inbox item.

### Payload

- `follow_up_title` - generated title for the new item.
- `body_md` - dashboard summary or remediation notes.
- `source` - monitoring source or dashboard that raised the follow-up.
- `related_subject_ref` - task, repository, machine, or issue reference.
- `captured_at` - time the follow-up was raised.

### Consumers

- Inbox, which creates a new `Inbox Item` for human or automated triage.

### Published language rules

- Monitoring owns the observation payload; Inbox owns the resulting item lifecycle.
- `FollowUpCaptured` is the only write Monitoring makes into another bounded
  context. It closes the loop from observing a condition to acting on it.

## Shared Enums

```meta
type: shared-enums
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Monitoring has a single aggregate; `Signal Type` is documented under it. This
chapter is reserved for future cross-aggregate enums.
