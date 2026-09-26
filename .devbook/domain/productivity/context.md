# Productivity

```meta
status: draft
type: context
```

Productivity turns AI-assisted work activity into personal insight for the one
person using it, and owns none of the work it measures.

Inside the boundary: the productivity ledger and the summaries read off it, and
the per-device choices that shape how a week is read — the reader's own working
week, and when their weekly assistant allowance resets.

Outside it: task work, repository state, and completion decisions, which
[Tasks](../tasks/domain.md#task) and
[Repository Management](../repository-management/domain.md#repository-registry)
answer; agent session facts, which
[Sessions](../sessions/domain.md#session-log) answers. The model itself is in
[domain.md](domain.md), which stays this context's root document until the
devbook contract v6 layout lands.

## Working week

```meta
status: draft
type: setting
key: working-hours.json
scope: user
default: Monday to Friday 09:00-17:30; Saturday and Sunday not worked
related: [.devbook/domain/productivity/features.md#personal-productivity-dashboard]
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.WorkingHoursSettingsStoreTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsWorkingHoursTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.DashboardPaneTests.The_grid_outlines_the_hours_of_the_working_week]
```

Which hours the reader meant to be working, as seven independent days — each one
worked or not, and each with its own start and end. Seven days rather than one
range plus a set of working days, because a reader who starts at seven on
Fridays cannot say so with a single range.

What it does is presentation, and deliberately only that: the dashboard's hour
grids outline the hours the week claims, and nothing else changes. No figure is
recomputed and no work is excluded — agent-active time at eleven at night counts
exactly as much as time at eleven in the morning, and a grid that quietly
dropped out-of-hours work would answer a different question from the one its
tile is named for. What the outline buys the reader is the ability to see the
difference themselves.

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

`scope: user` is the nearest rung the vocabulary offers: the choice is one
person's, but it is stored per device and nothing syncs it, so the same reader
on a second machine starts again from the default.

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
spent in. Like the working week it changes no figure — only where the boundaries
fall.

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
week back to detection. Stored per device beside the working week, with the same
`scope: user` caveat.

## usage-metrics

```meta
status: draft
type: feature-flag
key: usage-metrics
related: [".devbook/domain/productivity/features.md#ai-vendor-usage-import"]
```

Decided at release, from configuration. Name the switch in business language, and say who owns the rollout, what turning it on changes, and when the flag is retired.

## dashboard

```meta
status: draft
type: feature-flag
key: dashboard
related: [".devbook/domain/productivity/features.md#personal-productivity-dashboard"]
```

Decided at release, from configuration. Name the switch in business language, and say who owns the rollout, what turning it on changes, and when the flag is retired.
