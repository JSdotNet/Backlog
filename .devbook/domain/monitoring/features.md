# Monitoring & Dashboard

```meta
type: features
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Progress tracking

```meta
type: feature
status: draft
related: [.devbook/domain/tasks/features.md#projection]
```

Track what changed since the last review, per project and per repository:

- detect the items that moved status, were updated, or went stale;
- capture progress signals from notes, commits, GitHub issues, or manual updates.

## Attention surfacing

```meta
type: feature
status: draft
depends-on: [.devbook/domain/monitoring/features.md#progress-tracking]
```

Highlight items needing follow-up (overdue, blocked, untouched), surface stale
tasks and long-deferred inbox items, and alert on stuck queues or high
backlog counts.

## Multi-layer dashboards

```meta
type: feature
status: draft
```

Present layered dashboards across application health, backlog/GitHub progress,
queue health, and optional agent sessions.

### Project dashboard (Application Insights)

```meta
type: sub-feature
status: draft
```

Show the application's performance metrics, such as errors, latency and
availability, with cost and usage attribution. The metrics are correlated with
backlog status.

### Tasks and GitHub progress

```meta
type: sub-feature
status: draft
related: [.devbook/domain/tasks/features.md#projection]
```

Track GitHub issues linked to tasks, show status/milestone/assignee, and
flag mismatches (backlog done but issue open, or vice versa).

### Inbox and queue health

```meta
type: sub-feature
status: draft
related: [.devbook/domain/inbox/features.md#queue-health]
```

Unprocessed inbox count and oldest age, queue depth and processing rate, and
automation run status.

### Agent sessions

```meta
type: sub-feature
status: draft
related: [.devbook/domain/sessions/features.md#session-inventory]
```

Monitor the sessions [Sessions](../sessions/features.md#session-inventory) records,
for every agent, linked to issues and tasks, and alert when one is `stalled`.

## Multi-repo scanning

```meta
type: feature
status: draft
related: [.devbook/domain/repository-management/features.md#repository-health-scoring]
```

Scan the registered repositories for backlog changes and GitHub issue status,
aggregate signals across them, and show an activity timeline per repository.

## Automation status

```meta
type: feature
status: draft
related: [.devbook/domain/capture/features.md#automation-capture]
```

Show the last-run status of the [capture monitors](../capture/features.md#automation-capture),
alert on failed or missed runs, and display scan results and item counts.

## Team monitoring

```meta
type: feature
status: draft
```

Run Monitoring as a standalone team service with shared dashboards, configurable
team-wide vs. personal views, role-based visibility, aggregated cross-member
signals, and shared inbox/queue health.
