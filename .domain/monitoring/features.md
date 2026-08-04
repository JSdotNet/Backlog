# Features: Monitoring & Dashboard

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: Progress tracking

```meta
status: draft
related: [.domain/backlog/features.md#feature-projection]
```

Track what changed since the last review per project/repo, detect items that
moved status / were updated / went stale, and capture progress signals from
notes, commits, GitHub issues, or manual updates.

## Feature: Attention surfacing

```meta
status: draft
depends-on: [.domain/monitoring/features.md#feature-progress-tracking]
```

Highlight items needing follow-up (overdue, blocked, untouched), surface stale
backlog entries and long-deferred inbox items, and alert on stuck queues or high
backlog counts.

## Feature: Multi-layer dashboards

```meta
status: draft
```

Present layered dashboards across application health, backlog/GitHub progress,
queue health, and optional Copilot sessions.

### Sub-feature: Project dashboard (Application Insights)

```meta
status: draft
```

Application performance metrics (errors, latency, availability), cost/usage
attribution, correlated with backlog status.

### Sub-feature: Backlog and GitHub progress

```meta
status: draft
related: [.domain/backlog/features.md#feature-projection]
```

Track GitHub issues linked to backlog items, show status/milestone/assignee, and
flag mismatches (backlog done but issue open, or vice versa).

### Sub-feature: Inbox and queue health

```meta
status: draft
related: [.domain/inbox/features.md#feature-queue-health]
```

Unprocessed inbox count and oldest age, queue depth and processing rate, and
automation run status.

### Sub-feature: Copilot sessions

```meta
status: draft
related: [.domain/dev-pc-management/features.md#feature-copilot-session-tracking]
```

Monitor active Copilot sessions linked to issues/backlog items and alert when a
session stalls.

## Feature: Multi-repo scanning

```meta
status: draft
related: [.domain/repository-management/features.md#feature-repository-health-scoring]
```

Scan registered repos for backlog changes and GitHub issue status, aggregate
signals across repos, and show a repo-level activity timeline.

## Feature: Automation status

```meta
status: draft
related: [.domain/capture/features.md#feature-automation-capture]
```

Show last-run status of inbox automations, alert on failed/missed runs, and
display scan results and item counts.

## Feature: Team monitoring

```meta
status: draft
```

Run Monitoring as a standalone team service with shared dashboards, configurable
team-wide vs. personal views, role-based visibility, aggregated cross-member
signals, and shared inbox/queue health.
