# Productivity

```meta
type: features
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## AI productivity tracking

```meta
type: feature
status: draft
related: [.domain/productivity/domain.md#productivity-ledger, .domain/backlog/domain.md#aiworklogged]
```

Track when AI contributes to personal work and show what changed because of that
assistance: tasks moved, artifacts created, prompts reused, reviews shortened, or
research summarized.

### AI activity capture

```meta
type: sub-feature
status: draft
```

Record AI-assisted activity from backlog work, Copilot sessions, IDE chats, and
automation runs with enough context to understand the work without copying tool
internals.

### Productivity summaries

```meta
type: sub-feature
status: draft
```

Summarize AI-assisted work by day, week, repository, activity kind, and tool so
the person can see how AI affects throughput and focus.

### Time-saved estimates

```meta
type: sub-feature
status: draft
```

Capture optional estimates or calibrated defaults for time saved, keeping the
estimate visibly separate from measured activity.

### AI vendor usage import

```meta
type: sub-feature
status: draft
feature-flag: usage-metrics
related: [.domain/productivity/dependencies.md#outbound-dependencies]
```

Import token, cost, and session usage from the AI vendors the person works
through, so productivity insight rests on measured usage rather than
recollection. Both vendors report at organization level only: Claude exposes
usage and cost reports to an organization holding an Admin API key, and GitHub
reports Copilot usage per organization to an organization owner. The import is
evidence for the ledger; it never becomes the ledger.

### Local usage accumulation

```meta
type: sub-feature
status: idea
related: [.domain/productivity/features.md#ai-vendor-usage-import]
```

Accumulate usage locally, call by call, for the person who has no organization
behind their AI subscription.

Neither vendor offers a personal usage history. Anthropic documents the Admin
API as unavailable to individual accounts, and GitHub has no endpoint at all for
an individual Copilot subscriber's own usage — its billing page is the only
route, by manual export. For those people the only measurable signal is what
each response reports about itself: Claude returns per-request token counts on
every message it answers.

The capability is therefore to record those per-response counts as they arrive
and roll them up by day, model, and work subject — the same shape the vendor
import produces, so summaries do not care which route the evidence took. Kept as
an idea rather than a plan because it only measures work that flows through
Backlog itself, which is a narrower claim than the vendor reports make, and that
narrowing has to stay visible to the person reading the numbers.

## Personal productivity dashboard

```meta
type: feature
status: draft
feature-flag: dashboard
related: [.domain/monitoring/features.md#multi-layer-dashboards]
```

Expose productivity trends as personal insight rather than team performance
reporting. The view shows patterns and evidence while preserving the user's
control over interpretation.