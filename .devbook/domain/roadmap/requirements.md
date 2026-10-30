# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/roadmap/features.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it.

The scenarios use the default working week: Monday to Friday 09:00 to 17:30, 8.5
hours a day and 42.5 a week. Friday 9 October 2026 is in week 41.

## Planning work that has no task yet

```meta
type: requirements
status: draft
related: [.devbook/domain/roadmap/features.md#planning-work-that-has-no-task-yet]
```

> The requirements of completing a plan by hand. The feature chapter says why a
> person completes it; this says what completing offers and how a completed plan
> is read. The rules the item holds to are its
> [invariants](domain.invariants.md#invariant-gathered-tasks-outrank-a-completion-by-hand).

The scenarios plan "Write the launch post" from Monday 5 to Friday 9 October, and
read the roadmap on Monday 12 October.

### Requirement: Complete is offered only while no task carries the plan

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.AnOpenTaskLessItem_OffersToComplete_NeverToOpenInTasks, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.AnItemTasksCarry_OffersToOpenThemInTasks_NamedForTheItem, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandEditingTests.CompletingFromTheEditor_StoresTheDay_AndDrawsTheBarDone]
```

The system SHALL offer Complete on a plan that no task carries and that is open, and record the day the person completes it as its completion date.

#### Scenario: Completing a task-less plan

- **Given** "Write the launch post" gathers no task and is open
- **When** the person chooses Complete at the end of its bar
- **Then** the plan is completed on Monday 12 October
- **And** its bar stays from Monday 5 to Friday 9 October

#### Scenario: A plan that gathers only a devbook chapter

- **Given** "Write the launch post" gathers one devbook chapter and no task
- **When** the person opens its editor
- **Then** the editor offers Complete

#### Scenario: A plan a task carries

- **Given** a task is filed under "Write the launch post"
- **When** the person opens its editor
- **Then** neither the editor nor the bar offers Complete

### Requirement: The editor offers what fits the plan

```meta
type: requirement
status: draft
tests: [integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandEditingTests.OpenInTasksIsNotOfferedForAnItemNoTaskCarries, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandEditingTests.OpenInTasksIsOfferedForAnExistingItemWithATag_WhenTheShellCanOpenIt, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandEditingTests.CompleteRaisesTheItemsId_ForWorkNoTaskCarries, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandEditingTests.ReopenIsOfferedInsteadOfComplete_OnceTheWorkWasCompleted, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandEditingTests.CompleteIsNotOfferedForNewWork, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandEditingTests.ReopeningFromTheEditor_ClearsTheDay]
```

The system SHALL offer in a plan's editor Open in Tasks only while tasks carry the plan, Complete only while no task carries it and it is open, and Reopen only while it is completed by hand.

#### Scenario: A plan completed by hand

- **Given** "Write the launch post" gathers no task and was completed on Friday 9 October
- **When** the person opens its editor
- **Then** the editor offers Reopen, and neither Complete nor Open in Tasks

#### Scenario: Reopening

- **Given** "Write the launch post" was completed on Friday 9 October
- **When** the person chooses Reopen in its editor
- **Then** the plan is open again and has no completion date

### Requirement: A plan completed by hand is drawn finished where it was planned

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.ATaskLessItemCompletedByHand_IsDrawnFinished_WhereItWasPlanned_AndNeverOverdue, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.ATaskLessItemCompletedByHand_IsDrawnFinished_WithoutAForecastToo]
```

The system SHALL draw a plan completed by hand over its planned window, locked and in the done style [every finished part wears](#requirement-a-finished-part-is-drawn-as-done), and never as overdue or as work with no task yet.

#### Scenario: A plan completed after its window ended

- **Given** "Write the launch post" gathers no task and was completed on Monday 12 October
- **When** the roadmap draws the plan
- **Then** its bar runs from Monday 5 to Friday 9 October in the done style
- **And** the bar is locked, carries no overdue alert, and is not outlined as work with no task yet

### Requirement: Keep-up leaves a plan completed by hand alone

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapItemCompletionTests.TheKeepUpProjection_LeavesAnItemCompletedByHandWhereItWasPlanned
```

The system SHALL leave the window of a plan completed by hand as it is stored, even when the plan is sized by its effort.

#### Scenario: An effort-sized plan completed by hand

- **Given** "Write the launch post" was placed by its effort, gathers no task, and was completed on Thursday 8 October
- **When** the roadmap draws the plan on Monday 12 October
- **Then** its window stays from Monday 5 to Friday 9 October and does not start today

### Requirement: Work waiting on a plan completed by hand waits until its completion date

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapItemCompletionTests.WhatWaitsOnAnItemCompletedByHand_IsReleasedWhenItWasCompleted_NotWhenItsWindowEnds
```

The system SHALL end the wait of every item that depends on a plan completed by hand on the plan's completion date rather than on its planned end.

#### Scenario: Completed early

- **Given** "Publish the launch" waits on "Write the launch post", which was completed on Wednesday 7 October
- **When** the roadmap lays out "Publish the launch"
- **Then** its wait on "Write the launch post" ends on Wednesday 7 October, not on Friday 9 October

### Requirement: The dashboard reads a plan completed by hand as finished

```meta
type: requirement
status: draft
related: [.devbook/domain/productivity/features.md#whether-each-plan-will-land-in-its-window]
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapPlanProgressSourceTests.A_task_less_item_completed_by_hand_crosses_as_finished_on_the_day_it_was_completed, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapPlanProgressSourceTests.An_item_its_tasks_carry_crosses_by_its_tasks_whatever_day_was_set_by_hand]
```

The system SHALL report a plan completed by hand as Finished in the dashboard's plan outlook, with its completion date as the day it landed.

#### Scenario: The outlook after completing

- **Given** "Write the launch post" gathers no task and was completed on Thursday 8 October
- **When** the person reads the dashboard's plan outlook
- **Then** the plan reads Finished and landed on Thursday 8 October

#### Scenario: A task filed after completing

- **Given** "Write the launch post" was completed on Thursday 8 October and an open task was filed under it afterwards
- **When** the person reads the dashboard's plan outlook
- **Then** the plan is not read as Finished, because its task is open

## Carrying the pace with the person

```meta
type: requirements
status: draft
related: [.devbook/domain/roadmap/features.md#carrying-the-pace-with-the-person]
```

> The requirements of the working week travelling with the pace. The feature
> chapter says what travels and why; this says what it promises about the day
> overrides.

### Requirement: Day overrides travel inside the working week

```meta
type: requirement
status: draft
```

The system SHALL carry the day overrides inside the working week of the synced pace document, under the working week's own change stamp, so the newer change to the working week wins together with its overrides.

#### Scenario: A blocked day reaches the other device

- **Given** two paired devices with sync on
- **When** the person blocks Friday 9 October on the first device
- **Then** the second device counts no hours on Friday 9 October

#### Scenario: A newer change to the week wins with its overrides

- **Given** the first device blocked Friday 9 October and the second device has not pulled it
- **When** the person changes Monday's hours on the second device and the devices sync
- **Then** both devices hold the second device's working week, and Friday 9 October is worked

### Requirement: A working week without overrides has none

```meta
type: requirement
status: draft
```

The system SHALL read a working week that carries no overrides list as one with no day overrides.

#### Scenario: A pace saved before overrides existed

- **Given** a pace document whose working week has no overrides list
- **When** the roadmap opens
- **Then** every date counts the hours its weekday's pattern gives it

## Reading the near term closely

```meta
type: requirements
status: draft
related: [.devbook/domain/roadmap/features.md#reading-the-near-term-closely]
```

> The requirements of the day and week heads on the axis. The feature chapter
> says how the axis is ruled and why; this says what the heads promise and how
> they count the actual hours.

### Requirement: A date that is not worked is hatched

```meta
type: requirement
status: draft
```

The system SHALL draw the head and column of every date that counts no hours, whether its weekday is not worked or the person blocked it, in the colours and hatching of a pattern weekend, and every other date as an ordinary head.

#### Scenario: A blocked Friday

- **Given** the person blocked Friday 9 October
- **When** the axis shows that week in days
- **Then** Friday 9 October's head and column look exactly like Sunday 11 October's

#### Scenario: An unblocked Saturday

- **Given** the person unblocked Saturday 10 October
- **When** the axis shows that week in days
- **Then** Saturday 10 October's head and column look like Thursday 8 October's, with no hatching

### Requirement: A day head does not look like a button

```meta
type: requirement
status: draft
```

The system SHALL draw a day head with the same border, fill and text it had before heads became toggles, so nothing but keyboard focus sets it apart from the original axis.

#### Scenario: The axis at rest

- **Given** the default working week and no day overrides
- **When** the axis shows the week of Monday 5 October in days
- **Then** no day head shows a button border, fill or shadow

#### Scenario: Keyboard focus

- **Given** the axis shows the week of Monday 5 October in days
- **When** the person tabs to Friday 9 October's head
- **Then** that head shows a focus outline

### Requirement: A date that breaks the pattern is marked

```meta
type: requirement
status: draft
```

The system SHALL put a small marker on the head of every date that carries a day override, and on no other head, without changing the look its worked or not-worked state gives it.

#### Scenario: One blocked date among a pattern week

- **Given** the person blocked Friday 9 October
- **When** the axis shows that week in days
- **Then** Friday 9 October's head looks like Sunday 11 October's and carries the marker, and Sunday 11 October's head does not

#### Scenario: One unblocked weekend date

- **Given** the person unblocked Saturday 10 October
- **When** the axis shows that week in days
- **Then** Saturday 10 October's head looks like an ordinary head and carries the marker

### Requirement: A week head sums its dates

```meta
type: requirement
status: draft
```

The system SHALL count as each week head's planned hours the sum of the working hours of its seven dates, day overrides included.

#### Scenario: A week with one blocked and one unblocked date

- **Given** today is Saturday 3 October, the person blocked Friday 9 October and unblocked Saturday 10 October, whose stored hours are 10:00 to 14:00
- **When** the axis shows week 41
- **Then** its head reads 38 hours

### Requirement: The Hours switch shows or hides the hours line

```meta
type: requirement
status: draft
```

The system SHALL put one Hours switch on the roadmap toolbar, on by default and remembered on the device without syncing, and SHALL show no hours on any head while it is off.

#### Scenario: Turning the hours off

- **Given** the Hours switch is on
- **When** the person turns it off
- **Then** no day or week head shows an hours figure, and every column keeps its width

#### Scenario: The choice stays on the device

- **Given** two paired devices with sync on, and the person turned the Hours switch off on the first
- **When** the person restarts the app on the first device and opens the roadmap on the second
- **Then** the first device's switch is still off and the second device's is on

### Requirement: A head that has begun shows its actual hours

```meta
type: requirement
status: draft
```

The system SHALL show on each day and week head that has begun, today and the current week included, its actual hours up to now alone, and on each head still to come its planned hours alone.

#### Scenario: A past day

- **Given** today is Monday 12 October and the person's working stretches total 6.2 hours on Thursday 8 October, a worked day
- **When** the axis shows Thursday 8 October
- **Then** its head reads "6.2h"

#### Scenario: A past worked day with no stretches

- **Given** today is Monday 12 October and the person has no working stretches on Wednesday 7 October, a worked day
- **When** the axis shows Wednesday 7 October
- **Then** its head reads "0h"

#### Scenario: A past day not worked

- **Given** today is Monday 12 October and the person's working stretches total 2 hours on Saturday 10 October, a day the pattern leaves off
- **When** the axis shows Saturday 10 October
- **Then** its head reads "2.0h"

#### Scenario: Today

- **Given** today is Monday 12 October, a worked day, and the person's working stretches total 3 hours so far
- **When** the axis shows today
- **Then** its head reads "3.0h"

#### Scenario: The current week

- **Given** today is Wednesday 14 October and the person's working stretches total 15.5 hours since Monday 12 October
- **When** the axis shows week 42
- **Then** its head reads "15.5h"

#### Scenario: A day still to come

- **Given** today is Monday 12 October and Tuesday 13 October is a worked day
- **When** the axis shows Tuesday 13 October
- **Then** its head reads "8.5h"

### Requirement: Actual hours count the person's working stretches

```meta
type: requirement
status: draft
```

The system SHALL count as a date's actual hours the time the person's working stretches cover on that date, over every session on every paired machine, counting overlapping time once.

#### Scenario: Two turns 20 minutes apart join

- **Given** on Thursday 8 October the person prompted at 10:00, the agent answered until 10:05, and the person prompted again at 10:20 and the agent answered until 10:30
- **When** the axis shows that day
- **Then** its actual hours are one stretch of 30 minutes, from 10:00 to 10:30

#### Scenario: Two turns 40 minutes apart stay apart

- **Given** on Thursday 8 October the person prompted at 10:00, the agent answered until 10:05, and the person prompted again at 10:40 and the agent answered until 10:45
- **When** the axis shows that day
- **Then** its actual hours are two stretches of 5 minutes each, and the 35 minutes between them count nothing

#### Scenario: The pause counts from the end of the answer

- **Given** on Thursday 8 October the person prompted at 09:00, the agent answered until 09:40, and the person prompted again at 09:45 and the agent answered until 09:50
- **When** the axis shows that day
- **Then** its actual hours are one stretch of 50 minutes, from 09:00 to 09:50

#### Scenario: Moving between sessions within half an hour

- **Given** on Thursday 8 October the person worked a stretch from 10:00 to 10:20 in one session and a stretch from 10:40 to 11:00 in another
- **When** the axis shows that day
- **Then** its actual hours are one hour, from 10:00 to 11:00

#### Scenario: An evening past midnight counts on its own date

- **Given** the person worked one stretch from 23:00 on Wednesday 7 October to 02:00 on Thursday 8 October
- **When** the axis shows both days
- **Then** Wednesday 7 October's actual hours are 3 hours and Thursday 8 October gains none

#### Scenario: A stretch across four in the morning counts toward both dates

- **Given** the person worked one stretch from 03:00 to 05:00 on Thursday 8 October
- **When** the axis shows both days
- **Then** Wednesday 7 October gains 1 hour and Thursday 8 October gains 1 hour

#### Scenario: An open stretch counts up to now

- **Given** it is 14:00 on Monday 12 October and the agent has been answering the person's 13:30 turn since then
- **When** the axis shows today
- **Then** today's actual hours are 30 minutes

#### Scenario: A stretch ends at the end of the reply

- **Given** on Thursday 8 October the person prompted at 11:00, the agent answered until 11:45, and the person made no turn after it
- **When** the axis shows that day
- **Then** its actual hours are one stretch of 45 minutes, from 11:00 to 11:45

#### Scenario: Overnight subagent work adds nothing

- **Given** the person's last turn on Wednesday 7 October came at 22:00, its reply ended at 22:10, and subagents went on working until 06:00 on Thursday 8 October with no human turn
- **When** the axis shows Thursday 8 October
- **Then** the night's subagent work adds no actual hours to it

#### Scenario: Overlapping stretches count once

- **Given** on Thursday 8 October the person worked a stretch from 14:00 to 15:00 on one paired machine and a stretch from 14:30 to 15:30 on the other
- **When** the axis shows that day
- **Then** its actual hours are 1.5 hours

### Requirement: Only the person's turns make a stretch

```meta
type: requirement
status: draft
```

The system SHALL start or extend a working stretch only at a human turn.

#### Scenario: A hand-back or a task notification is not a human turn

- **Given** on Thursday 8 October the person prompted at 10:00, the agent answered until 10:05, and at 10:20 a task notification and a subagent's hand-back made the agent answer again until 10:40
- **When** the axis shows that day
- **Then** its actual hours are one stretch of 5 minutes, from 10:00 to 10:05

#### Scenario: An answer to the agent's question is a human turn

- **Given** on Thursday 8 October the person prompted at 10:00, the agent asked a question at 10:10, and the person answered it at 10:25 and the agent answered until 10:35
- **When** the axis shows that day
- **Then** its actual hours are one stretch of 35 minutes, from 10:00 to 10:35

#### Scenario: A session that does not record who made a turn

- **Given** a Copilot session ran from 09:00 to 12:00 on Thursday 8 October
- **When** the axis shows that day
- **Then** that session adds no actual hours

### Requirement: A begun head's hours open the report behind them

```meta
type: requirement
status: draft
```

The system SHALL let the person open, from the hours on a begun day or week head, the working days of that head's week with each day's total and the working stretches it adds up from.

#### Scenario: Checking a day's figure

- **Given** today is Monday 5 October and Thursday 1 October's head reads "4.0h"
- **When** the person presses "4.0h"
- **Then** the report opens on 28 September to 4 October, Thursday's total reads 4.0h, and its stretches each show when they ran, how long they count and their prompts

#### Scenario: The day head still toggles

- **Given** Thursday 1 October's head reads "4.0h"
- **When** the person presses "4.0h"
- **Then** Thursday 1 October stays a worked day

### Requirement: A begun head shows no hours when actual hours cannot be read

```meta
type: requirement
status: draft
```

The system SHALL show no hours on a head that has begun when the actual hours cannot be read, because a planned figure there would read as hours worked.

#### Scenario: Session activity is unreadable

- **Given** today is Monday 12 October and the session activity cannot be read
- **When** the roadmap opens on last week in days
- **Then** the heads of last week, today and week 42 show no hours, Tuesday 13 October's head reads "8.5h", and the bars are drawn as before

## Blocking and unblocking a day

```meta
type: requirements
status: draft
related: [.devbook/domain/roadmap/features.md#blocking-and-unblocking-a-day]
```

> The requirements of pressing a day head. The feature chapter says why a single
> date can differ from the week; this says what a press promises.

### Requirement: A day head is a toggle

```meta
type: requirement
status: draft
```

The system SHALL make each day column's head a button, pressed by pointer, Enter or Space, named "Block" with the date on a date that is worked and "Unblock" with the date on one that is not.

#### Scenario: The names of a worked and a day off

- **Given** the default working week
- **When** a screen reader reads the heads of Friday 9 and Saturday 10 October
- **Then** it reads "Block Fri 9 Oct" and "Unblock Sat 10 Oct"

#### Scenario: Blocking from the keyboard

- **Given** focus is on Friday 9 October's head
- **When** the person presses Space
- **Then** Friday 9 October is blocked and its head is named "Unblock Fri 9 Oct"

### Requirement: A blocked date counts no hours

```meta
type: requirement
status: draft
```

The system SHALL count no working hours on a date the person blocked.

#### Scenario: Blocking a Friday

- **Given** Friday 9 October is worked from 09:00 to 17:30
- **When** the person blocks it
- **Then** its head shows no hours and week 41 counts 34 hours

### Requirement: An unblocked date counts its weekday's hours

```meta
type: requirement
status: draft
```

The system SHALL count on a date the person unblocked the hours from the start to the end its weekday stores in the weekly pattern.

#### Scenario: Unblocking a Saturday

- **Given** Saturday is not worked and stores 10:00 to 14:00
- **When** the person unblocks Saturday 10 October
- **Then** that date counts 4 hours and Saturday 17 October still counts none

### Requirement: Returning to the pattern removes the override

```meta
type: requirement
status: draft
```

The system SHALL remove a date's day override when a press makes the date match its weekday's pattern again.

#### Scenario: Unblocking a blocked Friday

- **Given** the person blocked Friday 9 October
- **When** the person presses its head again
- **Then** Friday 9 October holds no override and counts 8.5 hours

### Requirement: A past date can be changed

```meta
type: requirement
status: draft
```

The system SHALL let the person block or unblock a date that has passed as they would a date to come.

#### Scenario: Blocking last Friday

- **Given** today is Monday 12 October
- **When** the person blocks Friday 9 October
- **Then** Friday 9 October counts no hours and the measured paces count without it

## Setting days off in a list

```meta
type: requirements
status: draft
related: [.devbook/domain/roadmap/features.md#setting-days-off-in-a-list]
```

> The requirements of the Days off dialog. The feature chapter says why a list
> sits beside the head toggle; this says what the dialog promises.

### Requirement: The Days off button opens the dialog

```meta
type: requirement
status: draft
```

The system SHALL put a Days off button on the roadmap toolbar that opens the Days off dialog.

#### Scenario: Opening the dialog

- **Given** the roadmap is open
- **When** the person presses Days off on the toolbar
- **Then** the Days off dialog opens

### Requirement: The dialog lists every day override

```meta
type: requirement
status: draft
```

The system SHALL list every day override in the Days off dialog with its date, its weekday and whether it is blocked or unblocked, the dates still to come first and soonest first, and the dates passed only after the person asks to show past.

#### Scenario: Future overrides first, past ones hidden

- **Given** today is Saturday 3 October and the person blocked Friday 25 September and Friday 9 October and unblocked Saturday 17 October
- **When** the person opens the Days off dialog
- **Then** it lists "Fri 9 Oct, blocked" and then "Sat 17 Oct, unblocked", and not Friday 25 September

#### Scenario: Showing the past

- **Given** the same overrides and the Days off dialog is open
- **When** the person presses "Show past"
- **Then** the list also shows "Fri 25 Sep, blocked", after the dates still to come

### Requirement: A range of days off blocks only the worked dates

```meta
type: requirement
status: draft
```

The system SHALL block, for a range from one date to another added in the Days off dialog, every date in the range that the weekly pattern works, SHALL add no override for a date in it that the pattern leaves off, and SHALL remove an unblocked override inside the range, so every date in it is a day off.

#### Scenario: Two weeks off

- **Given** the default working week and no day overrides
- **When** the person adds days off from Monday 12 October to Sunday 25 October
- **Then** the ten weekdays from Monday 12 to Friday 23 October are blocked, the four weekend dates carry no override, and weeks 42 and 43 each count no hours

#### Scenario: A range over a date already blocked

- **Given** the person blocked Wednesday 14 October
- **When** the person adds days off from Monday 12 October to Friday 16 October
- **Then** the dialog lists the five dates from 12 to 16 October as blocked, each once

#### Scenario: A range over an unblocked Saturday

- **Given** the person unblocked Saturday 17 October
- **When** the person adds days off from Monday 12 October to Sunday 18 October
- **Then** Saturday 17 October carries no override and counts no hours

### Requirement: A single worked date can be added

```meta
type: requirement
status: draft
```

The system SHALL unblock a single date the person adds as worked in the Days off dialog when the weekly pattern leaves that date off, and SHALL add no override when the pattern already works it.

#### Scenario: Working a Saturday

- **Given** Saturday is not worked and stores 10:00 to 14:00
- **When** the person adds Saturday 10 October as a worked date
- **Then** the dialog lists "Sat 10 Oct, unblocked" and that date counts 4 hours

#### Scenario: A date the pattern already works

- **Given** the default working week
- **When** the person adds Thursday 8 October as a worked date
- **Then** no override is added and Thursday 8 October counts 8.5 hours

### Requirement: Removing an entry returns its date to the pattern

```meta
type: requirement
status: draft
```

The system SHALL remove a date's day override when the person removes its entry from the Days off dialog.

#### Scenario: Removing a blocked Friday

- **Given** the person blocked Friday 9 October
- **When** the person removes its entry in the Days off dialog
- **Then** the entry leaves the list and Friday 9 October counts 8.5 hours

### Requirement: The dialog writes the overrides a head press writes

```meta
type: requirement
status: draft
```

The system SHALL record each change made in the Days off dialog as the same day override a press on the date's head records, and SHALL lay the bars out again as soon as the change is made.

#### Scenario: The head shows a date blocked in the dialog

- **Given** the axis shows the week of Monday 5 October in days
- **When** the person adds days off from Friday 9 October to Friday 9 October in the Days off dialog
- **Then** Friday 9 October's head looks like Sunday 11 October's, carries the marker, and is named "Unblock Fri 9 Oct"

#### Scenario: A plan moves at once

- **Given** a plan of 7 points at 7 points a week starts on Monday 5 October
- **When** the person adds days off from Wednesday 7 October to Wednesday 7 October in the Days off dialog
- **Then** the plan's bar ends on Monday 12 October without the dialog being closed

#### Scenario: A range reaches the other device

- **Given** two paired devices with sync on
- **When** the person adds days off from Monday 12 October to Sunday 25 October on the first device
- **Then** the second device's Days off dialog lists the same ten blocked dates

## Looking back at finished plans

```meta
type: requirements
status: draft
related: [.devbook/domain/roadmap/features.md#looking-back-at-finished-plans]
```

> The requirements of showing or hiding finished plans. The feature chapter says
> where a finished plan is drawn; this says what the Completed switch promises.

### Requirement: The Completed switch shows or hides finished plans

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.WithFinishedWorkHidden_AFinishedItemIsNotDrawn_NorTheArrowToIt, unit:dotnet:Backlog.Desktop.UI.UnitTests.ShellNavigationStoreTests.The_roadmap_completed_switch_is_on_until_turned_off_and_off_survives_a_restart, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandTests.TheCompletedSwitch_IsOnUntilTurnedOff_AndOffIsKeptOnThisDevice]
```

The system SHALL put one Completed switch on the roadmap toolbar beside the Hours switch, on by default and remembered on the device without syncing, and SHALL draw no plan whose work is all done, or that was completed by hand, while it is off.

#### Scenario: Turning the finished plans off

- **Given** the Completed switch is on, "Write the launch post" was finished on Friday 9 October, and "Publish the launch" waits on it in the same lane
- **When** the person turns the switch off
- **Then** "Write the launch post" is not drawn, its lane closes up over "Publish the launch", and no arrow is drawn between them

#### Scenario: The choice stays on the device

- **Given** two paired devices with sync on, and the person turned the Completed switch off on the first
- **When** the person restarts the app on the first device and opens the roadmap on the second
- **Then** the first device's switch is still off and the second device's is on

## Forecasting work in flight

```meta
type: requirements
status: draft
related: [.devbook/domain/roadmap/features.md#forecasting-work-in-flight]
```

> The requirements of forecasting a plan in flight. The feature chapter says how
> each part is forecast; this says how a finished part looks beside the rest.

### Requirement: A finished part is drawn as done

```meta
type: requirement
status: draft
```

The system SHALL draw a part whose counted tasks are all done in a done style that tells it apart from an unfinished part by more than its lock and its full fill.

A part counts its own tasks and the shared tasks
[it carries](#requirement-a-shared-task-counts-where-own-work-is-still-open), so a
band whose own tasks are all done can draw finished while shared tasks filed under
it are still open. A part that counts no task is drawn as done only when the whole
plan is finished.

#### Scenario: A finished part beside an open one

- **Given** a plan whose own task in `finance` was done on Tuesday 6 October and whose own task in `fincent` is open, with shared tasks filed under both still open
- **When** the roadmap draws the plan on Monday 12 October
- **Then** `finance`'s bar is drawn in the done style, `fincent`'s is not, and both are locked

#### Scenario: A finished plan looking back

- **Given** a plan whose tasks were all done by Friday 9 October
- **When** the person scrolls the roadmap back to that week
- **Then** each of its bars is drawn in the done style

#### Scenario: A repository holding none of the plan's tasks

- **Given** a plan filed under `app` and `docs` whose one task is filed under `app` alone and is open
- **When** the roadmap draws the plan on Monday 12 October
- **Then** neither `app`'s bar nor `docs`'s is drawn in the done style

## Placing a plan in time

```meta
type: requirements
status: draft
related: [.devbook/domain/roadmap/features.md#placing-a-plan-in-time]
```

> The requirements of placing a plan in time. The feature chapter says how a plan
> is placed; this says how a plan filed under several repositories is laid out per
> repository, where a shared task counts, and how the day overrides count, for a
> plan and for a measured pace.

### Requirement: Each repository draws its own part at its own pace

```meta
type: requirement
status: draft
tests: [integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandPartsTests, integration:dotnet:Backlog.Desktop.UI.UnitTests.ImportPlanAcrossRoadmapTests.A_plan_waiting_on_a_plan_imported_with_it_starts_the_worked_day_after_it_ends, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapItemPartsTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.OneRepositorysSliceOfALargePlan_SpansOnlyThatRepositorysWork]
```

The system SHALL lay out a plan sized by its effort as one part per repository, each counting its own tasks and the shared tasks it carries at its repository's pace from the day after what it waits on ends, and SHALL end the plan's window on the latest part end.

An [own task](domain.md#own-task) is filed under one repository, and a
[shared task](domain.md#shared-task) under several. Which bands carry a shared
task is
[the next requirement](#requirement-a-shared-task-counts-where-own-work-is-still-open).

The scenarios start on Monday 12 October. Each plan waits on nothing unless the
scenario says so, and its window is still sized by its effort.

#### Scenario: A part waits on another repository's part

- **Given** a plan with a 4-point task in `app` and a 4-point task in `site` that waits on it, both repositories at 4 points a week
- **When** the roadmap lays the plan out
- **Then** `app`'s band draws it from Monday 12 to Friday 16 October and `site`'s band from Monday 19 to Friday 23 October

#### Scenario: The plan ends on its latest part end

- **Given** the plan of the previous scenario, and a second plan of 4 points in `app` that depends on it
- **When** the roadmap lays both plans out
- **Then** the first plan's window is Monday 12 to Friday 23 October, and the second plan starts on Monday 26 October

#### Scenario: One repository's part of a large plan

- **Given** a plan of 52 points filed under seven repositories, of which 16 open points are filed in `fincent` and wait on no other repository's tasks, `fincent` placed at 40 points a week
- **When** the roadmap lays the plan out
- **Then** `fincent`'s band draws the plan from Monday 12 to Tuesday 13 October, and not over the whole plan's window

#### Scenario: Dragging a part moves the whole plan

- **Given** the plan of the first scenario, drawn from Monday 12 to Friday 16 October in `app` and from Monday 19 to Friday 23 October in `site`
- **When** the person drags the `site` part to a new date
- **Then** the plan is moved by hand, and both bands draw it over its one moved window

### Requirement: A shared task counts where own work is still open

```meta
type: requirement
status: draft
```

The system SHALL count a shared task in full in each of its bands that still holds open own tasks, else in the bands whose own work ended last, or in all its bands when none had own tasks.

A band whose own tasks are all done therefore draws its part as finished, where
its work ran, as long as each open shared task filed under it has another band
whose own work is still open. The scenarios start on Monday 12 October, and each
plan waits on nothing.

#### Scenario: A task filed under two repositories counts in both

- **Given** a plan whose one task of 8 points is filed under `app` and `site`, `app` placed at 8 points a week and `site` at 4
- **When** the roadmap lays the plan out
- **Then** `app`'s band draws it from Monday 12 to Friday 16 October and `site`'s band from Monday 12 to Friday 23 October

#### Scenario: A band whose own task is done draws finished while shared tasks stay open

- **Given** a plan whose own task in `finance`, of 5 points, was started on Monday 5 October and done on Tuesday 6 October, whose own task in `fincent`, of 6 points, is open, and whose three shared tasks of 2 points each, filed under `finance` and `fincent`, are open, `fincent` placed at 6 points a week
- **When** the roadmap lays the plan out
- **Then** `finance`'s band draws its part as finished from Monday 5 to Tuesday 6 October, `fincent`'s band draws 12 points from Monday 12 to Friday 23 October, and the plan's window ends on Friday 23 October

#### Scenario: Shared tasks left alone count where own work ended last

- **Given** the plan of the previous scenario, except that `fincent`'s own task was started on Wednesday 7 October and done on Thursday 8 October
- **When** the roadmap lays the plan out
- **Then** `finance`'s band draws its part as finished from Monday 5 to Tuesday 6 October, and `fincent`'s band draws its part from Wednesday 7 to Friday 16 October, carrying the 6 shared points

### Requirement: An effort window counts the overrides

```meta
type: requirement
status: draft
```

The system SHALL count an effort-sized window forward over the working hours of each date, giving a blocked date none and an unblocked date its weekday's hours, and SHALL re-place the window as soon as a date is toggled.

#### Scenario: A blocked day in the window

- **Given** a plan of 7 points at 7 points a week starts on Monday 5 October
- **When** the person blocks Wednesday 7 October
- **Then** the plan ends on Monday 12 October instead of Friday 9 October

#### Scenario: An unblocked day in the window

- **Given** the same plan, Wednesday 7 October blocked, and Saturday stores 09:00 to 17:30
- **When** the person unblocks Saturday 10 October
- **Then** the plan ends on Saturday 10 October

### Requirement: A measured pace counts the hours in its stretch

```meta
type: requirement
status: draft
```

The system SHALL measure a pace as the points finished in its stretch, divided by the working hours that stretch holds with its day overrides, times the weekly pattern's total hours.

#### Scenario: A two-week stretch with a blocked Friday

- **Given** 17 points were finished over the last two weeks and one Friday in them was blocked
- **When** the roadmap measures the two-week pace
- **Then** it shows 17 ÷ 76.5 × 42.5, about 9.44 points a week

#### Scenario: A stretch with no overrides

- **Given** 17 points were finished over the last two weeks and no date in them carries an override
- **When** the roadmap measures the two-week pace
- **Then** it shows 8.5 points a week

### Requirement: A plan sized by its effort keeps up with its work

```meta
type: requirement
status: draft
tests: [integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandKeepUpTests.Opening_the_roadmap_stores_the_window_the_work_makes_from_today_and_draws_it, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandKeepUpTests.A_task_marked_done_stores_the_shorter_window, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.KeepUpWithWorkTests.AnEffortPlacedItemLeftBehind_IsStoredFromToday_StillPlacedByEffort, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.KeepUpWithWorkTests.ATaskMarkedDone_PullsTheEndIn_ToWhatIsLeft, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.KeepUpWithWorkTests.AnItemPlacedByItsDueDate_IsNeverWritten, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.KeepUpWithWorkTests.AnItemPlacedByHand_IsNeverWritten, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.EffortWindowTests.ADueDateOrAHandPlacedWindow_IsReadAsStored]
```

The system SHALL lay out again, every time the roadmap is read, each plan whose window is still sized by its effort and whose work is not finished, from today over the effort not yet done.

The scenarios use a pace of 7 points a week, and nothing in the plans has begun.

#### Scenario: A plan left behind starts today

- **Given** a plan of 7 points sized by its effort, stored from Monday 5 to Friday 9 October
- **When** the roadmap opens on Monday 12 October
- **Then** the plan runs from Monday 12 to Friday 16 October and is still sized by its effort

#### Scenario: A finished task pulls the end in

- **Given** a plan of two 7-point tasks sized by its effort, running from Monday 12 to Friday 23 October
- **When** one of the tasks is marked done on Monday 12 October
- **Then** the plan ends on Friday 16 October

#### Scenario: A plan placed by its due date keeps its dates

- **Given** a plan placed by its due date from Monday 5 to Friday 30 October
- **When** the roadmap opens on Monday 12 October
- **Then** the plan still runs from Monday 5 to Friday 30 October

#### Scenario: A plan moved by hand keeps its dates

- **Given** a plan the person moved by hand to Monday 5 to Friday 9 October
- **When** the roadmap opens on Monday 12 October
- **Then** the plan still runs from Monday 5 to Friday 9 October

### Requirement: A plan is never shorter than its start day

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.EffortWindowTests.AWindowIsNeverShorterThanItsStartDay
```

The system SHALL give a plan sized by its effort, and each of its parts, a window of at least the day it starts, however little effort it gathered.

#### Scenario: One point at a fast pace

- **Given** a plan whose one task registered 1 point, placed at 1000 points a week
- **When** the roadmap lays the plan out from Monday 12 October
- **Then** the plan runs on Monday 12 October alone

### Requirement: A due date ends the plan whatever its effort

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.ImportedPlanPlacementTests.ADueDateEndsTheWindow_WhateverTheEffort, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.ImportPlanItemsTests.ADueDateEndsTheWindow]
```

The system SHALL end a plan that states a due date on that date, without consulting the pace or the effort its tasks registered.

#### Scenario: A large plan with a near due date

- **Given** a plan due on Friday 23 October whose tasks registered 40 points, and a pace of 7 points a week
- **When** the plan is imported on Monday 12 October
- **Then** the plan runs from Monday 12 to Friday 23 October

### Requirement: A pace that cannot be read is seven points a week

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.PlanningVelocitySettingsStoreTests.An_untouched_store_reads_seven_points_a_week_and_writes_no_file, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.PlanningVelocitySettingsStoreTests.A_hand_edited_file_the_roadmap_could_not_divide_by_reads_as_the_default]
```

The system SHALL read a pace that is unset, unreadable, not a number, or not positive as seven points a working week.

#### Scenario: No pace was ever set

- **Given** the person has never set a pace
- **When** the roadmap places a plan sized by its effort
- **Then** it places the plan at 7 points a week

#### Scenario: A hand-edited pace with a comma

- **Given** the pace file was edited by hand to hold `2,5`
- **When** the roadmap reads the pace
- **Then** the pace in use is 7 points a week, not 25

### Requirement: A typed pace the roadmap cannot store as typed is refused

```meta
type: requirement
status: draft
tests: [integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandPaceTests.A_pace_the_roadmap_could_not_divide_by_says_so_and_changes_nothing, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.PlanningVelocitySettingsStoreTests.A_pace_of_zero_or_less_is_refused_and_changes_nothing, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.PlanningVelocitySettingsStoreTests.A_pace_too_fine_to_write_down_is_refused_rather_than_rounded_away, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.PlanningVelocitySettingsStoreTests.A_pace_that_is_not_a_number_is_refused_and_changes_nothing]
```

The system SHALL refuse a typed pace of zero or less, one finer than 0.0001, or one written with a comma as the decimal mark, say why beside the field, and leave the pace as it was.

The scenarios start from a pace of 4 points a week.

#### Scenario: Zero

- **When** the person types 0 as the pace
- **Then** the roadmap says the pace must be above zero, and the pace stays 4

#### Scenario: Finer than four decimal places

- **When** the person types 0.00001 as the pace
- **Then** the roadmap refuses it rather than rounding it, and the pace stays 4

#### Scenario: A decimal comma

- **When** the person types 2,5 as the pace
- **Then** the roadmap refuses it as not a number, and the pace stays 4

## Reading and rescheduling on a timeline

```meta
type: requirements
status: draft
related: [.devbook/domain/roadmap/features.md#reading-and-rescheduling-on-a-timeline]
```

> The requirements of moving work on the timeline. The feature chapter says how
> the view proposes a placement and the plan decides; this says when the view
> proposes nothing at all.

### Requirement: A drop that moves nothing proposes nothing

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.RoadmapTimelineTests.A_gesture_that_travelled_no_whole_week_reports_nothing, unit:dotnet:Backlog.UI.Components.UnitTests.RoadmapTimelineTests.An_edge_that_clamps_back_to_where_it_started_reports_nothing]
```

The system SHALL propose no change to the plan when a bar is dropped on its own lane less than half a week from where it was picked up, or when a pulled edge clamps back to the date it already had.

#### Scenario: Picked up and put back

- **Given** a bar from Wednesday 7 to Friday 9 October
- **When** the person drags it less than half a week along its own lane and drops it
- **Then** no change is proposed and the plan is not saved

#### Scenario: An edge pulled past the other edge

- **Given** a bar from Monday 5 to Thursday 8 October
- **When** the person drags its start one week later, past its end
- **Then** the start stays on Monday 5 October and no change is proposed

### Requirement: A bar's end carries one action

```meta
type: requirement
status: draft
related: [.devbook/domain/roadmap/requirements.md#planning-work-that-has-no-task-yet]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.AnItemTasksCarry_OffersToOpenThemInTasks_NamedForTheItem, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.AnItemTasksCarry_OffersNothing_WhereTheHostCannotShowTasks, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.AnOpenTaskLessItem_OffersToComplete_NeverToOpenInTasks, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.ACompletedTaskLessItem_OffersNothingOnItsBar, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.AnItemInTwoBands_CarriesItsActionOnTheBarThatEndsLast_Only, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.EachItemSaysWhetherTasksCarryIt_AndWhetherItWasCompletedByHand, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.AnItemTasksCarry_IsNotCompletedByHand_WhateverDayWasSet, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.EveryPartOfAnItemAnswersForTheItem, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.WithoutTheGatheredWork_NoItemSaysWhatItOffers]
```

The system SHALL show at the end of a plan's latest-ending bar, while that bar is hovered or focused, Open in Tasks when tasks carry its plan, Complete when no task carries it and it is open, and no action when it was completed by hand.

#### Scenario: A plan its tasks carry

- **Given** a plan with one task filed under it
- **When** the person points at its bar
- **Then** the bar's end shows Open in Tasks, which opens the plan's tasks in the Tasks view

#### Scenario: A task-less open plan

- **Given** a plan that gathers no task and is open
- **When** the person moves keyboard focus to its bar
- **Then** the bar's end shows Complete

#### Scenario: A plan completed by hand

- **Given** a plan that gathers no task and was completed by hand
- **When** the person points at its bar
- **Then** the bar's end shows no action

#### Scenario: A bar at rest

- **Given** a plan with one task filed under it
- **When** the bar is neither hovered nor focused
- **Then** its end shows no action

### Requirement: A bar whose window ended with its work open is drawn as an alert

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.OnceTasksCarryAnItemCompletedByHand_TheTasksWin_AndItsPassedWindowIsOverdue
```

The system SHALL draw a bar whose window ended before today while its work is open with an error-red outline, which leaves the bar's leading edge in its band colour, and a "!" chip in the fault tone, and say "Overdue: ended" with the bar's end date in its detail.

#### Scenario: A plan left open past its window

- **Given** a plan placed by hand from Monday 5 to Friday 9 October with one open task
- **When** the roadmap draws it on Monday 12 October
- **Then** its bar has an error-red outline and a "!" chip in the fault tone
- **And** its detail opens with "Overdue: ended 9 Oct 2026"

#### Scenario: A plan whose tasks are all done

- **Given** a plan placed by hand from Monday 5 to Friday 9 October whose one task was done on Thursday 8 October
- **When** the roadmap draws it on Monday 12 October
- **Then** its bar carries no overdue alert

#### Scenario: A plan still inside its window

- **Given** a plan placed by hand from Monday 12 to Friday 16 October with one open task
- **When** the roadmap draws it on Monday 12 October
- **Then** its bar carries no overdue alert
