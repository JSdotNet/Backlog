# Productivity

```meta
status: draft
type: context
```

Productivity turns AI-assisted work activity into personal insight for the one
person using it, and owns none of the work it measures.

Inside the boundary: the productivity ledger and the summaries read off it, and
the choices that shape how a week is read. These are the reader's own working
week, which follows them between devices, and when their weekly assistant
allowance resets, which stays on each device. The monthly budget for each
assistant's spend is one of them too, and it also stays on each device.

Outside it: task work, repository state, and completion decisions, which
[Tasks](../tasks/domain.md#task) and
[Repository Management](../repository-management/domain.md#repository-registry)
answer; agent session facts, which
[Sessions](../sessions/domain.md#session-log) answers. The model itself is in [domain.md](domain.md).

## Working week

```meta
status: draft
type: setting
key: working-hours.json
scope: user
default: Monday to Friday 09:00-17:30; Saturday and Sunday not worked
related: [.devbook/domain/productivity/features.md#personal-productivity-dashboard, .devbook/domain/productivity/features.md#hours-worked, .devbook/domain/productivity/domain.md#office-hours, .devbook/domain/roadmap/features.md#placing-a-plan-in-time, .devbook/domain/roadmap/domain.md#working-week, .devbook/domain/roadmap/domain.md#day-override, .devbook/domain/roadmap/features.md#blocking-and-unblocking-a-day, .devbook/domain/roadmap/features.md#setting-days-off-in-a-list, .devbook/arc42/adr/0019-roadmap-counts-the-working-week.md]
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.WorkingHoursSettingsStoreTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsWorkingHoursTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.DashboardPaneTests.The_grid_outlines_the_hours_of_the_working_week]
```

Which hours the reader meant to be working, as seven independent days — each one
worked or not, and each with its own start and end. Seven days rather than one
range plus a set of working days, because a reader who starts at seven on
Fridays cannot say so with a single range.

On the dashboard it is presentation, and deliberately only that: the hour grids
outline the hours the week claims, and nothing else changes. No dashboard figure
is recomputed and no work is excluded — agent-active time at eleven at night
counts exactly as much as time at eleven in the morning, and a grid that quietly
dropped out-of-hours work would answer a different question from the one its
tile is named for. What the outline buys the reader is the ability to see the
difference themselves.

The roadmap does count it
([ADR 0019](../../arc42/adr/0019-roadmap-counts-the-working-week.md), proposed).
A plan sized by its effort is counted over the hours this week claims and skips
the days not worked. A pace's week is this week's total hours. Day and week
columns show their working hours, and days not worked are shaded. See
[Placing a plan in time](../roadmap/features.md#placing-a-plan-in-time).

This setting holds the weekly pattern only. A single date that differs from it,
a holiday or a worked Saturday, is a
[day override](../roadmap/domain.md#day-override). The person sets it on the
roadmap, by pressing a day head
([Blocking and unblocking a day](../roadmap/features.md#blocking-and-unblocking-a-day))
or in the Days off dialog
([Setting days off in a list](../roadmap/features.md#setting-days-off-in-a-list)).
It travels inside the same `workingWeek` key.

The dashboard's hour grids still read the weekly pattern only. They ignore the
overrides. The dashboard's
[Hours worked](features.md#hours-worked) part reads the overrides too, because
it splits the person's actual hours by their
[office hours](domain.md#office-hours), and a blocked date has none.

An hour is outlined when **any part** of it falls inside that day's hours, so a
day ending at 17:30 outlines the 17:00 hour: there is no half-marked cell to
draw, and disowning the hour would claim that work at ten past five happened out
of hours.

Per value:

- **A day marked not worked** outlines nothing on that day, and keeps the hours
  it had — turning it back on restores them rather than the defaults.
- **A day whose end is at or before its start** outlines nothing. The settings
  screen refuses such a range with a message naming the hours it will not take,
  so the state is reachable only by editing the file by hand; a grid that
  outlined every hour because two times were the wrong way round would be worse
  than one that outlined none.
- **No file, or one nobody can read,** is the default week. A day the file does
  not mention comes from the default too, rather than being read as a day off —
  nothing stored means nobody said, and answering "not a working day" on the
  reader's behalf is the assumption this surface exists to avoid.

Changed on the settings screen, one day at a time, and stored beside the app's
other per-device choices — its own file rather than a section of the workspace
pointer, which would then be rewritten every time somebody moved Friday's
finishing time. Times are written as `HH:mm` so the file, the field, and the
reader all spell them the same way.

`scope: user` fits: the choice is one person's, and it follows that person. With
sync on, the week travels as a `workingWeek` key inside the roadmap's synced
`planning-pace` document, newest change winning (ADR 0019). A change here writes
`working-hours.json` and that key together. A week pulled from another device
replaces this file. So `working-hours.json` stays the device's copy, and the
dashboard keeps reading it. A pace document without the key leaves the file
alone, and the device's next change writes the key.

## Weekly usage reset

```meta
status: draft
type: setting
key: usage-reset.json
scope: user
default: unset
related: [.devbook/domain/productivity/features.md#personal-productivity-dashboard]
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.UsageResetSettingsStoreTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.DashboardPaneTests.Under_a_usage_reset_the_grid_says_its_rows_are_cut_at_the_reset]
```

When the assistant's weekly usage allowance starts again: a day of the week and
a time, recurring every seven days.

It decides where the dashboard cuts a week. The columns of the weekly charts and
the seven days of the hour grids run from one reset to the next rather than from
a calendar Monday, so a week on screen is the week the allowance is actually
spent in. It changes no figure, only where the boundaries fall.

The time is on this machine's clock rather than UTC, because the reset is a fact
the reader copies off the assistant's own usage screen in their own clock
("Resets Mon 2:00 PM"), and a setting that asked them to convert it would be
asking for the one piece of arithmetic most often got wrong.

Per value:

- **Set** — the weeks are anchored on the most recent occurrence at or before
  now, resolved through the zone's offset so a reset falling in a
  daylight-saving gap or overlap still names one instant rather than failing.
- **Unset**, which is how it ships — detection stands in: the reset reported by
  the most recent seven-day limit the assistant refused a request on, and past
  that, plain calendar weeks. A file that does not parse reads as unset too.

A configured reset outranks a detected one on purpose, because the usage records
on a machine may come from an older plan or an older month, and the surface says
which of the three cuts won rather than leaving the reader to guess.

Set and cleared on the settings screen; clearing removes the file and hands the
week back to detection. Stored per device beside the working week. Unlike the
working week, nothing syncs it, so `scope: user` is only the nearest rung the
vocabulary offers: the same reader on a second machine starts again unset.

## Monthly spend budget

```meta
type: setting
key: spend-budgets.json
scope: user
default: no budget for any provider
related: [.devbook/domain/productivity/features.md#spend-against-a-monthly-budget, .devbook/domain/productivity/features.md#what-needs-you, .devbook/domain/productivity/features.md#personal-productivity-dashboard]
```

How much the reader means to spend on each assistant in a calendar month. There is
one amount for each spend provider: Claude (Anthropic), GitHub Copilot and the Azure
AI Foundry resource. Each amount stands alone, because the providers' figures differ
in kind and possibly in currency, and one budget across all three would compare
unlike amounts.

The dashboard sets each provider's spend so far against its budget, and draws the
budget line on the spend chart. A provider projected past its budget by the end of
the month is listed under
[What needs you](features.md#what-needs-you). The projection is described in
[Spend against a monthly budget](features.md#spend-against-a-monthly-budget).

Per value:

- **An amount** sets that provider's budget for every month until the reader
  changes it.
- **Empty**, which is how it ships, means that provider has no budget. Its card
  shows the spend without a budget to set it against, and it never appears in
  What needs you.

An amount is a bare number with no currency. It is read in the currency that
provider reports its spend in, because Backlog holds no exchange rate. A negative
amount, or text that is not an amount, is refused with a message, and the budget in
force stays. A file entry that cannot be read is dropped, and the other budgets in
the file are kept.

Set in Dashboard settings and stored per device beside the weekly usage reset.
Clearing the last budget removes the file.
Like the reset, nothing syncs it, so `scope: user` is the nearest rung the
vocabulary offers: the same reader on a second machine starts with no budgets.

## AI usage metrics

```meta
status: draft
type: feature-flag
key: usage-metrics
default: off
related: [".devbook/domain/productivity/features.md#ai-vendor-usage-import"]
```

Turning it on reads Claude and GitHub Copilot usage from their organization APIs as evidence for the productivity figures. Both sources are organization-scoped — Claude needs an API key an organization admin can use, GitHub needs organization-owner access — which is why it ships off. Marked `DEV`. The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a person's choice is kept in `features.json` beside the app's other per-device choices. It is retired when the import is released and the credentials, not the switch, decide whether it runs.

## Dashboard

```meta
status: draft
type: feature-flag
key: dashboard
default: on
related: [".devbook/domain/productivity/features.md#personal-productivity-dashboard"]
```

Turning it on lets the reader open the full-screen dashboard of their productivity and what their assistants cost from the app chrome. The dashboard is six tabs and opens on the Overview every time; [Six tabs and what each answers](features.md#six-tabs-and-what-each-answers) says what each holds. On by default and marked `DEV`. The key is `DashboardFeatures.Dashboard` in the Dashboard module; the string is unchanged from when it lived on `MonitoringFeatures`, so nobody's switched-off dashboard came back. The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a person's choice is kept in `features.json` beside the app's other per-device choices. It is retired when the dashboard leaves `DEV`.
