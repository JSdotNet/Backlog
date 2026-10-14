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

### Requirement: A repeating task ticked on the phone gets one next occurrence

```meta
type: requirement
status: draft
```

The desktop SHALL give a repeating task ticked off on the phone exactly one next occurrence, as its own tick does, however often the tick is pulled and however many desktops pull it.

#### Scenario: Ticking a repeating task off

- **Given** a task in today's My Day that repeats weekly and falls due today
- **When** the reader ticks it off on the phone and the desktop syncs
- **Then** the desktop shows it ticked off and holds one new occurrence falling due a week later, which reaches the phone on the desktop's next sync

#### Scenario: Pulling the tick again

- **Given** the desktop has given the phone-ticked repeating task its next occurrence
- **When** the desktop pulls the same documents again, or a second desktop pulls the tick
- **Then** each desktop still holds one next occurrence, and both hold the same one

#### Scenario: Unticking after the next occurrence exists

- **Given** the desktop has given the phone-ticked repeating task its next occurrence
- **When** the reader unticks the task on the phone and the desktop syncs
- **Then** the desktop shows the task unticked and keeps the next occurrence

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

## Linked tasks

```meta
type: requirements
status: proposed
related: [.devbook/domain/tasks/features.md#linked-tasks, .devbook/domain/tasks/domain.md#connected-target, .devbook/domain/tasks/domain.md#source-state, .devbook/arc42/adr/0020-external-items-arrive-as-linked-tasks.md]
```

> Where a connected target's tasks are filed and which status each one gets.
> The feature chapter says what linked tasks are; ADR 0020 §4 and §9 say why
> the map and the repository are per target.

### Requirement: A target's repository files its unfiled tasks

```meta
type: requirement
status: proposed
```

The sync SHALL file a target's new linked tasks, and its tasks filed under no repository, under the repository set on that target.

#### Scenario: A GitHub repository with another repository set

- **Given** the connected GitHub repository `JSdotNet/Backlog-issues` with the repository `JSdotNet/Backlog` set, and a new issue in it
- **When** the sync runs
- **Then** the issue's task is filed under `JSdotNet/Backlog`

#### Scenario: A spec-manager product with a repository

- **Given** a connected spec-manager product with the repository `JSdotNet/Backlog` set, and a new item in the product
- **When** the sync runs
- **Then** the item's task is filed under `JSdotNet/Backlog`

#### Scenario: A task brought in before the repository was set

- **Given** a linked task of a spec-manager product, filed under no repository, and the person then sets the repository `JSdotNet/Backlog` on the product
- **When** the sync runs
- **Then** the task is filed under `JSdotNet/Backlog`

### Requirement: A task already filed keeps its repository

```meta
type: requirement
status: proposed
```

The sync SHALL leave a linked task that is filed under a repository under that repository, whatever repository its target names.

#### Scenario: The person filed the task elsewhere

- **Given** a linked task the person filed under `JSdotNet/Website`, and its target has the repository `JSdotNet/Backlog` set
- **When** the sync runs
- **Then** the task is still filed under `JSdotNet/Website`

### Requirement: Without a repository the target decides

```meta
type: requirement
status: proposed
```

The sync SHALL file the linked tasks of a target with no repository set under the target itself when the target is a repository, and under no repository otherwise.

#### Scenario: A GitHub repository

- **Given** the connected GitHub repository `JSdotNet/Backlog` with no repository set, and a new issue in it
- **When** the sync runs
- **Then** the issue's task is filed under `JSdotNet/Backlog`

#### Scenario: A spec-manager product

- **Given** a connected spec-manager product with no repository set, and a new item in it
- **When** the sync runs
- **Then** the item's task is filed under no repository

### Requirement: A mapped status name gives its mapped status

```meta
type: requirement
status: proposed
```

The sync SHALL give a linked task the task status its target's status map holds for the item's source status name, compared without regard to case.

#### Scenario: A refined item on a product with no work status

- **Given** a spec-manager product whose status "Verfijnd" normalises to `Active`, and its status map gives "verfijnd" the status Ready
- **When** the sync brings in a new item in "Verfijnd"
- **Then** the item's task is Ready, not In progress

### Requirement: An unmapped status name takes the fixed map

```meta
type: requirement
status: proposed
```

The sync SHALL give a linked task whose source status name its target's status map does not hold the status the fixed map gives the item's normalised state.

#### Scenario: A status the person left on Default

- **Given** a spec-manager product whose status map holds "Verfijnd" only, and its status "In ontwikkeling" normalises to `Active`
- **When** the sync brings in a new item in "In ontwikkeling"
- **Then** the item's task is In progress

### Requirement: A dropped item is archived whatever the map holds

```meta
type: requirement
status: proposed
```

The sync SHALL archive the task of a dropped item, whatever its target's status map holds for the item's source status name.

#### Scenario: An archived item keeps its last status name

- **Given** a linked task whose target's status map gives "Verfijnd" the status Ready, and the item is archived at the source while still named "Verfijnd"
- **When** the sync runs
- **Then** the task is Archived

### Requirement: A move the map tells apart moves the task

```meta
type: requirement
status: proposed
```

The sync SHALL move a linked task when its item moves between two source statuses that its target's status map resolves to different task statuses, even when both normalise to the same state.

#### Scenario: From refined to in development

- **Given** a Ready linked task whose item is in "Verfijnd", its target's status map gives "Verfijnd" the status Ready and holds nothing for "In ontwikkeling", and both statuses normalise to `Active`
- **When** the item moves to "In ontwikkeling" and the sync runs
- **Then** the task is In progress

### Requirement: An edit to the status map moves no task

```meta
type: requirement
status: proposed
```

The sync SHALL leave the status of a linked task whose item has not moved at the source as it is after the person edits its target's status map.

#### Scenario: Mapping a status that tasks are already in

- **Given** an In progress linked task whose item is in "Verfijnd", and the person maps "Verfijnd" to Ready
- **When** the sync runs and the item has not moved
- **Then** the task is still In progress

### Requirement: A Done the map gives follows the source

```meta
type: requirement
status: proposed
```

The sync SHALL treat a Done that its target's status map gives as the source's Done, so the task is not flagged as finished locally and follows its item's next move at the source.

#### Scenario: A status mapped to Done, then reopened

- **Given** a linked task that is Done because its target's status map gives the item's status "Opgeleverd" the status Done
- **When** the item moves back to "In ontwikkeling" at the source and the sync runs
- **Then** the task is In progress and carries no done-locally flag

### Requirement: A task finished here stays Done

```meta
type: requirement
status: proposed
```

The sync SHALL leave a linked task the person finished here Done, and flag that it is done here and open at the source, while its target's status map gives its item's source status anything but Done.

#### Scenario: Finished here while the item is refined

- **Given** a linked task whose target's status map gives "Verfijnd" the status Ready, its item is in "Verfijnd", and the person marks the task Done
- **When** the sync runs
- **Then** the task is still Done and is flagged as done here and open at the source
