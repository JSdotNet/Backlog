# Insights

```meta
type: context
related: [.devbook/arc42/adr/0073-the-bounded-contexts-are-redrawn.md#2-insights]
```

Insights turns the work the person and their agents do into personal insight for
that one person, on the [dashboard](features.md#personal-productivity-dashboard),
and owns none of the work it reads. Monitoring & Dashboard merged into it under
[local ADR 0073 §2](../../arc42/adr/0073-the-bounded-contexts-are-redrawn.md#2-insights);
its code is still `Backlog.Modules.Dashboard` until that module is renamed
`Backlog.Modules.Insights`.

Inside the boundary: the productivity ledger and the summaries read off it, the
dashboard that reads across the other contexts and points at what needs the person,
and the choices that shape how a week is read: the weekly reset of the assistant's
allowance and the monthly spend budget for each assistant, which stay on each device.
The hour grids outline the reader's [working week](../work/context.md#working-week),
which [Work](../work/context.md) owns with the rest of capacity (local ADR 0073 §1).

Outside it: task and plan work, repository state and completion decisions, which [Work](../work/context.md) and [Repositories](../repositories/domain.md#repository-registry) answer; agent session facts, which [Sessions](../sessions/domain.md#session-log) answers; drift verdicts, which [Devbook](../devbook/domain.md#sync-verdict) answers. Insights writes into none of them: an item on the dashboard only points at the tab that holds its evidence. Team monitoring and scanning across repositories are not part of it (local ADR 0073 §2). The model is in [domain.md](domain.md).

## Weekly usage reset

```meta
type: setting
key: usage-reset.json
scope: user
default: unset
related: [.devbook/domain/insights/features.md#personal-productivity-dashboard]
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.UsageResetSettingsStoreTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.DashboardPaneTests.Under_a_usage_reset_the_grid_says_it_is_a_calendar_week_beside_reset_columns, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.SessionInsightsTests.A_grid_is_a_calendar_week_and_names_the_usage_week_beginning_in_it]
```

When the assistant's weekly usage allowance starts again: a day of the week and a time, recurring every seven days.

It decides where the dashboard cuts a week. The columns of the Sessions tab's weekly charts run from one reset to the next rather than from a calendar Monday. A column is the week the allowance is spent in. It changes no figure, only where the boundaries fall.

The hour grid beside those charts does not follow the reset. It is a calendar week, Monday to Sunday, so its seven rows are whole days and its "week N" title names the week the reader knows. Pressing a column opens the grid on the calendar week that usage week begins in, and the grid's caption says the columns run from the reset.

The time is on this machine's clock, not UTC. The reader copies the reset off the assistant's own usage screen ("Resets Mon 2:00 PM") and should not have to convert it.

Three cuts can decide where a week starts. The first that applies wins:

| Rank | Cut | Applies when | Where the weeks are anchored |
| --- | --- | --- | --- |
| 1 | Configured | The setting is set | The most recent occurrence at or before now, resolved through the zone's offset |
| 2 | Detected | The setting is unset, which is how it ships, or its file does not parse | The reset reported by the most recent seven-day limit the assistant refused a request on |
| 3 | Calendar | Neither of the above | Plain calendar weeks |

Resolving through the zone's offset means a reset falling in a daylight-saving gap or overlap still names one instant. A configured reset outranks a detected one on purpose, because the usage records on a machine may come from an older plan or month. The surface says which of the three cuts won.

The person sets and clears it on the settings screen. Clearing removes the file and hands the week back to detection. It is stored per device beside the working week. Unlike the working week, nothing syncs it, so `scope: user` is only the nearest rung the vocabulary offers. The same reader on a second machine starts again unset.

## Monthly spend budget

```meta
type: setting
key: spend-budgets.json
scope: user
default: no budget for any provider
related: [.devbook/domain/insights/features.md#spend-against-a-monthly-budget, .devbook/domain/insights/features.md#what-needs-you, .devbook/domain/insights/features.md#personal-productivity-dashboard]
```

How much the reader means to spend on each assistant in a calendar month, one amount for each [Spend Provider](domain.md#spend-provider). The providers' figures differ in kind and may be in different currencies, so each amount stands alone: one budget across all of them would compare unlike amounts.

The dashboard sets each provider's spend so far against its budget and draws the budget line on the spend chart. A provider projected past its budget by month end is listed under [What needs you](features.md#what-needs-you). [Spend against a monthly budget](features.md#spend-against-a-monthly-budget) describes the projection.

Per value:

- **An amount** sets that provider's budget for every month until the reader changes it.
- **Empty**, which is how it ships, means that provider has no budget. Its card shows the spend without a budget, and it never appears in What needs you.

An amount is a bare number with no currency. It is read in the currency that provider reports its spend in, because Backlog holds no exchange rate. A provider with no spend yet this month has no currency of its own: its budget is read in the currency of the month's other spend, and in US dollars when nothing has been spent. A negative amount, or text that is not an amount, is refused with a message, and the budget in force stays. A file entry that cannot be read is dropped, and the other budgets are kept.

The person sets it in Dashboard settings. It is stored per device beside the weekly usage reset, and clearing the last budget removes the file. Like the reset, nothing syncs it, so `scope: user` is the nearest rung the vocabulary offers. The same reader on a second machine starts with no budgets.

## AI usage metrics

```meta
type: feature-flag
key: usage-metrics
default: off
related: [".devbook/domain/insights/features.md#ai-vendor-usage-import"]
```

With the `usage-metrics` flag on, Claude and GitHub Copilot usage is read from their organization APIs as evidence for the productivity figures. Both sources are organization-scoped, which is why it ships off: Claude needs an API key an organization admin can use, and GitHub needs organization-owner access. The flag is marked `DEV`.

The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a person's choice is kept in `features.json` beside the app's other per-device choices. It is retired when the import is released and the credentials, not the switch, decide whether it runs.
