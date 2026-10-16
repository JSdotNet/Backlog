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
related: [.devbook/domain/tasks/features.md#my-day, .devbook/domain/tasks/context.md#clear-my-day-overnight, .devbook/arc42/adr/0032-my-day-preferences-ride-the-task-feed.md]
```

> The requirements of My Day on the desktop and the phone, and how the phone's
> changes reach the desktop. The feature chapter says what My Day is and why;
> this says what it promises. A carried task is defined under
> [My Day](domain.md#my-day).

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

### Requirement: An unfinished task carries over when My Day is kept

```meta
type: requirement
status: draft
```

With Clear My Day overnight off, the system SHALL keep an unfinished task picked for an earlier day in today's My Day on every paired device.

#### Scenario: Yesterday's open task on both heads

- **Given** Clear My Day overnight is off and an open task was picked for yesterday
- **When** the desktop opens the Tasks pane and the phone opens Today after its next pull
- **Then** the desktop counts the task in My Day and shows it under the My Day filter, and the phone shows it under Anytime today

#### Scenario: The same task with the switch on

- **Given** Clear My Day overnight is on and an open task was picked for yesterday
- **When** the desktop opens the Tasks pane and the phone opens Today after its next pull
- **Then** neither device shows the task in today's My Day

#### Scenario: Taking a carried task out

- **Given** Clear My Day overnight is off and a carried task is in today's My Day on the desktop
- **When** the reader chooses Remove from My Day on it
- **Then** the task leaves My Day, its `myday:` date is cleared, and no other task changes

#### Scenario: Finishing a carried task on the phone

- **Given** Clear My Day overnight is off and the phone shows the page of a carried task
- **When** the reader presses Mark done
- **Then** the phone is on Today with the task under Done today

### Requirement: A carried task ticked off today stays until the day ends

```meta
type: requirement
status: draft
```

With Clear My Day overnight off, the system SHALL list a task picked for an earlier day under Done today when it was ticked off today, and leave it out when it was ticked off on an earlier day.

#### Scenario: Ticked off today

- **Given** Clear My Day overnight is off and a task picked for two days ago was ticked off this morning
- **When** the reader opens Today on the phone
- **Then** the task is under Done today

#### Scenario: Ticked off yesterday

- **Given** Clear My Day overnight is off and a task picked for two days ago was ticked off yesterday
- **When** the reader opens Today on the phone or the Tasks pane on the desktop
- **Then** the task is not in today's My Day

### Requirement: A task picked for a later day never shows today

```meta
type: requirement
status: draft
```

The system SHALL leave a task picked for a later day out of today's My Day, whatever Clear My Day overnight says.

#### Scenario: Moved to tomorrow with the switch off

- **Given** Clear My Day overnight is off and the phone shows the page of an open task in today's My Day
- **When** the reader presses Move to tomorrow and the desktop syncs
- **Then** neither device shows the task in today's My Day until tomorrow

### Requirement: A carried task shows no agenda time

```meta
type: requirement
status: draft
```

The system SHALL show no agenda time for a carried task, on the desktop or the phone.

#### Scenario: Yesterday's block

- **Given** Clear My Day overnight is off and an open task was picked for yesterday with the block 10:00–11:00
- **When** the phone opens Today and the desktop shows the task's row and detail pane
- **Then** the phone shows the task under Anytime today without a block and offers no Focus, and the desktop row shows no block and the detail pane shows no agenda control

### Requirement: A change to the switch reaches every paired device

```meta
type: requirement
status: draft
```

The system SHALL bring a change to Clear My Day overnight made on one desktop to every paired device on that device's next pull.

#### Scenario: Turning it off on one desktop

- **Given** two paired desktops and a phone, all reading Clear My Day overnight as on
- **When** the reader turns it off on the first desktop and the other devices pull
- **Then** the second desktop reads it as off and all three devices list the same My Day

#### Scenario: Two flips between syncs

- **Given** the reader turns the switch off on the first desktop and later turns it on on the second, before either syncs
- **When** both desktops and the phone sync
- **Then** every device reads the switch as on, the later choice

### Requirement: A device that never set the switch reads it as on

```meta
type: requirement
status: draft
```

A device that never set Clear My Day overnight SHALL read it as on and send nothing for it.

#### Scenario: A newly paired desktop

- **Given** a desktop has Clear My Day overnight off and a second desktop that never set it is paired
- **When** the second desktop syncs
- **Then** the second desktop sends no choice of its own and reads the switch as off afterwards

#### Scenario: A phone before any choice arrives

- **Given** no desktop has ever set Clear My Day overnight and an open task was picked for yesterday
- **When** the phone opens Today
- **Then** the task is not in today's My Day

## Linked tasks

```meta
type: requirements
status: proposed
related: [.devbook/domain/tasks/features.md#linked-tasks, .devbook/domain/tasks/domain.md#connected-target, .devbook/domain/tasks/domain.md#source-state, .devbook/arc42/adr/0020-external-items-arrive-as-linked-tasks.md]
```

> Where a connected target's tasks are filed, which status each one gets, and
> which deleted tasks come back. The feature chapter says what linked tasks
> are; ADR 0020 §4 and §9 say why the map and the repository are per target,
> and §5 says when a deleted task is restored.

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

### Requirement: A deleted linked task is never made again

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.A_deleted_linked_task_is_never_made_again, unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.DeleteDisconnectedLinkedTasksTests.A_task_brought_back_and_then_deleted_on_its_own_stays_deleted]
```

A sync SHALL NOT create again a linked task the person deleted on its own.

#### Scenario: The person deletes a linked task

- **Given** a linked task for an open GitHub issue, which the person deleted
- **When** the target syncs again
- **Then** the backlog holds no task for that issue

#### Scenario: A restored task deleted on its own

- **Given** a linked task deleted with its source and restored by the sync of the target connected again
- **When** the person deletes that task and the target syncs again
- **Then** the backlog holds no task for that item

### Requirement: Reconnecting a target restores the tasks deleted with it

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.DeleteDisconnectedLinkedTasksTests.Connecting_the_source_again_brings_the_deleted_tasks_back, unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.A_linked_task_deleted_with_its_source_is_restored_and_follows_the_source_again, unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.A_linked_task_deleted_with_its_source_is_restored_past_the_first_sync_cutoff]
```

A sync of a connected target SHALL restore each task that Delete tasks deleted with that target and whose item it fetches, and SHALL update it from the item.

#### Scenario: The target is connected again

- **Given** a disconnected GitHub repository whose tasks the person deleted with Delete tasks, and whose issues are still open
- **When** the person connects the repository again and it syncs
- **Then** each of those tasks is live again, follows its issue, and is counted as restored

#### Scenario: The item is older than the first sync's cut-off

- **Given** a task deleted with its source whose item has not changed for longer than the first sync skips
- **When** the target is connected again and syncs for the first time
- **Then** the task is restored

### Requirement: Restore deleted tasks restores every deleted task of the target

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.RestoreDeletedLinkedTasksTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.TaskConnectorSettingsTests.Confirming_restores_the_tasks_says_how_many_and_takes_the_offer_away, unit:dotnet:Backlog.Desktop.UI.UnitTests.TaskConnectorSettingsTests.Cancelling_the_restore_confirmation_restores_nothing]
```

After the person confirms, Restore deleted tasks SHALL restore every deleted linked task of one connected target, the tasks deleted one by one included, and no other task.

#### Scenario: The person restores a target's deleted tasks

- **Given** a connected spec-manager product with three deleted tasks, one of them deleted on its own, and a deleted task of another product
- **When** the person chooses Restore deleted tasks on the product's card and confirms
- **Then** the three tasks are live again, and the other product's task stays deleted

#### Scenario: The person cancels

- **Given** a connected target with deleted tasks
- **When** the person chooses Restore deleted tasks and cancels the confirmation
- **Then** every one of those tasks stays deleted

#### Scenario: The target was disconnected meanwhile

- **Given** a target whose card offers Restore deleted tasks
- **When** the target is disconnected and the person then confirms the restore
- **Then** the restore is refused and nothing is restored

### Requirement: The assignee follows the source

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.The_source_overwrites_the_title_the_state_and_the_assignee
```

Each sync SHALL set a linked task's assignee to the one the source names.

#### Scenario: The issue is assigned

- **Given** a linked task whose issue has no assignee
- **When** the issue is assigned to Job and the target syncs
- **Then** the linked task names Job as its assignee

### Requirement: The title follows the source until the person renames it

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.The_source_overwrites_the_title_the_state_and_the_assignee, unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.A_title_renamed_locally_survives_a_changed_source_title, unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.A_target_whose_title_does_not_follow_the_source_keeps_the_local_title]
```

Each sync SHALL set a linked task's title to the source's, unless the person renamed the task or its Connected Target is set to keep the local title.

#### Scenario: Renamed at the source

- **Given** a linked task whose title nobody changed in Backlog
- **When** the issue is renamed and the target syncs
- **Then** the task has the issue's new title

#### Scenario: Renamed by the person first

- **Given** a linked task the person renamed to "My own name for it"
- **When** the issue is renamed and the target syncs
- **Then** the task is still called "My own name for it"

### Requirement: The status follows the source's state moves

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.A_state_change_at_the_source_moves_a_task_the_person_had_moved, unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.A_task_the_person_started_stays_started_while_the_source_still_says_open]
```

A sync SHALL move a linked task's status to the one its item's source status gives, through its target's status map or else the fixed map of its [Source State](domain.md#source-state), when the item moved at the source since the last sync, and otherwise leave the status where the person put it.

#### Scenario: The person starts the task

- **Given** a linked task for an open issue, which the person moved to `in_progress`
- **When** the target syncs and the issue is still open
- **Then** the task stays `in_progress`

#### Scenario: The source moves the item

- **Given** that task `in_progress`
- **When** the issue is closed and the target syncs
- **Then** the task is `done`

### Requirement: The sync walks the status along the lifecycle

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.The_walk_is_the_shortest_path_along_the_lifecycle, unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.A_closed_item_walks_its_ready_task_to_done_and_ticks_it_without_a_start]
```

A sync SHALL reach a linked task's new status along the shortest path of the [Task lifecycle](flow.md#task-lifecycle)'s own edges, without stamping a start for a status it only passes through.

#### Scenario: A ready task's issue is closed

- **Given** a `ready` linked task with no start date
- **When** its issue is closed and the target syncs
- **Then** the task is `done` and ticked off, having passed through `in_progress`, and it still has no start date

### Requirement: What arrives with an item is copied once

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.The_body_labels_effort_and_due_date_are_copied_once
```

The system SHALL copy an item's body, labels, effort and due date onto its linked task when the task is created, and SHALL NOT change them on later syncs.

#### Scenario: The source rewrites the body

- **Given** a linked task whose body the person added notes to
- **When** the issue's body, labels, effort and due date change and the target syncs
- **Then** the task keeps its body with the notes, and its first labels, effort and due date

### Requirement: Two devices make one task for one item

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.Two_devices_syncing_the_same_items_make_the_same_tasks, unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.LinkedTaskIdsTests.The_same_item_always_gives_the_same_id]
```

The system SHALL give the linked task for one item the same id on every device that syncs the item, so the person's devices hold one task for it.

#### Scenario: Both machines sync the same repository

- **Given** two paired devices that both connected the same repository
- **When** both sync it
- **Then** each issue is one task on both devices

### Requirement: An item that leaves the source archives its task

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.A_vanished_item_archives_and_flags_its_task_and_does_not_delete_it
```

A sync SHALL archive and flag the linked task of an item the source no longer returns, and SHALL NOT delete it.

#### Scenario: The issue is gone

- **Given** a `ready` linked task whose issue no longer appears at the source
- **When** the target syncs
- **Then** the task is `archived`, flagged as gone from the source, and still in the backlog

### Requirement: Only the first plan label files the task

```meta
type: requirement
status: draft
related: [.devbook/domain/roadmap/domain.md#source-label]
tests: unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.Only_the_first_plan_label_becomes_the_plan_tag_and_the_task_is_flagged
```

A sync SHALL make only an item's first `+` label the linked task's plan tag, and SHALL flag the task when the item carries more than one.

#### Scenario: An issue carries two plan labels

- **Given** an issue labelled `+alpha`, `bug` and `+beta`
- **When** the target syncs
- **Then** the linked task carries the tags `+alpha` and `bug`, and is flagged for several plan labels

### Requirement: A sync never creates a Roadmap Item

```meta
type: requirement
status: draft
related: [.devbook/domain/roadmap/domain.md#roadmap-tag]
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.The_sync_is_given_nothing_that_can_create_a_roadmap_item, unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.A_plan_label_files_the_task_under_its_plan_and_a_task_without_one_under_none]
```

A sync SHALL file a linked task under the plan its plan label names, and SHALL NOT create a Roadmap Item for that plan.
The roadmap changes only through a person's gesture (local ADR 0013, ruling 3).

#### Scenario: A plan label names a plan the roadmap does not have

- **Given** no Roadmap Item tagged `offline-sync`
- **When** an issue labelled `+Offline Sync` syncs
- **Then** its linked task is filed under `+offline-sync`, and the roadmap still has no item for it

## Projection

```meta
type: requirements
status: draft
related: [.devbook/domain/tasks/features.md#projection]
```

> The requirements of the automatic Projection policy, which is not built yet.
> The feature chapter says what Projection is and why; this says what the policy
> will promise.

### Requirement: Starting work opens one artifact per target repository

```meta
type: requirement
status: draft
related: [.devbook/domain/tasks/domain.md#projection]
```

When a task moves from `ready` to `in_progress`, the Projection policy SHALL open one downstream artifact, a GitHub issue or a Copilot CLI task, for each `repo_id` the task targets, and record each as a Projection Ref on the task.

#### Scenario: A task that targets two repositories

- **Given** a `ready` task that targets the repositories `backlog` and `fincent` and holds no Projection Ref
- **When** the task moves to `in_progress`
- **Then** one GitHub issue is open in `backlog` and one in `fincent`, and the task holds a Projection Ref to each of them

### Requirement: Completing a task closes its artifacts

```meta
type: requirement
status: draft
related: [.devbook/domain/tasks/domain.md#projection]
```

When a task is completed, the Projection policy SHALL close every downstream artifact the task's Projection Refs name.

#### Scenario: A task projected into two repositories is ticked off

- **Given** an `in_progress` task holding Projection Refs to an open GitHub issue in `backlog` and one in `fincent`
- **When** the person ticks the task off
- **Then** both issues are closed

## Sub-items and steps

```meta
type: requirements
status: draft
related: [.devbook/domain/tasks/features.md#sub-items-and-steps]
```

> The requirements of the task's steps and of the two readings of its body.

### Requirement: The chosen reading of the body survives a save

```meta
type: requirement
status: draft
tests: integration:dotnet:Backlog.Desktop.UI.UnitTests.EntryViewPreferenceRoundTripTests.An_entry_that_prefers_the_markdown_block_still_prefers_it_after_a_reload
```

The system SHALL keep the reading of a task's body a person last chose, the steps or the Markdown block, through every save of the task's text and every reload.

#### Scenario: The Markdown block is chosen and the task is saved

- **Given** a task with steps whose reader chose the Markdown block
- **When** the task's text is saved and the backlog is opened again
- **Then** the task opens on the Markdown block, and its metadata line still carries `view:notes`

## Recurring tasks

```meta
type: requirements
status: draft
related: [.devbook/domain/tasks/features.md#recurring-tasks, .devbook/domain/tasks/domain.md#occurrence-spawning]
```

> What [Occurrence Spawning](domain.md#occurrence-spawning) promises when a
> repeating task is ticked off, on the desktop or through a pulled copy.

### Requirement: The tick spawns the next occurrence

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.RecurringTaskTests.Reaching_Done_spawns_nothing_until_the_entry_is_ticked, unit:dotnet:Backlog.Desktop.UI.UnitTests.RecurrenceSuccessorInListTests.Reaching_Done_spawns_nothing_until_the_entry_is_ticked]
```

The system SHALL create the next occurrence of a repeating task when a save ticks it off, and not when its status reaches `done`.

#### Scenario: Done but not ticked

- **Given** a weekly task falling due on 21 August
- **When** its status is set to `done` and nobody ticks it
- **Then** no next occurrence exists

#### Scenario: Ticked after reaching done

- **Given** that task at `done` and unticked
- **When** the person ticks it off
- **Then** one new task exists, falling due on 28 August, and the ticked task stays `done`

### Requirement: The next occurrence carries the repeat and starts the occurrence fresh

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.RecurringTaskTests.A_successor_carries_the_repeat_and_leaves_the_occurrence_behind, unit:dotnet:Backlog.Modules.Tasks.UnitTests.RecurringTaskTests.A_successor_points_at_the_same_devbook_chapters, unit:dotnet:Backlog.Modules.Tasks.UnitTests.RecurringTaskTests.A_completed_repeat_with_no_due_date_spawns_an_undated_successor]
```

The system SHALL give the next occurrence what [Occurrence Spawning](domain.md#occurrence-spawning) lists as what the repeat is of, and start it at `ready` with what was about the finished occurrence left empty.

#### Scenario: A weekly review with a step, a reminder and a link

- **Given** a weekly task with one ticked step, a reminder, a My Day date, a dependency and a linked issue
- **When** the person ticks it off
- **Then** the next occurrence has the same title, priority, area, tags, dependency and step, the step is pending, and it has no reminder, no My Day date, no linked issue and no usage history

#### Scenario: A repeat with no due date

- **Given** a weekly task with no due date
- **When** the person ticks it off
- **Then** the next occurrence is `ready` and has no due date

### Requirement: A task that arrives ticked spawns nothing

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.RecurringTaskTests.An_entry_typed_in_as_already_done_spawns_nothing, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeRecurrenceTests.A_task_that_arrives_already_ticked_spawns_nothing]
```

The system SHALL create no next occurrence for a repeating task that is created already ticked, or that a pull brings to a device for the first time already ticked.

#### Scenario: A pulled task the desktop has never seen

- **Given** a desktop that has never held a weekly task
- **When** a pull brings it in already ticked off
- **Then** the desktop holds that task and no next occurrence of it

### Requirement: An occurrence has at most one successor

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.RecurringTaskTests.Completing_a_repeating_entry_creates_exactly_one_successor, unit:dotnet:Backlog.Modules.Tasks.UnitTests.RecurringTaskTests.Unticking_and_ticking_again_keeps_the_one_successor, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeRecurrenceTests.A_successor_already_pulled_is_not_spawned_again, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeRecurrenceTests.A_replayed_page_spawns_nothing_more]
```

The system SHALL give each occurrence of a repeating task at most one successor, whichever device and however many ticks spawn it.

#### Scenario: Unticked and ticked again

- **Given** a weekly task the person ticked off, which now has its next occurrence
- **When** the person unticks it and ticks it again
- **Then** the backlog still holds one next occurrence

#### Scenario: The successor already arrived from another desktop

- **Given** a desktop that pulled a weekly task's next occurrence from another desktop
- **When** it pulls the tick of that weekly task
- **Then** it creates no second next occurrence

### Requirement: A deleted successor is not spawned again

```meta
type: requirement
status: draft
```

The system SHALL create no next occurrence for a ticked occurrence whose successor the person deleted.

#### Scenario: The person stops a repeat

- **Given** a weekly task whose next occurrence the person deleted
- **When** the person unticks the weekly task and ticks it off again
- **Then** no next occurrence comes back

### Requirement: Unticking leaves the successor in place

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeRecurrenceTests.An_untick_after_the_spawn_leaves_the_successor_in_place, unit:dotnet:Backlog.Modules.Tasks.UnitTests.RecurringTaskTests.Unticking_and_ticking_again_keeps_the_one_successor]
```

The system SHALL keep the next occurrence of a repeating task when the occurrence it followed is unticked, on the desktop or through a pull.

#### Scenario: Unticked on the desktop

- **Given** a weekly task the person ticked off, which now has its next occurrence
- **When** the person unticks it
- **Then** the next occurrence is still in the backlog

## Manual ordering

```meta
type: requirements
status: draft
related: [.devbook/domain/tasks/features.md#manual-ordering]
```

> The requirements of the order a person arranges the backlog in.

### Requirement: Unranked tasks fall back to recency

```meta
type: requirement
status: draft
tests: integration:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteTaskRepositoryTests.Listing_puts_hand_ranked_tasks_first_and_falls_back_to_newest
```

The backlog SHALL list tasks by their hand-set rank, and tasks sharing a rank, which includes every task nobody ranked, newest first.

#### Scenario: Two ranked tasks and two unranked ones

- **Given** a task ranked 1, a task ranked 2, and two tasks nobody ranked, created a day apart
- **When** the backlog is listed
- **Then** the two unranked tasks come first, the newer one on top, followed by the tasks ranked 1 and 2

## Import

```meta
type: requirements
status: draft
related: [.devbook/domain/tasks/features.md#import, .devbook/domain/tasks/domain.md#entry-kind]
```

> The requirements of importing a plan, and of the paths that leave plans to it.

### Requirement: The text save refuses a plan entry

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.SaveTaskFromTextPlanEntryTests.A_plan_entry_is_refused_rather_than_saved_as_a_task, unit:dotnet:Backlog.Modules.Tasks.UnitTests.SaveTaskFromTextPlanEntryTests.An_update_is_refused_on_the_same_terms, unit:dotnet:Backlog.Modules.Tasks.UnitTests.SaveTaskFromTextPlanEntryTests.The_refusal_names_import_as_the_way_in]
```

The system SHALL refuse to save entry text of the plan [Entry Kind](domain.md#entry-kind) as a task, whether it creates a task or updates one, and SHALL name Import as the way to bring a plan in.

#### Scenario: A plan entry typed into the backlog

- **Given** entry text whose type word is `plan`
- **When** the person saves it as a task
- **Then** no task is stored, and the refusal names Import

## Archive and lifecycle

```meta
type: requirements
status: draft
related: [.devbook/domain/tasks/features.md#archive-and-lifecycle, .devbook/domain/tasks/domain.md#completed]
```

> What the list's checkbox and the status picker promise about the tick,
> [Completed](domain.md#completed), and the status.

### Requirement: Ticking an open task off moves it to done

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksDetailPaneTests.The_circle_moves_an_open_status_to_done, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksDetailPaneTests.The_circle_completes_the_entry_as_done_and_puts_it_back]
```

The list's checkbox SHALL tick off a task at `draft`, `ready` or `in_progress` and move it to `done` in the same save.

#### Scenario: Ticking a task in progress

- **Given** a task at `in_progress`
- **When** the person ticks its checkbox
- **Then** the task is ticked off and `done`

### Requirement: Ticking a finished task keeps its status

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksDetailPaneTests.An_end_state_status_does_not_tick_the_entry_or_move_it_under_completed
```

The list's checkbox SHALL tick off a task at `done` or `archived` and leave its status as it is.

#### Scenario: Ticking an archived task

- **Given** an unticked task at `archived`
- **When** the person ticks its checkbox
- **Then** the task is ticked off and still `archived`

### Requirement: Reaching done through the status picker does not tick

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksDetailPaneTests.An_end_state_status_does_not_tick_the_entry_or_move_it_under_completed
```

The system SHALL leave a task unticked, on the open list, when its status is moved to `done` or `archived` through the status picker.

#### Scenario: Marking work done

- **Given** an unticked task at `in_progress`
- **When** the person picks `done` as its status
- **Then** the task reads `done`, is still unticked, and stays on the open list

### Requirement: Unticking clears only the tick

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksDetailPaneTests.The_circle_completes_the_entry_as_done_and_puts_it_back
```

Unticking a task SHALL clear its `completed_on` and leave its status and every other field as they were.

#### Scenario: Unticking a finished task

- **Given** a task the person ticked off, now `done`
- **When** the person unticks it
- **Then** the task is back on the open list, unticked and still `done`

## Multi-device sync

```meta
type: requirements
status: draft
related: [.devbook/domain/tasks/features.md#multi-device-sync]
```

> What keeping one backlog on several devices promises.

### Requirement: The later edit wins whole

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeTests.Two_devices_converge_on_the_later_stamped_edit, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeTests.An_older_document_never_overwrites_a_newer_local_copy_even_one_already_pushed]
```

When the same task was edited on two devices, the system SHALL keep on both the copy with the later edit, whole, and SHALL NOT let an older copy overwrite a newer one.

#### Scenario: Two edits to one task

- **Given** a task renamed on the desktop at ten and renamed again on the laptop at eleven
- **When** both devices have synced
- **Then** both hold the laptop's copy of the task
