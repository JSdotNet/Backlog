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

## Placing a plan in time

```meta
type: requirements
status: draft
related: [.devbook/domain/roadmap/features.md#placing-a-plan-in-time]
```

> The requirements of placing a plan in time. The feature chapter says how a plan
> is placed; this says how a plan filed under several repositories is laid out per
> repository, and how the day overrides count, for a plan and for a measured pace.

### Requirement: Each repository draws its own part at its own pace

```meta
type: requirement
status: draft
tests: [integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandPartsTests, integration:dotnet:Backlog.Desktop.UI.UnitTests.ImportPlanAcrossRoadmapTests.A_plan_waiting_on_a_plan_imported_with_it_starts_the_worked_day_after_it_ends, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapItemPartsTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.OneRepositorysSliceOfALargePlan_SpansOnlyThatRepositorysWork]
```

The system SHALL lay out a plan sized by its effort as one part per repository, each counting the full points of the tasks filed there at that repository's pace from the day after what it waits on ends, and SHALL end the plan's window on the latest part end.

The scenarios start on Monday 12 October. Each plan waits on nothing unless the
scenario says so, and its window is still sized by its effort.

#### Scenario: A task filed under two repositories counts in both

- **Given** a plan whose one task of 8 points is filed under `app` and `site`, `app` placed at 8 points a week and `site` at 4
- **When** the roadmap lays the plan out
- **Then** `app`'s band draws it from Monday 12 to Friday 16 October and `site`'s band from Monday 12 to Friday 23 October

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

- **Given** the plan of the second scenario, drawn from Monday 12 to Friday 16 October in `app` and from Monday 19 to Friday 23 October in `site`
- **When** the person drags the `site` part to a new date
- **Then** the plan is moved by hand, and both bands draw it over its one moved window

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
