# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/productivity/features.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it.

The scenarios use the default working week: Monday to Friday 09:00 to 17:30, 8.5
hours a day and 42.5 a week. Week 41 runs from Monday 5 to Sunday 11 October
2026. No weekly usage reset is set or detected, so the dashboard's weeks are
calendar weeks.

## Six tabs and what each answers

```meta
type: requirements
status: draft
related: [.devbook/domain/productivity/features.md#six-tabs-and-what-each-answers]
```

> The requirements of the dashboard's tabs. The feature chapter says what each tab
> answers; this says the order they come in and which one the pane opens on.

### Requirement: The tabs come in a fixed order

```meta
type: requirement
status: draft
tests: integration:dotnet:Backlog.Desktop.UI.UnitTests.DashboardPaneTests.The_sections_are_a_tablist_that_opens_on_overview
```

The system SHALL show the dashboard's tabs in the order Overview, Productivity, Tasks, Sessions, Devbook, Cost.

#### Scenario: Opening the dashboard

- **Given** the dashboard is switched on
- **When** the person opens the dashboard
- **Then** the tab strip reads Overview, Productivity, Tasks, Sessions, Devbook and Cost, in that order

### Requirement: The pane opens on the Overview

```meta
type: requirement
status: draft
tests: [integration:dotnet:Backlog.Desktop.UI.UnitTests.DashboardPaneTests.The_sections_are_a_tablist_that_opens_on_overview, integration:dotnet:Backlog.Desktop.UI.UnitTests.DashboardPaneTests.A_pane_opened_again_starts_on_overview]
```

The system SHALL show the Overview tab each time the dashboard pane opens, whichever tab was shown when it last closed.

#### Scenario: The first time the pane opens

- **Given** the dashboard is switched on
- **When** the person opens the dashboard
- **Then** the Overview tab is selected and its panel is shown

#### Scenario: The pane opened again after the Cost tab

- **Given** the person had the Cost tab selected when they closed the dashboard
- **When** they open the dashboard again
- **Then** the Overview tab is selected and the Cost tab is not

## Deltas against the previous window

```meta
type: requirements
status: draft
related: [.devbook/domain/productivity/features.md#deltas-against-the-previous-window]
```

> The requirements of the deltas on the headline figures. The feature chapter says
> which figures carry one and how each is shown; this says what each is compared with
> and when it is left out.

### Requirement: A figure is compared with the window just before it

```meta
type: requirement
status: draft
tests: [integration:dotnet:Backlog.Desktop.UI.UnitTests.DashboardDeltaPartTests.The_merged_pull_requests_tile_says_how_far_it_moved_against_the_previous_window, integration:dotnet:Backlog.Desktop.UI.UnitTests.DashboardDeltaPartTests.The_comparison_names_the_period_the_reader_picked, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.ProductivityInsightsTests.Eight_weeks_is_compared_with_the_eight_before_it, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.SessionInsightsTests.Eight_weeks_is_compared_with_the_eight_before_it]
```

The system SHALL compare a headline figure over a window of N weeks with the same figure over the N weeks just before that window.

#### Scenario: Eight weeks of merged pull requests

- **Given** the person merged 30 pull requests in the 8 weeks up to today and 24 in the 8 weeks before them
- **When** the person picks the 8-week window
- **Then** the pull requests merged tile shows 30, "up 6 vs previous 8 weeks"

### Requirement: No delta without the whole previous window

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Dashboard.UnitTests.SessionInsightsTests.Twenty_six_weeks_has_no_previous_agent_active_time_because_it_was_never_read, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.SessionInsightsTests.Twelve_weeks_has_no_previous_when_the_records_begin_inside_the_twelve_before, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.ProductivityInsightsTests.A_baseline_with_no_block_or_an_incomplete_one_gives_no_previous_figure, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.CostInsightsTests.Copilot_has_no_previous_spend_and_is_not_asked_for_one, integration:dotnet:Backlog.Desktop.UI.UnitTests.DashboardDeltaPartTests.The_merged_pull_requests_tile_draws_no_comparison_when_there_is_no_previous_figure, integration:dotnet:Backlog.Desktop.UI.UnitTests.DashboardDeltaPartTests.The_agent_active_time_tile_compares_as_a_share_and_only_when_there_is_an_earlier_figure, integration:dotnet:Backlog.Desktop.UI.UnitTests.DashboardOverviewTests.Open_drift_reads_on_the_drift_tone_with_no_delta_or_trend]
```

The system SHALL leave out a figure's delta when its source does not hold the figure for the whole previous window.

#### Scenario: Agent-active time over 26 weeks

- **Given** session history reaches back 26 weeks
- **When** the person picks the 26-week window
- **Then** the agent-active time tile shows its figure with no delta

#### Scenario: Session records that begin inside the previous window

- **Given** the earliest activity recorded on any machine is 100 days ago, and the agents were active for 40 hours in the last 12 weeks
- **When** the person picks the 12-week window
- **Then** the agent-active time tile shows 40 hours with no delta, because the 12 weeks before began 168 days ago

#### Scenario: An incomplete count of merged pull requests

- **Given** GitHub could count only part of the pull requests merged in the 12 weeks before the window
- **When** the person picks the 12-week window
- **Then** the pull requests merged tile shows its figure with no delta

#### Scenario: Copilot spend

- **Given** Copilot reports its spend for the whole month at once
- **When** the Overview shows spend this month
- **Then** Copilot's line shows its spend with no delta

#### Scenario: Drift issues open

- **Given** three devbook drift issues are open
- **When** the Overview shows the drift tile
- **Then** the tile shows 3 with no delta

## What needs you

```meta
type: requirements
status: draft
related: [.devbook/domain/productivity/features.md#what-needs-you]
```

> The requirements of Needs you on the Overview. The feature chapter says what each
> rule reads and how an item reads; this says the order, the rework threshold and the
> empty list.

### Requirement: Needs you lists its items in rule order

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Modules.Dashboard.UnitTests.AttentionInsightsTests.The_items_come_in_rule_order_drift_roadmap_budget_rework
```

The system SHALL list the items that need the person in this order: devbook units whose sync failed or whose verdict is conflict, roadmap items behind pace, providers projected past their monthly budget, and rework.

#### Scenario: One item of every kind

- **Given** a devbook unit's last verdict is conflict, a roadmap item is behind pace, Anthropic is projected past its budget, and 5 of 10 reviewed pull requests came back after review
- **When** the Overview shows Needs you
- **Then** it lists the devbook unit first, then the roadmap item, then Anthropic, then the rework

### Requirement: Rework needs you above a quarter of the reviewed pull requests

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Dashboard.UnitTests.AttentionInsightsTests.Rework_needs_you_when_more_than_a_quarter_of_reviewed_pull_requests_came_back_after_review, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.AttentionInsightsTests.Rework_just_over_a_quarter_needs_you, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.AttentionInsightsTests.Rework_of_exactly_a_quarter_does_not_need_you, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.AttentionInsightsTests.Rework_divides_by_reviewed_not_merged_pull_requests, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.AttentionInsightsTests.Rework_over_no_reviewed_pull_requests_does_not_need_you]
```

The system SHALL list rework under Needs you only when more than 25% of the pull requests reviewed in the window came back after review.

#### Scenario: More than a quarter came back

- **Given** 6 of the 20 pull requests reviewed in the window came back after review
- **When** the Overview shows Needs you
- **Then** it lists "6 pull requests came back after review"

#### Scenario: Just over a quarter came back

- **Given** 25 of the 99 pull requests reviewed in the window came back after review
- **When** the Overview shows Needs you
- **Then** it lists the rework

#### Scenario: Exactly a quarter came back

- **Given** 5 of the 20 pull requests reviewed in the window came back after review
- **When** the Overview shows Needs you
- **Then** it does not list the rework

#### Scenario: The share is of reviewed, not merged, pull requests

- **Given** 20 pull requests were merged and 24 reviewed in the window, and 6 came back after review
- **When** the Overview shows Needs you
- **Then** it does not list the rework, because 6 of 24 is exactly a quarter

#### Scenario: No pull request was reviewed

- **Given** 12 pull requests were merged in the window and none was reviewed
- **When** the Overview shows Needs you
- **Then** it does not list the rework

### Requirement: An empty list says nothing needs you

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Dashboard.UnitTests.AttentionInsightsTests.Nothing_needs_you_when_every_source_answers_clean, integration:dotnet:Backlog.Desktop.UI.UnitTests.DashboardOverviewTests.Needs_you_says_nothing_needs_you_when_nothing_qualifies, integration:dotnet:Backlog.Desktop.UI.UnitTests.DashboardOverviewTests.Needs_you_names_the_skipped_sources_under_an_empty_list]
```

The system SHALL show "Nothing needs you." in Needs you when no item qualifies.

#### Scenario: Every source answered and nothing qualifies

- **Given** every source Needs you reads answered, and no rule raised an item
- **When** the Overview shows Needs you
- **Then** it shows "Nothing needs you." and nothing else

#### Scenario: A source could not be read

- **Given** the month-end projection could not be read, and no other rule raised an item
- **When** the Overview shows Needs you
- **Then** it shows "Nothing needs you." followed by the reason the projection could not be read

## Spend against a monthly budget

```meta
type: requirements
status: draft
related: [.devbook/domain/productivity/features.md#spend-against-a-monthly-budget]
```

> The requirements of the month-end projection. The feature chapter says how the Cost
> tab shows it; this says how it is worked out. The scenarios are in August 2026, a
> month of 31 days.

### Requirement: The month-end projection runs on the last seven days

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Dashboard.UnitTests.SpendProjectionTests, integration:dotnet:Backlog.Desktop.UI.UnitTests.DashboardCostPartsTests.A_card_shows_spend_of_budget_with_a_projected_marker_and_says_it_will_pass]
```

The system SHALL project a provider's month-end spend as its spend so far this month plus its average daily spend over the last seven days of the month up to and including today, times the days left after today.

#### Scenario: The first day of the month

- **Given** Anthropic reported 3 on 1 August and nothing before it this month
- **When** the dashboard projects Anthropic's spend on 1 August
- **Then** it projects 93: the 3 so far plus 3 a day for the 30 days left

#### Scenario: Fewer than seven days into the month

- **Given** Anthropic reported 500 on 31 July, and 2, nothing, 4 and 6 on 1 to 4 August
- **When** the dashboard projects Anthropic's spend on 4 August
- **Then** it projects 93: the 12 so far plus an average of 3 a day over the four days for the 27 days left

#### Scenario: Later in the month

- **Given** Anthropic reported 100 on 2 August, 7 on 13 August and 7 on 19 August
- **When** the dashboard projects Anthropic's spend on 19 August
- **Then** it projects 138: the 114 so far plus an average of 2 a day over 13 to 19 August for the 12 days left

#### Scenario: The last day of the month

- **Given** Anthropic reported 10 on 31 August and has a budget of 5
- **When** the dashboard projects Anthropic's spend on 31 August
- **Then** it projects 10, the spend so far, and says Anthropic will pass its budget

## Hours worked

```meta
type: requirements
status: draft
related: [.devbook/domain/productivity/features.md#hours-worked]
```

> The requirements of the dashboard's Hours worked part. The feature chapter
> says what the part shows and why; this says how it splits the actual hours.

### Requirement: Actual hours split at the edges of office hours

```meta
type: requirement
status: draft
```

The system SHALL show for each day in the dashboard's window the person's actual hours split into the time inside that date's office hours and the time outside them, beside the date's planned hours.

#### Scenario: A stretch that runs past the end of the day

- **Given** the person worked one stretch from 16:00 to 19:00 on Thursday 8 October, a day worked from 09:00 to 17:30
- **When** the dashboard shows that day in the Hours worked part
- **Then** it shows 1.5 hours inside office hours and 1.5 hours outside, against 8.5 hours planned

### Requirement: A blocked date counts all its work outside

```meta
type: requirement
status: draft
```

The system SHALL count every actual hour on a date the person blocked as outside office hours.

#### Scenario: Work on a blocked Friday

- **Given** the person blocked Friday 9 October and worked one stretch from 10:00 to 12:00 on it
- **When** the dashboard shows that day in the Hours worked part
- **Then** it shows no time inside office hours and 2 hours outside, against no planned hours

### Requirement: An unblocked date has its weekday's office hours

```meta
type: requirement
status: draft
```

The system SHALL take as the office hours of a date the person unblocked the start and end its weekday stores in the weekly pattern.

#### Scenario: Work on an unblocked Saturday

- **Given** Saturday stores 10:00 to 14:00, the person unblocked Saturday 10 October, and worked one stretch from 13:00 to 15:00 on it
- **When** the dashboard shows that day in the Hours worked part
- **Then** it shows 1 hour inside office hours and 1 hour outside, against 4 hours planned

### Requirement: A week sums its days

```meta
type: requirement
status: draft
```

The system SHALL show for each week in the dashboard's window the sum of its days' time inside office hours, time outside them, and planned hours.

#### Scenario: A week with a late day and a blocked day

- **Given** in week 41 the person worked from 16:00 to 19:00 on Thursday 8 October and from 10:00 to 12:00 on Friday 9 October, which they blocked, and worked no other stretch
- **When** the dashboard shows week 41 in the Hours worked part
- **Then** it shows 1.5 hours inside office hours and 3.5 hours outside, against 34 hours planned

## Asking AI about a part

```meta
type: requirements
status: draft
related: [.devbook/domain/productivity/features.md#asking-ai-about-a-part]
```

> The requirements of a dashboard part's Ask AI control. The feature chapter says
> what the assistant reads; this says what the control begins and when it is shown.

### Requirement: A part's Ask AI begins a question naming the part

```meta
type: requirement
status: draft
tests: [integration:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.A_dashboard_parts_ask_ai_opens_the_panel_on_the_dashboard_with_a_question_naming_the_part, integration:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.A_dashboard_parts_ask_ai_clears_an_earlier_answer, integration:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.A_dashboard_parts_ask_ai_ends_an_ask_in_flight, integration:dotnet:Backlog.Desktop.UI.UnitTests.DashboardPartFrameTests]
```

The system SHALL open the assistant on the Dashboard with the question begun `About the "<part>" chart: ` when the person presses a dashboard part's Ask AI.

#### Scenario: Asking about the Throughput part

- **Given** the assistant is switched on and the dashboard is open
- **When** the person presses Ask AI on the Throughput part
- **Then** the assistant opens on the Dashboard with the question `About the "Throughput" chart: ` for the person to finish

#### Scenario: An answer from an earlier question is on screen

- **Given** the assistant shows an answer to an earlier question
- **When** the person presses Ask AI on the Throughput part
- **Then** the earlier answer is cleared and the question begins `About the "Throughput" chart: `

### Requirement: No part offers Ask AI while the assistant is off

```meta
type: requirement
status: draft
tests: [integration:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.No_dashboard_part_offers_ask_ai_while_the_assistant_is_off, integration:dotnet:Backlog.Desktop.UI.UnitTests.DashboardPartFrameTests.Without_a_handler_cascaded_the_frame_draws_no_ask_ai]
```

The system SHALL show no Ask AI control on any dashboard part while the AI assistant is switched off.

#### Scenario: The assistant is switched off

- **Given** the AI assistant is switched off
- **When** the person opens the dashboard
- **Then** no part shows an Ask AI control
