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
related: [.domain/tasks/features.md#projection]
```

Track what changed since the last review per project/repo, detect items that
moved status / were updated / went stale, and capture progress signals from
notes, commits, GitHub issues, or manual updates.

## Attention surfacing

```meta
type: feature
status: draft
depends-on: [.domain/monitoring/features.md#progress-tracking]
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
queue health, and optional Copilot sessions.

### Project dashboard (Application Insights)

```meta
type: sub-feature
status: draft
```

Application performance metrics (errors, latency, availability), cost/usage
attribution, correlated with backlog status.

### Tasks and GitHub progress

```meta
type: sub-feature
status: draft
related: [.domain/tasks/features.md#projection]
```

Track GitHub issues linked to tasks, show status/milestone/assignee, and
flag mismatches (backlog done but issue open, or vice versa).

### Inbox and queue health

```meta
type: sub-feature
status: draft
related: [.domain/inbox/features.md#queue-health]
```

Unprocessed inbox count and oldest age, queue depth and processing rate, and
automation run status.

### Copilot sessions

```meta
type: sub-feature
status: draft
related: [.domain/sessions/features.md#session-inventory]
```

Monitor active Copilot sessions linked to issues/tasks and alert when a
session stalls.

## Multi-repo scanning

```meta
type: feature
status: draft
related: [.domain/repository-management/features.md#repository-health-scoring]
```

Scan registered repos for backlog changes and GitHub issue status, aggregate
signals across repos, and show a repo-level activity timeline.

## Automation status

```meta
type: feature
status: draft
related: [.domain/capture/features.md#automation-capture]
```

Show last-run status of inbox automations, alert on failed/missed runs, and
display scan results and item counts.

## Team monitoring

```meta
type: feature
status: draft
```

Run Monitoring as a standalone team service with shared dashboards, configurable
team-wide vs. personal views, role-based visibility, aggregated cross-member
signals, and shared inbox/queue health.
