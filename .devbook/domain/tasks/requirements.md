# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/tasks/features.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it.

## My Day

```meta
type: requirements
status: draft
related: [.devbook/domain/tasks/features.md#my-day]
```

> The requirements of My Day on the phone and how the phone's changes reach the
> desktop. The feature chapter says what My Day is and why; this says what it
> promises.

### Requirement: Today places each task by its agenda time

```meta
type: requirement
status: draft
```

The phone's Today SHALL show the open task in today's My Day whose block contains the current time as the Now card, the other open tasks with an agenda time under Agenda with their blocks, and the open tasks without one under Anytime today.

#### Scenario: Two timed tasks and untimed ones

- **Given** the desktop gave two tasks in today's My Day the blocks 10:00–12:00 and 12:15–12:45, the first has the steps "Step one", "Step two" and "Step three" with none ticked off, other tasks in today's My Day have no agenda time, and it is 10:30
- **When** the phone opens Today after its next pull
- **Then** the Now card shows the first task reading "Now · until 12:00", "Step 1 of 3" and "Next: Step one", Agenda shows the second task with "12:15–12:45", and the untimed tasks are under Anytime today

### Requirement: Between blocks the card shows the next block

```meta
type: requirement
status: draft
```

The phone's Today SHALL show the next open timed task to start as the card, labelled with its start time, when no open task's block contains the current time.

#### Scenario: The Now task is ticked off

- **Given** Today shows the task of block 10:00–12:00 as the Now card, a task with the block 12:15–12:45 under Agenda, and it is 10:30
- **When** the reader ticks the Now task off
- **Then** the card shows the second task, reading "Next · 12:15"

### Requirement: A tick on the phone is the desktop's tick

```meta
type: requirement
status: draft
```

The system SHALL write a tick or an untick the phone makes on a task in today's My Day the way the desktop's checkbox writes it, so an untick clears the tick and leaves the task's status as it is.

#### Scenario: Ticking a task off

- **Given** a task under Anytime today on the phone
- **When** the reader ticks it off on the phone and the desktop syncs
- **Then** the phone shows it under Done today and the desktop shows it ticked off under Completed

#### Scenario: Unticking a task

- **Given** a task the reader ticked off on the phone today, shown under Done today
- **When** the reader unticks it on the phone and the desktop syncs
- **Then** the phone shows it under Anytime today again and the desktop shows it open and unticked, with the status the tick left it in

### Requirement: A step ticked on the phone ticks the task's sub-item

```meta
type: requirement
status: draft
```

The system SHALL write a step the phone ticks off, from the task's own page or from Focus, as the status of that step's Sub-Item on the task.

#### Scenario: From the task's page

- **Given** the phone shows the page of a task in today's My Day with three steps, reading "0 of 3"
- **When** the reader ticks off the first step
- **Then** the page reads "1 of 3" and the first step is ticked off

#### Scenario: From Focus

- **Given** Focus is open on that task, reading "Step 2 of 3 · now"
- **When** the reader presses Step done — next
- **Then** Focus reads "Step 3 of 3 · now"

#### Scenario: On the desktop

- **Given** the reader ticked off two of the task's three steps on the phone
- **When** the desktop syncs
- **Then** the desktop shows the task's steps as 2 of 3

### Requirement: Move to tomorrow takes the task out of today's My Day

```meta
type: requirement
status: draft
```

The system SHALL take a task the phone moves to tomorrow out of today's My Day on the phone and on the desktop.

#### Scenario: Moving a task from its page

- **Given** the phone shows the page of an open task in today's My Day
- **When** the reader presses Move to tomorrow and the desktop syncs
- **Then** the phone is back on Today without the task, and the desktop leaves it out of today's My Day and offers Add to My Day on it again

### Requirement: Focus counts down to the end of the block

```meta
type: requirement
status: draft
```

Focus SHALL count down every second to the end of the task's block, with the first step not yet ticked off shown as the current step.

#### Scenario: Opening Focus during the block

- **Given** a task in today's My Day with the block 10:00–12:00 and three steps, none ticked off, and it is 11:31:20
- **When** the reader opens Focus on it
- **Then** the ring reads 28:40 and moves on every second, and the current step reads "Step 1 of 3 · now"

### Requirement: Focus completes the task once its steps are done

```meta
type: requirement
status: draft
```

Focus SHALL offer Complete task once every step of the task is ticked off, a press that ticks the task off and returns to Today.

#### Scenario: The last step

- **Given** Focus is open on a task with three steps, two of them ticked off
- **When** the reader presses Step done — next
- **Then** Focus reads "All 3 steps done" and offers Complete task

#### Scenario: Completing the task

- **Given** Focus offers Complete task on a task with all three steps ticked off
- **When** the reader presses Complete task and the desktop syncs
- **Then** the phone is on Today with the task under Done today, and the desktop shows it ticked off with its steps at 3 of 3
