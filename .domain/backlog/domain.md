# Domain: Backlog Management

```meta
status: draft
order: ["features.md", "model.md", "flow.md", "dependencies.md", "naming.md"]
```

> One chapter per Aggregate or Domain Service in this bounded context.
> Aggregate chapters include sub-chapters for their owned Entities, Value
> Objects, and Enums. Value Objects/Enums shared across multiple aggregates
> get their own chapter at the end instead of being duplicated.

Backlog Management maintains a personal backlog of prompts, tasks, ideas, and
follow-ups across multiple projects and repos. It converts triaged
[Inbox Items](../inbox/domain.md#aggregate-inbox-item) into actionable,
prioritized Backlog Entries and projects them to external systems such as GitHub
and the Copilot CLI.

## Aggregate: Backlog Entry

```meta
status: draft
related: [.domain/inbox/domain.md#aggregate-inbox-item, .arc42/08-crosscutting-concepts.md#shared-data-types]
```

A refined, actionable item in the personal backlog and the consistency boundary
for all of its sub-items, projections, and usage history. It is the single source
of truth: one logical item with one priority and one status even when it targets
multiple repositories. Invariants: status only moves through the defined
lifecycle; all mutations to sub-items, projection references, AI work log events, and usage events go through the root; parent progress reflects sub-item completion; projections are created on `Ready → In Progress` (`EntryProjected`, one per `repo_id`) and closed on completion (`EntryCompleted`). A manually created entry starts at `draft` with no `source_inbox_id`.

The entry also carries two attributes that place it in the person's own working
set rather than in any external system: `area`, a free-form string ("repos",
"projects", "inbox", or whatever vocabulary the person actually uses) that files
the entry into a self-chosen grouping — blank is normalized to unfiled, and the
taxonomy is deliberately theirs, not a fixed enum; and `order`, a manual rank used
to hand-sequence entries within the backlog (entries that have never been ranked
share the default and fall back to recency). Both are freely re-settable and
carry no lifecycle invariant of their own.

### Entities

#### Sub-Item

An ordered breakdown step owned by the entry, with its own `title`,
`Sub-Item Status`, optional `notes`, and `order`. It has identity within the
aggregate but no meaning outside it. Sub-items can be reordered, added, or removed
independently and may project to GitHub issue task-list checkboxes.

### Value Objects

#### Projection Ref

An immutable link to a downstream external artifact created from the entry:
`repo_id`, `external_id`, and `target_type` (e.g. github-issue, cli-task).
Equality is by value.

#### Usage Event`r`n`r`nAn immutable audit record of a prompt copy/use: `timestamp` and `action`.`r`nEquality is by value.`r`n`r`n#### AI Work Log`r`n`r`nAn immutable record that an AI-assisted action contributed to the entry: `timestamp`, `ai_tool`, `activity_kind`, optional `session_id`, and optional `outcome_ref`. Equality is by value.

### Enums

#### Entry Type

Classification of the entry: `prompt`, `task`, `idea`, `follow_up`.

#### Entry Status

Lifecycle state: `draft`, `ready`, `in_progress`, `done`, `archived`.

#### Priority

Ranking of the entry: `low`, `medium`, `high`, `critical`.

#### Sub-Item Status

Completion state of a sub-item: `pending`, `done`.

## Domain Service: Projection

```meta
status: draft
related: [.domain/monitoring/domain.md#aggregate-progress-signal, .arc42/06-runtime-view.md#backlog-entry-to-github-issue]
```

Creates and closes downstream artifacts for a multi-repo entry: on `EntryProjected`
it creates one GitHub issue and/or CLI task per `repo_id`, recording each as a
`Projection Ref`; on `EntryCompleted` it closes all projections. It is a service
because it coordinates the entry with external systems (GitHub, Copilot CLI) and
spans multiple downstream artifacts rather than a single aggregate mutation. Invocation semantics: event-triggered policy / process manager. It reacts to `EntryProjected` and `EntryCompleted`; it is not invoked as part of a synchronous aggregate command.

## Domain Event: StatusChanged

```meta
status: draft
related: [.domain/backlog/domain.md#aggregate-backlog-entry, .domain/monitoring/domain.md#aggregate-progress-signal]
```

Published when a `Backlog Entry` changes `Entry Status`.

### Payload

- `backlog_item_id` - entry identifier.
- `previous_status` - prior `Entry Status`.
- `new_status` - new `Entry Status`.
- `changed_at` - time of the transition.
- `repo_ids` - targeted repositories for correlation.

### Consumers

- Monitoring & Dashboard, which turns work-state changes into progress signals.

### Published language rules

- The event reflects the durable work-state transition; consumers do not infer
  additional semantics from projection side effects.

## Domain Event: EntryProjected

```meta
status: draft
related: [.domain/backlog/domain.md#aggregate-backlog-entry, .domain/backlog/domain.md#domain-service-projection, .domain/monitoring/domain.md#aggregate-progress-signal]
```

Published when a `Backlog Entry` begins external execution and the Projection
policy creates one downstream artifact per targeted repository.

### Payload

- `backlog_item_id` - entry identifier.
- `repo_id` - repository receiving a projection.
- `projection_target` - `github-issue` or `copilot-task`.
- `external_id` - created downstream artifact identifier.
- `projected_at` - time the projection was created.

### Consumers

- Monitoring & Dashboard, which correlates planned work with external execution.

### Published language rules

- One event instance is emitted per projection target so consumers can reason
  about multi-repo fan-out without inspecting `Projection Ref` internals.

## Domain Event: EntryCompleted

```meta
status: draft
related: [.domain/backlog/domain.md#aggregate-backlog-entry, .domain/backlog/domain.md#domain-service-projection, .domain/monitoring/domain.md#aggregate-progress-signal]
```

Published when a completed `Backlog Entry` closes its downstream projections.

### Payload

- `backlog_item_id` - entry identifier.
- `repo_ids` - repositories whose projections are being closed.
- `closed_projection_ids` - downstream artifact identifiers that were closed.
- `completed_at` - time the projections were closed.

### Consumers

- Monitoring & Dashboard, which reconciles work completion against external systems.

### Published language rules

- Completion is owned by Backlog Management; external systems consume the closing
  signal but do not redefine what completion means.

## Domain Event: AIWorkLogged

```meta
status: draft
related: [.domain/backlog/domain.md#aggregate-backlog-entry, .domain/productivity/domain.md#aggregate-productivity-ledger]
```

Published when a Backlog Entry records that an AI-assisted action contributed to
the work item.

### Payload

- `backlog_item_id` - entry identifier.
- `ai_tool` - tool or channel used, such as Copilot CLI, GitHub Copilot App, IDE
  chat, or another AI assistant.
- `activity_kind` - planning, coding, review, summarization, research, or other
  user-facing category.
- `session_id` - optional source session identifier when one exists.
- `outcome_ref` - optional reference to a pull request, commit, issue, note, or
  generated artifact.
- `logged_at` - time the activity was recorded.

### Consumers

- Productivity, which turns AI-assisted activity into personal productivity
  metrics and trends.

### Published language rules

- The event records contribution evidence only; it does not claim productivity
  value by itself.
- Consumers conform to the published fields and do not inspect Backlog Entry
  internals to infer additional activity.`r`n`r`n## Shared Enums

```meta
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Backlog Management has a single aggregate; all enums are documented under the
Backlog Entry. This chapter is reserved for future cross-aggregate enums.
