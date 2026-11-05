# Monitoring & Dashboard

```meta
type: features
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Progress tracking

```meta
type: feature
related: [.devbook/domain/tasks/features.md#projection]
```

Track what changed since the last review, per project and per repository. Detect the items that moved status, were updated, or went stale, and capture progress signals from notes, commits, GitHub issues or manual updates.

## Attention surfacing

```meta
type: feature
depends-on: [.devbook/domain/monitoring/features.md#progress-tracking]
```

Show what needs a person's attention:

- highlight items needing follow-up (overdue, blocked, untouched);
- surface stale tasks and long-deferred inbox items;
- alert on stuck queues or high backlog counts.

## Multi-layer dashboards

```meta
type: feature
```

Present layered dashboards across:

- application health;
- backlog/GitHub progress;
- queue health;
- optional agent sessions.

### Project dashboard (Application Insights)

```meta
type: sub-feature
```

Show the application's performance metrics with cost and usage attribution. The
metrics include errors, latency and availability, and are correlated with backlog
status.

### Tasks and GitHub progress

```meta
type: sub-feature
related: [.devbook/domain/tasks/features.md#projection]
```

Track GitHub issues linked to tasks, show status/milestone/assignee, and
flag mismatches (backlog done but issue open, or vice versa).

### Inbox and queue health

```meta
type: sub-feature
related: [.devbook/domain/inbox/features.md#queue-health]
```

Show the unprocessed inbox count and oldest age, the queue depth and processing
rate, and the automation run status.

### Agent sessions

```meta
type: sub-feature
related: [.devbook/domain/sessions/features.md#session-inventory]
```

Monitor the sessions [Sessions](../sessions/features.md#session-inventory) records
for every agent, linked to issues and tasks. Alert when one is `stalled`.

## Multi-repo scanning

```meta
type: feature
related: [.devbook/domain/repository-management/features.md#repository-health-scoring]
```

Scan the registered repositories for backlog changes and GitHub issue status,
aggregate signals across them, and show an activity timeline per repository.

## Automation status

```meta
type: feature
related: [.devbook/domain/capture/features.md#automation-capture]
```

Show the last-run status of the [capture monitors](../capture/features.md#automation-capture),
alert on failed or missed runs, and display scan results and item counts.

## Team monitoring

```meta
type: feature
```

Run Monitoring as a standalone team service with:

- shared dashboards;
- configurable team-wide vs. personal views;
- role-based visibility;
- aggregated cross-member signals;
- shared inbox/queue health.
