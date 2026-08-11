# Features: Productivity

```meta
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: AI productivity tracking

```meta
status: draft
related: [.domain/productivity/domain.md#aggregate-productivity-ledger, .domain/backlog/domain.md#domain-event-aiworklogged]
```

Track when AI contributes to personal work and show what changed because of that
assistance: tasks moved, artifacts created, prompts reused, reviews shortened, or
research summarized.

### Sub-feature: AI activity capture

```meta
status: draft
```

Record AI-assisted activity from backlog work, Copilot sessions, IDE chats, and
automation runs with enough context to understand the work without copying tool
internals.

### Sub-feature: Productivity summaries

```meta
status: draft
```

Summarize AI-assisted work by day, week, repository, activity kind, and tool so
the person can see how AI affects throughput and focus.

### Sub-feature: Time-saved estimates

```meta
status: draft
```

Capture optional estimates or calibrated defaults for time saved, keeping the
estimate visibly separate from measured activity.

## Feature: Personal productivity dashboard

```meta
status: draft
related: [.domain/monitoring/features.md#feature-multi-layer-dashboards]
```

Expose productivity trends as personal insight rather than team performance
reporting. The view shows patterns and evidence while preserving the user's
control over interpretation.