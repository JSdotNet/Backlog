# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it.

The requirements of the Goals view, the stand-up, pull request status and syncing name
the tests that prove them. Syncing is proved between two desktops through the real sync
service; none is proved end to end in the app yet. The scenarios use the goal
"Customers get paid faster", due Friday 30 October 2026, with the plan `+bank-integration`
attached. Today is Monday 19 October 2026 unless a scenario says otherwise, and the
person reads by Week, so the last window is 12 to 16 October and the next is 19 to
23 October.

## The Goals view

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#the-goals-view]
```

### Requirement: A goal lands from the done rate of the last four iterations

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalForecastingTests.Two_a_week_with_six_open_lands_at_the_end_of_the_third_week_from_now, unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalForecastingTests.Month_reads_the_done_rate_from_June_to_September, unit:dotnet:Backlog.UI.Components.UnitTests.GoalsTextTests.A_goal_line_reads_done_over_total_plus_the_window_due_and_landing]
```

The system SHALL forecast a goal's landing date from the average number of its items done in each of the last four iterations of the chosen length.

#### Scenario: Two items a week, six open

- **Given** the goal had 1, 3, 2 and 2 items done in the four weeks up to 16 October, and has 6 open items
- **When** the Goals view is read
- **Then** the goal lands at the end of the third week from now, Friday 6 November

#### Scenario: A longer iteration reads a longer history

- **Given** the person chose Month
- **When** the Goals view is read
- **Then** the done rate is read from June, July, August and September

### Requirement: A goal landing after its due date is At risk

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalForecastingTests.Landing_after_the_due_date_is_at_risk, unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalForecastingTests.Landing_on_the_due_date_is_on_track, unit:dotnet:Backlog.UI.Components.UnitTests.GoalsBoardComponentsTests.A_goal_card_reads_its_line_and_forecast_and_its_bar_in_words]
```

The system SHALL show a goal as At risk when its landing date falls after its due date.

#### Scenario: Landing a week late

- **Given** the goal is due 30 October and lands 6 November, and no open impediment holds it back
- **When** the Goals view is read
- **Then** the goal reads "At risk" and "lands ~6 Nov"

#### Scenario: Landing in time

- **Given** the goal "Devbook search ships" is due 23 October and lands 23 October, and no open impediment holds it back
- **When** the Goals view is read
- **Then** the goal reads "On track"

### Requirement: An open impediment puts its goal at risk

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalForecastingTests.An_open_impediment_puts_a_goal_landing_in_time_at_risk, unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalForecastingTests.Without_an_open_impediment_a_goal_landing_in_time_is_on_track, unit:dotnet:Backlog.Modules.Progress.UnitTests.StandUpTests.A_goal_landing_in_time_with_only_a_resolved_impediment_is_on_track]
```

The system SHALL show a goal as At risk while an impediment that is not Resolved holds back one of the items it counts, whatever its landing date.

#### Scenario: An impediment on a plan's entry

- **Given** the goal lands before its due date, and the impediment "Bank sample files missing" is Escalated and holds back "Bank import mapping", an entry of `+bank-integration`
- **When** the Goals view is read
- **Then** the goal reads "At risk"

#### Scenario: The impediment is resolved

- **Given** the goal lands before its due date, and "Bank sample files missing" is Resolved
- **When** the Goals view is read
- **Then** the goal reads "On track"

### Requirement: A goal with no recent done rate and open items is At risk

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.GoalForecastingTests.Nothing_done_in_four_iterations_with_open_items_is_at_risk_with_no_landing_date, unit:dotnet:Backlog.UI.Components.UnitTests.GoalsTextTests.A_goal_line_drops_plus_nought_and_a_missing_landing]
```

The system SHALL show a goal that had no item done in the last four iterations and still has open items as At risk, with no landing date.

#### Scenario: Nothing done for a month

- **Given** the goal "Phone capture in daily use" had no item done in the four weeks up to 16 October and has 7 open items
- **When** the Goals view is read
- **Then** the goal reads "At risk" and shows no landing date

### Requirement: Work in progress that no goal holds is listed under No goal

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.GoalsHoldingTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.GoalsHostTests.Work_in_progress_no_goal_holds_is_listed_under_no_goal, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.The_goals_view_draws_the_board_over_the_shells_tasks]
```

The system SHALL list under No goal each task in progress and each open pull request in the repository scope that no goal holds, each with Attach.

#### Scenario: A task in progress outside every goal

- **Given** the task "VAT rate per line item" in `backlog` is in progress and no goal holds it, and the header's scope is `backlog`
- **When** the Goals view is read
- **Then** "VAT rate per line item" is listed under No goal with "Attach…"

## Setting a goal

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#setting-a-goal]
```

### Requirement: A goal needs a title and a due date

```meta
type: requirement
status: draft
```

The system SHALL refuse to save a goal without a title or without a due date.

#### Scenario: No due date

- **Given** the person opened New goal
- **When** they give the title "Customers get paid faster" and no due date, and save
- **Then** the goal is not saved and the due date field says it is needed

### Requirement: Deleting a goal leaves its work unchanged

```meta
type: requirement
status: draft
```

The system SHALL, once the person confirms deleting a goal, remove the goal and its attachments and change no plan, task or pull request it held.

#### Scenario: Deleting a goal with a plan attached

- **Given** the goal holds `+bank-integration` and the task "Credit notes"
- **When** the person deletes the goal and confirms
- **Then** the goal is gone, every task of `+bank-integration` and "Credit notes" are unchanged, and "Credit notes", while in progress, shows under No goal

## Attaching work to a goal

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#attaching-work-to-a-goal]
```

### Requirement: An attached plan counts each of its entries

```meta
type: requirement
status: draft
```

The system SHALL count an attached plan as one item per entry filed under its tag, finished or not, including entries filed after it was attached.

#### Scenario: A plan of nine entries

- **Given** `+bank-integration` has 9 entries, 5 of them done, and the goal holds nothing else
- **When** the goal is read
- **Then** its progress is 5/9

#### Scenario: An entry filed later

- **Given** the goal holds `+bank-integration` at 5/9
- **When** a new task is filed under `+bank-integration`
- **Then** the goal's progress reads 5/10

### Requirement: An attached task counts once with its pull requests

```meta
type: requirement
status: draft
```

The system SHALL count an attached task as one item, and SHALL not count again the pull requests the task links.

#### Scenario: A task with two pull requests

- **Given** the goal holds only the task "Invoice PDF export", which links #1338 and #1340
- **When** the goal is read
- **Then** its total is 1

#### Scenario: A pull request attached beside its task

- **Given** the goal holds the task "Invoice PDF export", which links #1338, and holds #1338 as well
- **When** the goal is read
- **Then** its total is 1

### Requirement: An attached pull request counts once

```meta
type: requirement
status: draft
```

The system SHALL count an attached pull request that no attached task links as one item, done once it is merged.

#### Scenario: A merged pull request

- **Given** the goal holds only #1352, which is merged
- **When** the goal is read
- **Then** its progress is 1/1

### Requirement: A task in an attached plan counts once

```meta
type: requirement
status: draft
```

The system SHALL count a task that the goal holds both directly and as an entry of an attached plan as one item.

#### Scenario: Attached twice over

- **Given** the goal holds `+bank-integration`, 9 entries, and also holds its entry "Customer search by IBAN" as a task
- **When** the goal is read
- **Then** its total is 9

### Requirement: An item belongs to at most one goal

```meta
type: requirement
status: draft
```

The system SHALL NOT attach a plan, task or pull request that another goal holds without asking: it SHALL name that goal, ask whether to move the item, and on yes move it, so the item is never in two goals.

#### Scenario: A task another goal holds

- **Given** the goal "Devbook search ships" holds the task "Highlight matches"
- **When** the person attaches "Highlight matches" to "Customers get paid faster"
- **Then** the view says "Already in Devbook search ships" and asks whether to move it, and until the person answers the task stays in that goal

#### Scenario: Moving a task to another goal

- **Given** the view asks whether to move "Highlight matches" from "Devbook search ships"
- **When** the person answers yes
- **Then** "Customers get paid faster" holds "Highlight matches", and "Devbook search ships" no longer does

#### Scenario: Declining the move

- **Given** the view asks whether to move "Highlight matches" from "Devbook search ships"
- **When** the person answers no
- **Then** "Highlight matches" stays in "Devbook search ships", and "Customers get paid faster" holds nothing new

#### Scenario: An entry another goal holds directly

- **Given** "Devbook search ships" holds the task "Bank import mapping" directly, and "Customers get paid faster" holds `+bank-integration`, which files it
- **When** both goals are read
- **Then** "Bank import mapping" counts in "Devbook search ships" only

### Requirement: Attaching changes nothing on the work

```meta
type: requirement
status: draft
```

The system SHALL leave a plan, task or pull request unchanged when it is attached to a goal or detached from one.

#### Scenario: Detaching a task

- **Given** the goal holds the task "Credit notes", which is in progress
- **When** the person detaches it
- **Then** "Credit notes" is still in progress with the same tags and linked pull requests

## Choosing the iteration

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#choosing-the-iteration]
```

### Requirement: A Day window is one working day

```meta
type: requirement
status: draft
```

The system SHALL, with Day chosen, read the next window as today when today is a working day, and the last window as the working day before it.

#### Scenario: A Monday stand-up

- **Given** today is Monday 19 October and the person works Monday to Friday
- **When** the person chooses Day
- **Then** the last window is Friday 16 October and the next is Monday 19 October

### Requirement: A Week window runs Monday to Friday

```meta
type: requirement
status: draft
```

The system SHALL, with Week chosen, read each window as Monday to Friday, and on a day outside every window take the next window as the first one after today.

#### Scenario: Read on a Saturday

- **Given** today is Saturday 17 October
- **When** the person chooses Week
- **Then** the header reads "12–16 Oct → 19–23 Oct"

### Requirement: Two-week windows keep to the start date

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.GoalsHostTests.Choosing_2_weeks_the_first_time_asks_for_the_start_offering_this_weeks_monday_and_keeps_it_before_the_length, unit:dotnet:Backlog.Desktop.UI.UnitTests.GoalsHostTests.Cancelling_the_two_week_start_keeps_the_iteration_as_it_was, unit:dotnet:Backlog.Desktop.UI.UnitTests.GoalsHostTests.With_a_two_week_start_kept_2_weeks_switches_without_asking, unit:dotnet:Backlog.UI.Components.UnitTests.GoalsBoardComponentsTests.The_two_week_start_dialog_opens_on_the_offered_day_and_hands_back_the_one_given, unit:dotnet:Backlog.UI.Components.UnitTests.GoalsBoardComponentsTests.The_two_week_start_dialog_refuses_a_blank_day_and_keeps_nothing]
```

The system SHALL, with 2 weeks chosen, read each window as fourteen days starting on the two-week start or a whole number of fourteen days from it.

#### Scenario: Anchored on 5 October

- **Given** the two-week start is Monday 5 October and today is Monday 19 October
- **When** the person chooses 2 weeks
- **Then** the last window is 5 to 18 October and the next is 19 October to 1 November

#### Scenario: Choosing 2 weeks for the first time

- **Given** the person has never chosen 2 weeks
- **When** they choose it
- **Then** they are asked for the start date, with Monday 19 October offered

### Requirement: A Month window is a calendar month

```meta
type: requirement
status: draft
```

The system SHALL, with Month chosen, read each window as one calendar month.

#### Scenario: Read in October

- **Given** today is Monday 19 October
- **When** the person chooses Month
- **Then** the last window is September and the next is October

### Requirement: The iteration length is the person's own

```meta
type: requirement
status: draft
```

The system SHALL keep the chosen iteration length as the person's setting and open the Goals view and the stand-up on it.

#### Scenario: Reopening the app

- **Given** the person chose 2 weeks
- **When** they reopen the app
- **Then** the Goals view reads by 2 weeks

## The stand-up

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#the-stand-up]
```

### Requirement: The stand-up has four blocks

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.GoalsBoardComponentsTests.The_goals_heading_carries_the_count_of_goals, unit:dotnet:Backlog.UI.Components.UnitTests.GoalsBoardComponentsTests.The_stand_up_heads_each_block_with_its_count_and_tags_planned_with_points]
```

The system SHALL show the stand-up as four blocks in this order: Goals, Finished, Planned and Impediments, each heading with its count.

#### Scenario: A week's stand-up

- **Given** three goals, seven items done from 12 to 16 October, five planned items and three open impediments
- **When** the stand-up is read
- **Then** it shows Goals, Finished 7, Planned 5 and Impediments 3, in that order

### Requirement: Finished is grouped by goal, then No goal

```meta
type: requirement
status: draft
```

The system SHALL list under Finished every item done in the last window, grouped by the goal that counts it, with the items no goal counts last under "No goal".

#### Scenario: A task done outside every goal

- **Given** "Invoice PDF export" of "Customers get paid faster" and "VAT rate per line item" of no goal were both done on 14 October
- **When** the stand-up is read
- **Then** "Invoice PDF export" is under "Customers get paid faster" and "VAT rate per line item" is under "No goal", last

### Requirement: Planned is derived from three sources

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.StandUpTests.Planned_is_derived_from_three_sources, unit:dotnet:Backlog.Modules.Progress.UnitTests.StandUpTests.An_item_from_two_sources_is_planned_once]
```

The system SHALL list under Planned, each once, the items goals hold that are in progress, the items due inside the next window, and for each attached plan its first ready entry in plan order that is not in progress.

#### Scenario: All three sources

- **Given** "Customer search by IBAN" of the goal is in progress, "Recurring invoices" is due 22 October, and "Payment matching rules" is the first ready entry of `+bank-integration` not in progress
- **When** the stand-up is read
- **Then** all three are under Planned, under "Customers get paid faster"

#### Scenario: An item from two sources

- **Given** "Customer search by IBAN" is in progress and due 21 October
- **When** the stand-up is read
- **Then** it is listed under Planned once

### Requirement: The points tag compares planned effort with the scaled pace

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.StandUpTests.The_points_tag_adds_up_planned_effort_beside_the_weekly_pace, unit:dotnet:Backlog.Modules.Progress.UnitTests.StandUpTests.The_points_tag_scales_the_pace_to_a_day, unit:dotnet:Backlog.UI.Components.UnitTests.GoalsTextTests.The_points_tag_compares_planned_with_capacity, unit:dotnet:Backlog.UI.Components.UnitTests.GoalsBoardComponentsTests.A_planned_item_shows_its_effort_in_points_and_one_without_shows_none, unit:dotnet:Backlog.Desktop.UI.UnitTests.GoalsHostTests.A_planned_items_effort_reaches_the_stand_up_in_points]
```

The system SHALL tag Planned with the planned items' `effort:` added up, then the person's pace from the Roadmap scaled to the next window's working days, as `planned / capacity pts`.

#### Scenario: A week at 18 points

- **Given** the planned items carry 5, 8, 3 and 5 points and one carries none, and the pace is 18 points a week
- **When** the stand-up is read by Week
- **Then** Planned is tagged `21 / 18 pts`

#### Scenario: A day at 18 points a week

- **Given** the planned items carry 5 points, and the pace is 18 points a five-day week
- **When** the stand-up is read by Day
- **Then** Planned is tagged `5 / 4 pts`

### Requirement: Impediments lists the open ones and counts the resolved

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Progress.UnitTests.StandUpTests.Read_by_Day_impediments_list_the_open_ones_with_who_age_and_goal_and_count_the_resolved, unit:dotnet:Backlog.UI.Components.UnitTests.GoalsTextTests.An_impediment_with_nobody_named_reads_not_reported_yet, unit:dotnet:Backlog.UI.Components.UnitTests.GoalsBoardComponentsTests.An_impediment_row_names_who_it_is_with_its_age_and_its_goal_in_the_error_ink]
```

The system SHALL list under Impediments every impediment that is not Resolved, with its state, who it is with, its age in working days and the goal it holds back, followed by how many were resolved in the last window.

#### Scenario: One escalated, one resolved

- **Given** "Bank sample files missing" was raised on Wednesday 14 October and is Escalated to the account manager, and one other impediment was resolved on 15 October
- **When** the stand-up is read by Day on Friday 16 October, so the last window is Thursday 15 October
- **Then** it lists "Bank sample files missing", Escalated, "Account manager · 3 days · Customers get paid faster", and reads "1 resolved"

#### Scenario: A new impediment

- **Given** "Credit note numbering unclear" is New
- **When** the stand-up is read
- **Then** its line reads "Not reported yet"

### Requirement: Copy for stand-up copies the four blocks

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.StandUpClipboardTests]
```

The system SHALL put the four blocks on the clipboard on the desktop as plain text, each heading with its count.

#### Scenario: Pasting into Teams

- **Given** the stand-up is read on the desktop
- **When** the person chooses Copy for stand-up and pastes into a Teams message
- **Then** the message holds the four blocks as plain text, with their counts

## Tracking an impediment

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#tracking-an-impediment]
```

### Requirement: An impediment moves forward along its states

```meta
type: requirement
status: draft
```

The system SHALL let an impediment move from New to Reported, from Reported to Escalated or Being handled, and from Escalated to Being handled, and SHALL refuse every other move except to Resolved.

#### Scenario: Skipping Escalated

- **Given** "Android build runner flaky" is Reported to the platform team
- **When** the person moves it to Being handled
- **Then** it is Being handled, and its history reads New, Reported, Being handled

#### Scenario: Moving back

- **Given** "Bank sample files missing" is Escalated
- **When** the person moves it to Reported
- **Then** the move is refused and it stays Escalated

### Requirement: Any open impediment can be resolved

```meta
type: requirement
status: draft
```

The system SHALL let an impediment in any state but Resolved move to Resolved.

#### Scenario: Resolved while New

- **Given** "Credit note numbering unclear" is New
- **When** the person resolves it
- **Then** it is Resolved, and its history reads New, Resolved

#### Scenario: A resolved impediment stays resolved

- **Given** "Credit note numbering unclear" is Resolved
- **When** the person tries to move it to Reported
- **Then** the move is refused

### Requirement: Every move records its day and who it is with

```meta
type: requirement
status: draft
```

The system SHALL record with each move the day it was made and who the impediment is with at that step.

#### Scenario: Escalating on a Friday

- **Given** "Bank sample files missing" was New on 14 October and Reported to bank support on 15 October
- **When** the person escalates it to the account manager on Friday 16 October
- **Then** its history reads New Wed 14, Reported · bank support Thu 15, Escalated · account manager Fri 16

### Requirement: A blocked task raises one New impediment

```meta
type: requirement
status: draft
tests: [integration:dotnet:Backlog.HostComposition.UnitTests.BlockedTaskRaisesImpedimentTests.Set_blocked_raises_one_New_impediment_and_unblocking_leaves_it, integration:dotnet:Backlog.HostComposition.UnitTests.BlockedTaskRaisesImpedimentTests.A_blocked_token_in_the_entry_text_raises_one_too]
```

When a task is marked blocked, by its `blocked:` token or a `set_blocked` call, the system SHALL raise a New impediment titled with the task's title that holds the task back, unless an impediment already holds it back that is not Resolved, or that was raised on or after the day the task was marked blocked.

#### Scenario: Marking a task blocked

- **Given** the task "Bank import mapping" is held back by no impediment
- **When** an agent calls `set_blocked` on it
- **Then** a New impediment "Bank import mapping" holds it back

#### Scenario: Already held back

- **Given** "Bank sample files missing", Escalated, holds back "Bank import mapping"
- **When** the person adds a `blocked:` token to "Bank import mapping"
- **Then** no impediment is raised

#### Scenario: Held back only by a resolved impediment

- **Given** the only impediment that held back "Bank import mapping" is Resolved
- **When** the task is marked blocked again
- **Then** a New impediment "Bank import mapping" holds it back

#### Scenario: Resolved while the task is still blocked

- **Given** "Bank import mapping" was marked blocked on 15 October, and the impediment that raised is Resolved on 16 October
- **When** the task is written again with its mark unchanged
- **Then** no impediment is raised

### Requirement: Unblocking a task leaves its impediment

```meta
type: requirement
status: draft
```

The system SHALL leave an impediment's state unchanged when a task it holds back is no longer marked blocked.

#### Scenario: The token is removed

- **Given** "Bank sample files missing", Being handled, holds back "Bank import mapping", which is marked blocked
- **When** the `blocked:` token is removed from the task
- **Then** "Bank sample files missing" is still Being handled

## Pull request status

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#pull-request-status]
```

### Requirement: A pull request row shows its checks as passed over total

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.PullRequestStatusModelTests.Every_check_passed_reads_passed_and_14_of_14, unit:dotnet:Backlog.UI.Components.UnitTests.PullRequestStatusModelTests.Checks_still_running_read_running_and_9_of_14, unit:dotnet:Backlog.UI.Components.UnitTests.PullRequestStatusModelTests.One_failed_check_reads_failing_even_while_others_run, unit:dotnet:Backlog.UI.Components.UnitTests.PullRequestStatusTests.A_running_or_failing_check_draws_its_own_glyph]
```

The system SHALL show on every open pull request row in Progress an icon and its checks as `passed/total`, in a running variant while a check runs and a failing variant once one fails.

#### Scenario: Every check passed

- **Given** #1341 has 14 checks, all passed
- **When** its row is drawn
- **Then** it shows the passed icon and `14/14`

#### Scenario: Checks still running

- **Given** #1358 has 14 checks, 9 passed and 5 running
- **When** its row is drawn
- **Then** it shows the running icon and `9/14`

#### Scenario: A failing check

- **Given** #1344 has 14 checks, 2 passed and one failed
- **When** its row is drawn
- **Then** it shows the failing icon and `2/14`

### Requirement: A pull request row shows approvals over required

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.PullRequestStatusModelTests.One_of_two_approvals_is_short, unit:dotnet:Backlog.UI.Components.UnitTests.PullRequestStatusModelTests.No_required_total_shows_the_received_count_alone, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubClientLinkedPullRequestStatusTests.A_ruleset_pull_request_rule_gives_its_count, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubClientLinkedPullRequestStatusTests.The_larger_of_the_protection_rule_and_every_ruleset_rule_is_required, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubClientLinkedPullRequestStatusTests.A_base_branch_with_no_rule_has_no_required_total]
```

The system SHALL show on every open pull request row in Progress an icon and its approvals as `approvals/required`, reading required as the larger of the base branch's protection rule and its rulesets' pull request rules, and SHALL read no required total for a base branch no rule asks an approval of.

#### Scenario: One of two approvals

- **Given** #1341 has one approval and its base branch `main` requires two
- **When** its row is drawn
- **Then** it shows the approvals icon and `1/2`

#### Scenario: A ruleset asks for the approval

- **Given** #1350's base branch has no protection rule and a ruleset whose pull request rule requires one approval
- **When** its row is drawn
- **Then** required is 1

#### Scenario: No rule asks for an approval

- **Given** #1358's base branch `main` has a ruleset that requires status checks and no review
- **When** its status is read
- **Then** it has no required total, and its approvals still count the ones received

### Requirement: Checks and approvals are named for screen readers only

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.PullRequestStatusTests.The_visible_text_holds_counts_and_a_screen_reader_hears_the_words]
```

The system SHALL name the checks and approvals marks with the words "checks" and "approvals" in their accessible labels and not in their visible text.

#### Scenario: Read by a screen reader

- **Given** #1341's row shows `14/14` and `1/2`
- **When** a screen reader reads the row
- **Then** it hears "14 of 14 checks passed" and "1 of 2 approvals", and the visible text holds neither word

## Goals on the phone

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#goals-on-the-phone]
```

### Requirement: The phone reads goals, the stand-up and impediments

```meta
type: requirement
status: draft
```

The system SHALL show on the phone the goals, the four stand-up blocks and the impediments that the paired desktop has synced.

#### Scenario: Opening the Stand-up tab

- **Given** the desktop synced three goals and three open impediments
- **When** the person opens Stand-up on the phone
- **Then** it shows the three goals, Finished, Planned and the three impediments

### Requirement: The phone changes only an impediment's state, check-again date and note

```meta
type: requirement
status: draft
```

The system SHALL let the phone change an impediment's state, check-again date and note, through the device outbox, and nothing else in Progress.

#### Scenario: Moving an impediment on the phone

- **Given** "Bank sample files missing" is Escalated, and the phone is offline
- **When** the person moves it to Being handled and sets check again to Tuesday 20 October
- **Then** both changes wait in the device outbox and reach the desktop once the phone is back online

#### Scenario: A desktop change made since stands

- **Given** the phone last synced "Bank sample files missing" with no note
- **When** the person writes a note on the desktop, then a different note on the phone, and both sync
- **Then** both devices show the desktop's note

#### Scenario: A move the desktop did not apply gives way

- **Given** "Bank sample files missing" is Escalated, and the phone's move to Being handled has left the phone
- **When** the next copy the phone pulls is still Escalated
- **Then** the phone shows it Escalated

#### Scenario: Only the moves its state allows are offered

- **Given** "Credit note numbering unclear" is New
- **When** the person opens its state list on the phone
- **Then** only Reported and Resolved can be chosen

#### Scenario: No goal editing on the phone

- **Given** the goal "Customers get paid faster" is open on the phone
- **When** the person looks for a way to edit it, attach work to it or copy the stand-up
- **Then** the phone offers none

## Syncing goals and impediments

```meta
type: requirements
status: draft
related: [.devbook/domain/progress/features.md#syncing-goals-and-impediments]
```

### Requirement: Goals and impediments travel as one document each

```meta
type: requirement
status: draft
tests: [integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.GoalSyncBetweenDevicesTests.A_goal_set_on_one_desktop_appears_on_the_other, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.ImpedimentSyncBetweenDevicesTests.An_impediment_raised_on_one_desktop_appears_on_the_other, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.ImpedimentSyncBetweenDevicesTests.A_sprint_series_on_the_replica_is_still_dropped_beside_an_impediment]
```

The system SHALL carry each goal as one `goal` document and each impediment as one `impediment` document on the task feed, under its own id, while sync is on and the device is paired.

#### Scenario: A goal reaches the other desktop

- **Given** two paired desktops with sync on
- **When** the person sets the goal "Customers get paid faster" on the first
- **Then** the second shows the goal after its next pull, without a Refresh

#### Scenario: An impediment reaches the other desktop

- **Given** two paired desktops with sync on
- **When** the person raises "Bank sample files missing" on the first, holding back one task
- **Then** the second shows it New, with its note and the task it holds back, after its next pull, without a Refresh

#### Scenario: A sprint series an older build left

- **Given** the replica holds a `sprint-series` document an older desktop pushed, beside the impediment "Bank sample files missing"
- **When** the second desktop pulls
- **Then** it shows the impediment and writes nothing for the sprint series

### Requirement: The newer change wins per document

```meta
type: requirement
status: draft
tests: [integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.GoalSyncBetweenDevicesTests.The_newer_change_wins_per_goal_on_both_desktops, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.ImpedimentSyncBetweenDevicesTests.The_newer_move_wins_per_impediment_on_both_desktops]
```

The system SHALL keep, when two devices both changed one goal or one impediment since they last synced, the copy with the newer change on every device.

#### Scenario: Two edits to one impediment

- **Given** the phone set "Bank sample files missing" to Being handled at 09:10, and the desktop changed its note at 09:20 before pulling
- **When** both have synced
- **Then** every device holds the desktop's copy, and the move to Being handled is lost

#### Scenario: Two moves made apart

- **Given** the first desktop reported "Bank sample files missing" to Finance, and the second resolved it later, before either synced
- **When** both have synced
- **Then** both desktops hold it Resolved, and the report to Finance is lost

#### Scenario: Two goals changed at once

- **Given** the first desktop changed "Customers get paid faster" and the second changed "Devbook search ships", both offline
- **When** both have synced
- **Then** both changes are kept on every device

### Requirement: A deletion travels like a change

```meta
type: requirement
status: draft
tests: [integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.GoalSyncBetweenDevicesTests.A_goal_deleted_on_one_desktop_is_deleted_on_the_other]
```

The system SHALL delete a goal or impediment on every paired device once its deletion is the newer change.

#### Scenario: A goal deleted on one desktop

- **Given** the person deleted the goal "Phone capture in daily use" on the first desktop, and the second has not changed it since
- **When** both have synced
- **Then** the goal is gone on both desktops and on the phone
