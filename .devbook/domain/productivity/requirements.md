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

The system SHALL show for each day in the dashboard's period the person's actual hours split into the time inside that date's office hours and the time outside them, beside the date's planned hours.

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

The system SHALL show for each week in the dashboard's period the sum of its days' time inside office hours, time outside them, and planned hours.

#### Scenario: A week with a late day and a blocked day

- **Given** in week 41 the person worked from 16:00 to 19:00 on Thursday 8 October and from 10:00 to 12:00 on Friday 9 October, which they blocked, and worked no other stretch
- **When** the dashboard shows week 41 in the Hours worked part
- **Then** it shows 1.5 hours inside office hours and 3.5 hours outside, against 34 hours planned
