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
> says how the axis is ruled and why; this says what the heads promise.

### Requirement: A date that is not worked is hatched

```meta
type: requirement
status: draft
```

The system SHALL hatch the column of every date that counts no hours, whether its weekday is not worked or the person blocked it, and no other date.

#### Scenario: A blocked Friday

- **Given** the person blocked Friday 9 October
- **When** the axis shows that week in days
- **Then** Friday 9 October is hatched, as Sunday 11 October is

#### Scenario: An unblocked Saturday

- **Given** the person unblocked Saturday 10 October
- **When** the axis shows that week in days
- **Then** Saturday 10 October is not hatched

### Requirement: A date that breaks the pattern is marked

```meta
type: requirement
status: draft
```

The system SHALL put a marker on the head of every date that carries a day override, and on no other head.

#### Scenario: One blocked date among a pattern week

- **Given** the person blocked Friday 9 October
- **When** the axis shows that week in days
- **Then** Friday 9 October's head carries the marker and Sunday 11 October's does not

### Requirement: A week head sums its dates

```meta
type: requirement
status: draft
```

The system SHALL show on each week head the sum of the working hours of its seven dates, day overrides included.

#### Scenario: A week with one blocked and one unblocked date

- **Given** the person blocked Friday 9 October and unblocked Saturday 10 October, whose stored hours are 10:00 to 14:00
- **When** the axis shows week 41
- **Then** its head reads 38 hours

### Requirement: A head that has begun shows actual over planned hours

```meta
type: requirement
status: draft
```

The system SHALL show the actual hours over the planned hours on the head of each day and week that has begun, counting today up to now, the actual hours alone on a begun day that is not worked, and the planned hours alone on every later head.

#### Scenario: A past day

- **Given** agents were active for 6.2 hours on Thursday 8 October, a worked day
- **When** the axis shows that day after it has ended
- **Then** its head reads "6.2 / 8.5h"

#### Scenario: A past day not worked

- **Given** agents were active for 2 hours on Saturday 3 October, a day the pattern leaves off
- **When** the axis shows that day after it has ended
- **Then** its head reads "2.0h"

#### Scenario: Today

- **Given** today is a worked day and agents have been active for 3 hours so far
- **When** the axis shows today
- **Then** its head reads "3.0 / 8.5h"

#### Scenario: A day still to come

- **Given** tomorrow is a worked day
- **When** the axis shows tomorrow
- **Then** its head shows 8.5 hours and no actual hours

### Requirement: Heads fall back to planned hours

```meta
type: requirement
status: draft
```

The system SHALL show the planned hours alone on every head when the actual hours cannot be read.

#### Scenario: Session activity is unreadable

- **Given** the session activity cannot be read
- **When** the axis shows last week in days
- **Then** each day head shows its planned hours and the roadmap still opens

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

## Placing a plan in time

```meta
type: requirements
status: draft
related: [.devbook/domain/roadmap/features.md#placing-a-plan-in-time]
```

> The requirements of counting hours for a plan and for a measured pace. The
> feature chapter says how a plan is placed; this says how the day overrides
> count.

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
