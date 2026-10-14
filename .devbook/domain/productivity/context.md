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

On the dashboard the working week is presentation, and deliberately only that.
The hour grids outline the hours the week claims, and nothing else changes. No
dashboard figure is recomputed and no work is excluded. Agent-active time at
eleven at night counts exactly as much as time at eleven in the morning. A grid
that quietly dropped out-of-hours work would answer a different question from the
one its tile is named for. The outline lets the reader see the difference
themselves.

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
day ending at 17:30 outlines the 17:00 hour. There is no half-marked cell to draw.
Disowning the hour would claim that work at ten past five happened out of hours.

Per value:

- **A day marked not worked** outlines nothing on that day, and keeps the hours
  it had. Turning it back on restores them rather than the defaults.
- **A day whose end is at or before its start** outlines nothing. The settings
  screen refuses such a range with a message naming the hours it will not take.
  The state is therefore reachable only by editing the file by hand. A grid that
  outlined every hour because two times were the wrong way round would be worse
  than one that outlined none.
- **No file, or one nobody can read,** is the default week. A day the file does
  not mention comes from the default too, rather than being read as a day off.
  Nothing stored means nobody said. Answering "not a working day" on the reader's
  behalf is the assumption this surface exists to avoid.

The person changes it on the settings screen, one day at a time. It is stored
beside the app's other per-device choices, in its own file. A section of the
workspace pointer would be rewritten every time somebody moved Friday's finishing
time. Times are written as `HH:mm`, so the file, the field, and the reader all
spell them the same way.

The Working week page's headline says the week in force in one sentence:
`You work Mon–Fri, 09:00–17:30.` A run of three worked days or more reads as a
range and the rest as a list, so `Mon, Tue, Thu, Fri` shows where the gap is.
The hours are named only when every worked day shares them; otherwise the
sentence ends `each day at its own hours`, and a week with no worked day says
`You have no working days set.` It reads the stored week, so a refused time
never reaches it.

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
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.UsageResetSettingsStoreTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.DashboardPaneTests.Under_a_usage_reset_the_grid_says_it_is_a_calendar_week_beside_reset_columns, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.SessionInsightsTests.A_grid_is_a_calendar_week_and_names_the_usage_week_beginning_in_it]
```

When the assistant's weekly usage allowance starts again: a day of the week and
a time, recurring every seven days.

It decides where the dashboard cuts a week. The columns of the Sessions tab's
weekly charts run from one reset to the next rather than from a calendar Monday,
so a column is the week the allowance is actually spent in. It changes no figure,
only where the boundaries fall.

The hour grid beside those charts does not follow the reset. It is a calendar
week, Monday to Sunday, so its seven rows are seven whole days and its "week N"
title names the week the reader knows. Pressing a column opens the grid on the
calendar week that usage week begins in, and the grid's caption says the columns
run from the reset.

The time is on this machine's clock rather than UTC. The reader copies the reset
off the assistant's own usage screen in their own clock ("Resets Mon 2:00 PM").
A setting that asked them to convert it would be asking for the one piece of
arithmetic most often got wrong.

Three cuts can decide where a week starts. The first that applies wins:

| Rank | Cut | Applies when | Where the weeks are anchored |
| --- | --- | --- | --- |
| 1 | Configured | The setting is set | The most recent occurrence at or before now, resolved through the zone's offset |
| 2 | Detected | The setting is unset, which is how it ships, or its file does not parse | The reset reported by the most recent seven-day limit the assistant refused a request on |
| 3 | Calendar | Neither of the above | Plain calendar weeks |

Resolving through the zone's offset means a reset falling in a daylight-saving gap
or overlap still names one instant rather than failing. A configured reset
outranks a detected one on purpose. The usage records on a machine may come from
an older plan or an older month. The surface says which of the three cuts won,
rather than leaving the reader to guess.

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
one amount for each [Spend Provider](domain.md#spend-provider). Each amount stands
alone. The providers' figures differ in kind and may be in different currencies,
so one budget across all of them would compare unlike amounts.

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
provider reports its spend in, because Backlog holds no exchange rate. A provider
with no spend yet this month has no currency of its own. Its budget is read in the
currency the month's other spend is in, and in US dollars when nothing has been
spent at all. A negative amount, or text that is not an amount, is refused with a
message, and the budget in force stays. A file entry that cannot be read is
dropped, and the other budgets in the file are kept.

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

Turning it on reads Claude and GitHub Copilot usage from their organization APIs
as evidence for the productivity figures. Both sources are organization-scoped,
which is why it ships off. Claude needs an API key an organization admin can use,
and GitHub needs organization-owner access. It is marked `DEV`.

The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a
person's choice is kept in `features.json` beside the app's other per-device
choices. It is retired when the import is released and the credentials, not the
switch, decide whether it runs.

## Dashboard

```meta
status: draft
type: feature-flag
key: dashboard
default: on
related: [".devbook/domain/productivity/features.md#personal-productivity-dashboard"]
```

Turning it on lets the reader open, from the app chrome, the full-screen dashboard
of their productivity and what their assistants cost. What its tabs hold and how
its controls narrow them is in
[Six tabs and what each answers](features.md#six-tabs-and-what-each-answers).

It is on by default and marked `DEV`. The key is `DashboardFeatures.Dashboard` in
the Dashboard module. Its string is the one `MonitoringFeatures` used, so a
dashboard someone switched off stays off.

The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a
person's choice is kept in `features.json` beside the app's other per-device
choices. It is retired when the dashboard leaves `DEV`.
