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
related: [.devbook/domain/productivity/domain.md#productivity-ledger, .devbook/domain/tasks/domain.md#aiworklogged]
```

[AI Productivity Tracking](domain.md#ai-productivity-tracking) shows what changed
because of AI assistance: tasks moved, artifacts created, prompts reused, reviews shortened, or
research summarized.

### AI activity capture

```meta
type: sub-feature
status: draft
```

Record AI-assisted activity from task work, Copilot sessions, IDE chats, and
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
feature-flag: .devbook/domain/productivity/context.md#ai-usage-metrics
related: [.devbook/domain/productivity/dependencies.md#outbound-dependencies]
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
status: draft
related: [.devbook/domain/productivity/features.md#ai-vendor-usage-import]
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
Tasks itself, which is a narrower claim than the vendor reports make, and that
narrowing has to stay visible to the person reading the numbers.

## Personal productivity dashboard

```meta
type: feature
status: draft
feature-flag: .devbook/domain/productivity/context.md#dashboard
setting: [.devbook/domain/productivity/context.md#working-week, .devbook/domain/productivity/context.md#weekly-usage-reset]
related: [.devbook/domain/monitoring/features.md#multi-layer-dashboards]
```

Expose productivity trends as personal insight rather than team performance
reporting. The view shows patterns and evidence while preserving the user's
control over interpretation.

### What the sessions cost and shipped

```meta
type: sub-feature
status: draft
related: [.devbook/domain/sessions/features.md#what-a-session-cost-and-what-it-shipped]
```

As of 2026-09-23 the assistant-sessions part also reads what the session records
carry beyond time: **output tokens**, per usage week and cut by model or by repository,
and **pull requests linked**, per week and by the repository each lives in — counted
once however many sessions linked one. Both are charts of their own rather than lines
on the hours chart, because a count of tokens and a count of hours are two measures.
Each figure is over the sessions that could say, and says it is partial when that is
fewer than the sessions counted: Copilot records neither, and a missing figure is
never read as zero.

The limit-hits tile says what overage did about each refusal: fell back to overage, a
wall for the reason the assistant gave ("org spend cap reached"), or nothing said.
And a Claude session now sits in the band of the registered clone its folder lies in,
rather than in "No repository recorded" with every other Claude session.

### Hours worked

```meta
type: sub-feature
status: draft
setting: [.devbook/domain/productivity/context.md#working-week]
related: [.devbook/domain/productivity/requirements.md#hours-worked, .devbook/domain/productivity/domain.md#office-hours, .devbook/domain/roadmap/domain.md#actual-hours, .devbook/domain/roadmap/domain.md#working-stretch, .devbook/domain/roadmap/domain.md#day-override, .devbook/domain/productivity/dependencies.md#outbound-dependencies]
```

Show how long the person actually worked, and how much of it fell outside the
hours they meant to work. The Hours worked part of the dashboard shows, per day
and per week over the period the dashboard's selector picks, the person's
[actual hours](../roadmap/domain.md#actual-hours). It splits them into the time
**inside** and **outside** their [office hours](domain.md#office-hours), and sets
both beside the planned hours of the working week.

Actual hours are the union of the person's
[working stretches](../roadmap/domain.md#working-stretch), as Roadmap Planning
defines them, so the axis and this part count the same time. Office hours apply
the [day overrides](../roadmap/domain.md#day-override) the person set on the
roadmap. A day off they blocked therefore counts all of its work outside, and a
Saturday they unblocked has office hours from its stored times.

It is presentation only, like the hour grids. No other dashboard figure changes
and no work is left out. What it promises is in
[the requirements](requirements.md#hours-worked).

### Drift at a glance

```meta
type: sub-feature
status: draft
related: [.devbook/domain/devbook/features.md#sync-verdicts-beside-a-chapter, .devbook/domain/devbook/domain.md#sync-verdict, .devbook/ai/05-unattended-runs.md]
```

Show how far each repository's devbook has drifted from its code, without opening a
chapter. The Drift at a glance part of the dashboard lists every sync unit a devbook
sweep has verified, grouped by the direction the unit goes — push, pull, sync,
report, or off — each with the last verdict the sweep reached on it, what the sweep
did about it, and when. A verdict leads to the pull request or drift issue the sweep
opened. Below the units it lists the `devbook-drift` issues still open, those
labelled `sync-failed` first: a sweep tried that unit, did not finish, and will not
try again until a person clears the label. A unit whose drift issue carries the
label is marked in its row too.

The direction is the one the sweep read when it verified the unit, so a group is
also the sweep that owns its units. A unit no sweep has verified is not listed.
The repository chips narrow the part; the window and the machine do not, because a
verdict is the latest a sweep left and an issue is open now. The verdicts are this
machine's and the issues are GitHub's, and either shows without the other: issues
that cannot be read leave the units standing and say why.

### Whether each plan will land in its window

```meta
type: sub-feature
status: draft
feature-flag: .devbook/domain/productivity/context.md#dashboard
related: [.devbook/domain/roadmap/features.md#placing-a-plan-in-time, .devbook/domain/roadmap/features.md#forecasting-work-in-flight, .devbook/domain/roadmap/context.md#story-points-a-week, .devbook/arc42/adr/0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md#5-placed_by_import-and-what-a-re-import-may-touch]
tests: [unit:dotnet:Backlog.Modules.Dashboard.UnitTests.TaskInsightsTests, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapPlanProgressSourceTests, integration:dotnet:Backlog.Desktop.UI.UnitTests.DashboardPaneTests.An_item_in_two_repositories_says_the_pace_of_each_part_it_was_projected_at]
```

See, without opening the roadmap, which plans will land inside the window they
were given. The Roadmap part of the dashboard lists the plans whose window overlaps
the weeks the selector picks, together with every plan whose window is still sized
by its effort. Each plan says how its work stands: finished, on track, behind,
overdue, not sized, or with no pace to read it at.

A plan placed by hand or by its due date is projected one repository part at a
time, the same parts [Placing a plan in time](../roadmap/features.md#placing-a-plan-in-time)
lays out. Each part's estimated work left is spent at its own repository's pace in
use. It starts on the later of today and the day after the parts it waits on end.
The plan is judged on the latest part end: on track when that falls inside its
window, behind when it falls after. A plan whose window is still sized by its effort
is already laid out that way, so the part reports the window the roadmap stores.
An unestimated task counts nothing here, because the part reports it as unestimated.
A figure that also guessed at its size would count it twice. A plan has no pace only
when no part with work left has one. Where the parts with work left run at different
paces, the plan names each of them, for example "app 8, site 4 points a week".
