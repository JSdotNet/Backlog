# Requirements

```meta
type: requirements
related: [.devbook/domain/work/features.md]
```

> What each feature guarantees: one SHALL sentence per requirement, with its scenarios.

The scenarios that place a plan in time use the default working week: Monday to
Friday 09:00 to 17:30, 8.5 hours a day and 42.5 a week. Friday 9 October 2026 is in
week 41.

## My Day

```meta
type: requirements
related: [.devbook/domain/work/features.md#my-day, .devbook/domain/work/context.md#clear-my-day-overnight, .devbook/arc42/adr/0032-my-day-preferences-ride-the-task-feed.md, .devbook/arc42/adr/0072-the-tasks-pane-groups-by-plan-and-a-plan-is-queued-as-a-whole.md]
```

> A carried task is defined under [My Day](domain.md#my-day).

### Requirement: Today places each task by its agenda time

```meta
type: requirement
```

The phone's Today SHALL show today's open My Day task whose block contains the current time as the Now card, other open timed tasks with their blocks under Agenda, and untimed ones under Anytime today.

#### Scenario: Two timed tasks and untimed ones

- **Given** the desktop gave two tasks in today's My Day the blocks 10:00–12:00 and 12:15–12:45, the first has the steps "Step one", "Step two" and "Step three" with none ticked off, other tasks in today's My Day have no agenda time, and it is 10:30
- **When** the phone opens Today after its next pull
- **Then** the Now card shows the first task reading "Now · until 12:00", "Step 1 of 3" and "Next: Step one", Agenda shows the second task with "12:15–12:45", and the untimed tasks are under Anytime today

### Requirement: Between blocks the card shows the next block

```meta
type: requirement
```

The phone's Today SHALL show the next open timed task to start as the card, labelled with its start time, when no open task's block contains the current time.

#### Scenario: The Now task is ticked off

- **Given** Today shows the task of block 10:00–12:00 as the Now card, a task with the block 12:15–12:45 under Agenda, and it is 10:30
- **When** the reader ticks the Now task off
- **Then** the card shows the second task, reading "Next · 12:15"

### Requirement: A tick on the phone is the desktop's tick

```meta
type: requirement
```

The system SHALL write a phone tick or untick on a task in today's My Day the way the desktop's checkbox writes it, so an untick clears the tick and leaves its status alone.

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
```

The system SHALL take a task the phone moves to tomorrow out of today's My Day on the phone and on the desktop.

#### Scenario: Moving a task from its page

- **Given** the phone shows the page of an open task in today's My Day
- **When** the reader presses Move to tomorrow and the desktop syncs
- **Then** the phone is back on Today without the task, and the desktop leaves it out of today's My Day and offers Add to My Day on it again

### Requirement: Focus counts down to the end of the block

```meta
type: requirement
```

Focus SHALL count down every second to the end of the task's block, with the first step not yet ticked off shown as the current step.

#### Scenario: Opening Focus during the block

- **Given** a task in today's My Day with the block 10:00–12:00 and three steps, none ticked off, and it is 11:31:20
- **When** the reader opens Focus on it
- **Then** the ring reads 28:40 and moves on every second, and the current step reads "Step 1 of 3 · now"

### Requirement: Focus completes the task once its steps are done

```meta
type: requirement
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
```

With Clear My Day overnight off, the system SHALL list a task picked for an earlier day under Done today when it was ticked off today, and leave it out when ticked off before today.

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
```

The system SHALL leave a task picked for a later day out of today's My Day, whatever Clear My Day overnight says.

#### Scenario: Moved to tomorrow with the switch off

- **Given** Clear My Day overnight is off and the phone shows the page of an open task in today's My Day
- **When** the reader presses Move to tomorrow and the desktop syncs
- **Then** neither device shows the task in today's My Day until tomorrow

### Requirement: A carried task shows no agenda time

```meta
type: requirement
```

The system SHALL show no agenda time for a carried task, on the desktop or the phone.

#### Scenario: Yesterday's block

- **Given** Clear My Day overnight is off and an open task was picked for yesterday with the block 10:00–11:00
- **When** the phone opens Today and the desktop shows the task's row and detail pane
- **Then** the phone shows the task under Anytime today without a block and offers no Focus, and the desktop row shows no block and the detail pane shows no agenda control

### Requirement: A change to the switch reaches every paired device

```meta
type: requirement
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

### Requirement: A plan is in My Day by the task rule

```meta
type: requirement
related: [.devbook/domain/work/domain.plan.md#roadmap-item, .devbook/arc42/adr/0072-the-tasks-pane-groups-by-plan-and-a-plan-is-queued-as-a-whole.md]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanMyDayTests.A_plan_stamped_today_is_in_My_Day_and_one_on_no_day_or_a_later_day_is_not, unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanMyDayTests.With_Clear_My_Day_overnight_on_a_plan_picked_yesterday_has_left_My_Day, unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanMyDayTests.With_Clear_My_Day_overnight_off_an_unfinished_plan_carries_over_and_a_finished_one_does_not, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteRoadmapPlanReplicaTests.A_plans_my_day_date_travels_with_the_copy_and_a_copy_without_it_reads_as_on_none, unit:dotnet:Backlog.Mobile.UI.UnitTests.TodayPlanLineTests.A_plan_is_in_by_the_task_rule, unit:dotnet:Backlog.Mobile.UI.UnitTests.TaskViewProjectionMyDayTests.With_the_switch_off_a_plan_picked_yesterday_carries_over_until_it_is_finished]
```

The system SHALL read a plan as in today's My Day from its Roadmap Item's My Day date by the rule a task's date is read, under the same Clear My Day overnight setting.

#### Scenario: A plan picked today

- **Given** the person put the plan `+zero-trust-auth` on My Day today
- **When** the person opens My Day on the desktop
- **Then** the plan is listed among today's plans

#### Scenario: A plan picked yesterday with the switch on

- **Given** Clear My Day overnight is on and the plan `+finance-setup` was put on My Day yesterday with entries still open
- **When** the person opens My Day today
- **Then** the plan is not in today's My Day

#### Scenario: A plan picked yesterday with the switch off

- **Given** Clear My Day overnight is off and the plan `+finance-setup` was put on My Day yesterday with entries still open
- **When** the person opens My Day today
- **Then** the plan is in today's My Day as a carried plan

### Requirement: A plan tag with no Roadmap Item gets one on My Day

```meta
type: requirement
related: [.devbook/domain/work/domain.plan.md#roadmap-item]
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PutPlanOnMyDayTests.A_plan_tag_no_item_carries_gets_its_item_on_that_day, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapCrossContextAdapterScopeTests.Putting_a_new_plan_tag_on_My_Day_creates_its_roadmap_item_and_reads_the_date_back]
```

The system SHALL create a Roadmap Item carrying the tag and today's My Day date when the person puts a plan tag that no Roadmap Item carries on My Day.

#### Scenario: A tag only entries carry

- **Given** entries carry the plan tag `+lets-bbq-setup` and no Roadmap Item carries it
- **When** the person puts `+lets-bbq-setup` on My Day
- **Then** the roadmap holds an item with the tag `lets-bbq-setup`, today's My Day date and a one-day window on today, unfiled, and the plan is in today's My Day

### Requirement: A plan on My Day shows only what is left for the person

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanMyDayTests.My_Day_lists_Plans_today_above_the_tasks_with_only_the_entries_the_person_holds, unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanMyDayTests.An_entry_awaiting_validation_on_an_open_pull_request_is_the_persons_and_one_in_a_session_is_not, unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanMyDayTests.A_plan_today_words_its_run_state]
```

The desktop's My Day SHALL show a plan in it with its progress and run state and only those of its entries that are left for the person, held for the person, or awaiting validation.

#### Scenario: A running plan on My Day

- **Given** the plan `+zero-trust-auth` is on today's My Day and running, with 8 of 24 entries done, three in a plan session, one test entry left for the person and one entry held on an unreadable source
- **When** the person opens My Day on the desktop
- **Then** the plan shows "8 of 24" and "Running · unattended", and lists only the test entry and the held entry

### Requirement: The phone shows a plan on Today as one line

```meta
type: requirement
related: [.devbook/arc42/06-runtime-view.md#mobile-my-day-and-task-push]
tests: [unit:dotnet:Backlog.Mobile.UI.UnitTests.TodayScreenTests.A_plan_on_today_is_one_line_above_the_tasks_without_its_entries, unit:dotnet:Backlog.Mobile.UI.UnitTests.TodayScreenTests.A_day_with_only_a_plan_shows_the_plan_and_not_the_empty_state, unit:dotnet:Backlog.Mobile.UI.UnitTests.TodayPlanLineTests.The_run_state_reads_off_the_open_entries, unit:dotnet:Backlog.Mobile.UI.UnitTests.TaskViewProjectionMyDayTests.The_roadmap_plan_is_kept_with_its_page_and_puts_its_plans_on_Today]
```

The phone's Today SHALL show each plan in today's My Day as one line above the task groups, with its name, its entries done of its total and its run state, without its entries.

#### Scenario: A plan and tasks on Today

- **Given** the plan `+zero-trust-auth` and three tasks are in today's My Day
- **When** the phone opens Today after its next pull
- **Then** Today shows the plan as one line and none of its entries, and the tasks in their groups

#### Scenario: Only a plan on Today

- **Given** the plan `+lets-bbq` is in today's My Day and no task is
- **When** the phone opens Today after its next pull
- **Then** Today shows the plan's line and not the empty state

### Requirement: My Day offers no suggestions

```meta
type: requirement
```

My Day SHALL list only the tasks and plans the person picked for it, and SHALL suggest none.

#### Scenario: Ready work not picked

- **Given** several ready entries and plans, none picked for today
- **When** the person opens My Day
- **Then** My Day lists none of them and shows no suggestion group

## Linked tasks

```meta
type: requirements
related: [.devbook/domain/work/features.md#linked-tasks, .devbook/domain/work/domain.md#connected-target, .devbook/domain/work/domain.md#source-state, .devbook/arc42/adr/0020-external-items-arrive-as-linked-tasks.md]
```

> ADR 0020 §4 and §9 say why the map and the repository are per target, and §5 says when a deleted task is restored.

### Requirement: A target's repository files its unfiled tasks

```meta
type: requirement
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
```

The sync SHALL leave a linked task that is filed under a repository under that repository, whatever repository its target names.

#### Scenario: The person filed the task elsewhere

- **Given** a linked task the person filed under `JSdotNet/Website`, and its target has the repository `JSdotNet/Backlog` set
- **When** the sync runs
- **Then** the task is still filed under `JSdotNet/Website`

### Requirement: Without a repository the target decides

```meta
type: requirement
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
```

The sync SHALL give a linked task the task status its target's status map holds for the item's source status name, compared without regard to case.

#### Scenario: A refined item on a product with no work status

- **Given** a spec-manager product whose status "Verfijnd" normalises to `Active`, and its status map gives "verfijnd" the status Ready
- **When** the sync brings in a new item in "Verfijnd"
- **Then** the item's task is Ready, not In progress

### Requirement: An unmapped status name takes the fixed map

```meta
type: requirement
```

The sync SHALL give a linked task whose source status name its target's status map does not hold the status the fixed map gives the item's normalised state.

#### Scenario: A status the person left on Default

- **Given** a spec-manager product whose status map holds "Verfijnd" only, and its status "In ontwikkeling" normalises to `Active`
- **When** the sync brings in a new item in "In ontwikkeling"
- **Then** the item's task is In progress

### Requirement: A dropped item is archived whatever the map holds

```meta
type: requirement
```

The sync SHALL archive the task of a dropped item, whatever its target's status map holds for the item's source status name.

#### Scenario: An archived item keeps its last status name

- **Given** a linked task whose target's status map gives "Verfijnd" the status Ready, and the item is archived at the source while still named "Verfijnd"
- **When** the sync runs
- **Then** the task is Archived

### Requirement: A move the map tells apart moves the task

```meta
type: requirement
```

The sync SHALL move a linked task when its item moves between two source statuses that its target's status map resolves to different task statuses, even when both normalise to the same state.

#### Scenario: From refined to in development

- **Given** a Ready linked task whose item is in "Verfijnd", its target's status map gives "Verfijnd" the status Ready and holds nothing for "In ontwikkeling", and both statuses normalise to `Active`
- **When** the item moves to "In ontwikkeling" and the sync runs
- **Then** the task is In progress

### Requirement: An edit to the status map moves no task

```meta
type: requirement
```

The sync SHALL leave the status of a linked task whose item has not moved at the source as it is after the person edits its target's status map.

#### Scenario: Mapping a status that tasks are already in

- **Given** an In progress linked task whose item is in "Verfijnd", and the person maps "Verfijnd" to Ready
- **When** the sync runs and the item has not moved
- **Then** the task is still In progress

### Requirement: A Done the map gives follows the source

```meta
type: requirement
```

The sync SHALL treat a Done that its target's status map gives as the source's Done, so the task is not flagged as finished locally and follows its item's next move at the source.

#### Scenario: A status mapped to Done, then reopened

- **Given** a linked task that is Done because its target's status map gives the item's status "Opgeleverd" the status Done
- **When** the item moves back to "In ontwikkeling" at the source and the sync runs
- **Then** the task is In progress and carries no done-locally flag

### Requirement: A task finished here stays Done

```meta
type: requirement
```

The sync SHALL leave a linked task the person finished here Done, flagging it done here and open at the source, while its target's status map gives its item's source status anything but Done.

#### Scenario: Finished here while the item is refined

- **Given** a linked task whose target's status map gives "Verfijnd" the status Ready, its item is in "Verfijnd", and the person marks the task Done
- **When** the sync runs
- **Then** the task is still Done and is flagged as done here and open at the source

### Requirement: A deleted linked task is never made again

```meta
type: requirement
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
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.The_walk_is_the_shortest_path_along_the_lifecycle, unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.A_closed_item_walks_its_ready_task_to_done_and_ticks_it_without_a_start]
```

A sync SHALL reach a linked task's new status along the shortest path of the [Task lifecycle](domain.task.md#task)'s own edges, without stamping a start for a status it only passes through.

#### Scenario: A ready task's issue is closed

- **Given** a `ready` linked task with no start date
- **When** its issue is closed and the target syncs
- **Then** the task is `done` and ticked off, having passed through `in_progress`, and it still has no start date

### Requirement: What arrives with an item is copied once

```meta
type: requirement
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
related: [.devbook/domain/work/domain.md#source-label]
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
related: [.devbook/domain/work/domain.plan.md#roadmap-tag]
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
related: [.devbook/domain/work/features.md#projection]
```

> The policy is not built yet.

### Requirement: Starting work opens one artifact per target repository

```meta
type: requirement
related: [.devbook/domain/work/domain.md#projection]
```

When a task moves from `ready` to `in_progress`, the Projection policy SHALL open one GitHub issue or Copilot CLI task for each `repo_id` the task targets and record each as a Projection Ref.

#### Scenario: A task that targets two repositories

- **Given** a `ready` task that targets the repositories `backlog` and `fincent` and holds no Projection Ref
- **When** the task moves to `in_progress`
- **Then** one GitHub issue is open in `backlog` and one in `fincent`, and the task holds a Projection Ref to each of them

### Requirement: Completing a task closes its artifacts

```meta
type: requirement
related: [.devbook/domain/work/domain.md#projection]
```

When a task is completed, the Projection policy SHALL close every downstream artifact the task's Projection Refs name.

#### Scenario: A task projected into two repositories is ticked off

- **Given** an `in_progress` task holding Projection Refs to an open GitHub issue in `backlog` and one in `fincent`
- **When** the person ticks the task off
- **Then** both issues are closed

## Sub-items and steps

```meta
type: requirements
related: [.devbook/domain/work/features.md#sub-items-and-steps]
```

### Requirement: A step is written in the task's body

```meta
type: requirement
tests: [integration:dotnet:Backlog.Desktop.UI.UnitTests.TasksDetailPaneTests.The_markdown_block_writes_the_whole_body_and_leaves_the_metadata_line_alone, integration:dotnet:Backlog.Desktop.UI.UnitTests.TasksDetailPaneTests.The_body_is_the_markdown_editor_alone, e2e:dotnet:Backlog.EndToEndTests.ConferenceDayTests]
```

On the desktop, the system SHALL read each `##` chapter written in a task's body as one of its steps, so a step is added, renamed, reordered, ticked and removed by editing that text.

#### Scenario: A step is typed into the body

- **Given** an open task with prose and no steps
- **When** the person types `## Check the totals` at the end of its body
- **Then** the task has one pending step titled "Check the totals"

### Requirement: A view token on the line survives a save

```meta
type: requirement
tests: [integration:dotnet:Backlog.Desktop.UI.UnitTests.EntryViewPreferenceRoundTripTests.An_entry_that_prefers_the_markdown_block_still_prefers_it_after_a_reload, integration:dotnet:Backlog.Desktop.UI.UnitTests.EntryViewPreferenceRoundTripTests.An_entry_with_no_preference_is_not_given_one_by_being_saved, integration:dotnet:Backlog.Desktop.UI.UnitTests.TasksDetailPaneTests.The_body_is_the_markdown_editor_alone]
```

The system SHALL keep a `view:` token found on a task's metadata line, as written, through every save of the task's text and every reload, and write none onto a task that carries none.

#### Scenario: A task carrying view:notes is saved

- **Given** a task with steps whose metadata line carries `view:notes`
- **When** the task's text is saved and the backlog is opened again
- **Then** its metadata line still carries `view:notes`, and its body opens in the Markdown editor as every task's does

#### Scenario: A task with no token is saved

- **Given** a task whose metadata line carries no `view:` token
- **When** the task's text is saved and the backlog is opened again
- **Then** its metadata line still carries none

## Recurring tasks

```meta
type: requirements
related: [.devbook/domain/work/features.md#recurring-tasks, .devbook/domain/work/domain.md#occurrence-spawning]
```

### Requirement: The tick spawns the next occurrence

```meta
type: requirement
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
```

The system SHALL create no next occurrence for a ticked occurrence whose successor the person deleted.

#### Scenario: The person stops a repeat

- **Given** a weekly task whose next occurrence the person deleted
- **When** the person unticks the weekly task and ticks it off again
- **Then** no next occurrence comes back

### Requirement: Unticking leaves the successor in place

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeRecurrenceTests.An_untick_after_the_spawn_leaves_the_successor_in_place, unit:dotnet:Backlog.Modules.Tasks.UnitTests.RecurringTaskTests.Unticking_and_ticking_again_keeps_the_one_successor]
```

The system SHALL keep the next occurrence of a repeating task when the occurrence it followed is unticked, on the desktop or through a pull.

#### Scenario: Unticked on the desktop

- **Given** a weekly task the person ticked off, which now has its next occurrence
- **When** the person unticks it
- **Then** the next occurrence is still in the backlog

## Plans view

```meta
type: requirements
related: [.devbook/domain/work/features.md#plans-view, .devbook/arc42/adr/0072-the-tasks-pane-groups-by-plan-and-a-plan-is-queued-as-a-whole.md]
```

> Local ADR 0072 decides these requirements. The switch and the plan rail are built; the rest is not yet.

### Requirement: The Tasks list opens on Plans and remembers the switch

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.The_list_opens_on_Plans_the_first_time_and_reopens_on_the_grouping_last_picked, unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanRailTests.The_switch_reads_Plans_and_picking_Flat_takes_the_rail_away_and_tells_the_host]
```

The Tasks list SHALL open on Plans in a window that never chose, and SHALL reopen each window on the Plans or Flat choice it last made.

#### Scenario: The first open

- **Given** a window that has never chosen Plans or Flat
- **When** the person opens Tasks
- **Then** the Plans view is shown

#### Scenario: Flat is remembered

- **Given** the person switched a window to Flat
- **When** the app is restarted and the window opens Tasks again
- **Then** the Flat view is shown

### Requirement: The plan rail lists each plan with its progress and run state

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanRailTests.The_rail_lists_My_Day_Now_each_plan_and_Unplanned_in_order_with_their_counts, unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanRailTests.Each_plan_carries_its_done_and_total_a_progress_line_and_its_run_state_as_coloured_text]
```

The Plans view SHALL list on its rail My Day, Now across plans, each plan with a progress line, its done and total entry counts and its run state as text, and Unplanned with its count, in that order.

#### Scenario: Plans in view

- **Given** the plan `+zero-trust-auth` has 8 of 24 entries done and a running plan session, the plan `+gtd-para` is queued second, and 172 open entries carry no plan
- **When** the person opens the Plans view
- **Then** the rail lists My Day, Now across plans, `zero-trust-auth` with "8/24" and "Running", `gtd-para` with "Queued 2nd", and Unplanned with 172

### Requirement: The selected plan reads as Now, Next and Later

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanSectionsTests.The_selected_plan_groups_its_entries_into_Now_Next_and_Later, unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanSectionsTests.The_unlock_line_names_only_waiting_entries_one_landing_away, unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanSectionsTests.The_other_plans_fold_to_a_line_each_with_their_note, unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanSectionsTests.Unplanned_closes_the_column_and_Select_new_plan_picks_from_it]
```

The Plans view SHALL show the selected plan's entries in a session under Now, its ready entries under Next, and under Later one line for its waiting entries naming what unlocks next and one line for its done entries, followed by every other plan folded to one line and then the unplanned entries.

#### Scenario: A plan mid-run

- **Given** the plan `+zero-trust-auth` has three entries in a session, three ready, ten waiting of which #20 waits on #14 and #17, and eight done
- **When** the person selects it on the rail
- **Then** Now lists the three entries in a session with their stages, Next lists the three ready ones, Later reads "10 waiting — next to unlock: #20 when #14 and #17 land" and "8 done" on two lines, and the other plans follow folded to one line each before Unplanned

### Requirement: The Flat view lists the unplanned work

```meta
type: requirement
```

The Flat view SHALL list the entries that carry no plan and the plan entries in today's My Day, and no other plan entry.

#### Scenario: A plan entry on My Day

- **Given** an unplanned task, a `+zero-trust-auth` entry in today's My Day and a `+zero-trust-auth` entry not in it
- **When** the person switches to Flat
- **Then** the list holds the unplanned task and the My Day plan entry, and not the other plan entry

### Requirement: A plan's waves follow its dependencies

```meta
type: requirement
related: [.devbook/domain/work/features.md#task-dependencies]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanWavesTests.A_chain_puts_each_entry_one_wave_after_the_one_it_waits_on, unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanWavesTests.An_entry_depending_across_waves_sits_after_its_latest_dependency, unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanWavesTests.Opening_the_heading_shows_the_plan_alone_in_waves, unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanWavesTests.List_shows_the_plan_as_rows_and_Plans_goes_back]
```

A plan's own view SHALL place each of its entries one wave after the latest wave among the plan entries it waits on, and the entries waiting on none of them in the first wave, and SHALL name the plan branch the run lands on.

#### Scenario: Three waves

- **Given** the plan `+zero-trust-auth` where #9 waits on #1, #14 waits on #9, and #1 waits on nothing
- **When** the person opens the plan's view
- **Then** #1 is in wave 1, #9 in wave 2, #14 in wave 3, and the run line reads "Lands on plan/zero-trust-auth"

#### Scenario: The list instead of waves

- **Given** the plan's view shows its waves
- **When** the person presses List on the Waves / List toggle
- **Then** the plan's entries show as rows

### Requirement: A row shows its status as text and ends in copy and queue

```meta
type: requirement
```

A row in the Tasks list SHALL show its status as coloured text rather than a pill, and SHALL end in a copy button shown while the row is hovered or focused and a queue button, keeping every other badge it carries.

#### Scenario: Hovering a row

- **Given** a Ready prompt entry with a plan badge and an effort badge, not hovered
- **When** the person hovers its row
- **Then** the row reads "Ready" as text with both badges, and shows the copy button beside its queue button

## Manual ordering

```meta
type: requirements
related: [.devbook/domain/work/features.md#manual-ordering]
```

### Requirement: Unranked tasks fall back to recency

```meta
type: requirement
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
related: [.devbook/domain/work/features.md#import, .devbook/domain/work/domain.md#entry-kind]
```

### Requirement: The text save refuses a plan entry

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.SaveTaskFromTextPlanEntryTests.A_plan_entry_is_refused_rather_than_saved_as_a_task, unit:dotnet:Backlog.Modules.Tasks.UnitTests.SaveTaskFromTextPlanEntryTests.An_update_is_refused_on_the_same_terms, unit:dotnet:Backlog.Modules.Tasks.UnitTests.SaveTaskFromTextPlanEntryTests.The_refusal_names_import_as_the_way_in]
```

The system SHALL refuse to save entry text of the plan [Entry Kind](domain.md#entry-kind) as a task, whether creating or updating one, and SHALL name Import as the way to bring a plan in.

#### Scenario: A plan entry typed into the backlog

- **Given** entry text whose type word is `plan`
- **When** the person saves it as a task
- **Then** no task is stored, and the refusal names Import

## Filing a follow-up after an entry

```meta
type: requirements
related: [.devbook/domain/work/features.md#filing-a-follow-up-after-an-entry, .devbook/domain/notifications/requirements.md#requirement-a-run-at-personal-validation-asks-to-be-approved]
```

### Requirement: A follow-up is filed once, after the gated entry

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.FileFollowUpTests.A_follow_up_in_a_plan_is_a_ready_prompt_after_the_gated_entry_with_its_plan_tag_and_repository, unit:dotnet:Backlog.Modules.Tasks.UnitTests.FileFollowUpTests.A_gated_entry_named_by_its_plan_and_item_is_found, unit:dotnet:Backlog.Modules.Tasks.UnitTests.FileFollowUpTests.A_gated_entry_outside_any_plan_gets_a_follow_up_with_no_plan_tag, unit:dotnet:Backlog.Modules.Tasks.UnitTests.FileFollowUpTests.A_follow_up_of_a_follow_up_stays_in_the_plan, unit:dotnet:Backlog.Modules.Tasks.UnitTests.FileFollowUpTests.The_same_answer_handed_over_again_files_no_second_task, unit:dotnet:Backlog.Modules.Tasks.UnitTests.FileFollowUpTests.A_follow_up_deleted_since_is_not_filed_again_by_the_same_answer, unit:dotnet:Backlog.Modules.Tasks.UnitTests.FileFollowUpTests.A_gated_entry_that_is_gone_is_not_found, unit:dotnet:Backlog.Modules.Tasks.UnitTests.FileFollowUpTests.The_plan_imported_again_keeps_the_follow_up, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteTaskRepositoryTests.The_notification_a_follow_up_was_filed_from_comes_back, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeTests.The_notification_a_follow_up_was_filed_from_crosses_the_wire_and_an_older_document_reads_as_none, unit:dotnet:Backlog.Mobile.UI.UnitTests.TaskEditsTests.A_phone_edit_keeps_the_notification_a_follow_up_was_filed_from]
```

The system SHALL file exactly one task per accepted Approve + follow-up answer, of type `prompt` and status `ready`, waiting on the gated entry, carrying the gated entry's plan tag and repository and no `import_plan_id`, with the person's words as its body and the answer's notification id as its provenance.

#### Scenario: A follow-up in a plan

- **Given** the gated entry T-41 is a prompt in the plan `+notifications` for `owner/backlog`
- **When** the person answers Approve + follow-up with "Add the empty-state screenshot to the docs", and the owner accepts it
- **Then** one `ready` prompt exists `after:T-41`, tagged `+notifications`, for `owner/backlog`, with those words as its body

#### Scenario: The same answer handed over again

- **Given** a follow-up already filed for notification N-7
- **When** the answer of N-7 is handed to Sessions again
- **Then** no second task is filed

#### Scenario: A gated entry outside any plan

- **Given** the gated entry T-52 carries no plan tag
- **When** the person answers Approve + follow-up with "Rename the setting"
- **Then** one `ready` prompt exists `after:T-52`, with no plan tag

#### Scenario: The plan is imported again

- **Given** a follow-up filed under `+notifications`, not yet started
- **When** the person re-imports an updated `+notifications` plan
- **Then** the follow-up is kept, and the plan still lists it

### Requirement: The person's words stay body text

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.FileFollowUpTests.Words_that_look_like_a_step_stay_one_line_of_body_text, unit:dotnet:Backlog.Modules.Tasks.UnitTests.FileFollowUpTests.The_words_never_parse_as_structure_or_tags, unit:dotnet:Backlog.Modules.Tasks.UnitTests.FileFollowUpTests.The_read_view_shows_the_words_as_typed, unit:dotnet:Backlog.Modules.Tasks.UnitTests.FileFollowUpTests.Ordinary_prose_is_kept_exactly_as_typed, unit:dotnet:Backlog.SharedKernel.UnitTests.MarkdownPreviewEscapeTests]
```

The system SHALL write the words of a follow-up answer into the follow-up's body as plain text, so that no line of them becomes a sub-item, a heading or a fence.

#### Scenario: Words that look like a step

- **Given** a follow-up answer reading "- [ ] delete the old table"
- **When** the follow-up is filed
- **Then** its body holds that line as text, and the task has no sub-items

## Archive and lifecycle

```meta
type: requirements
related: [.devbook/domain/work/features.md#archive-and-lifecycle, .devbook/domain/work/domain.md#completed]
```

> The tick is [Completed](domain.md#completed).

### Requirement: Ticking an open task off moves it to done

```meta
type: requirement
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
related: [.devbook/domain/work/features.md#multi-device-sync]
```

### Requirement: The later edit wins whole

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeTests.Two_devices_converge_on_the_later_stamped_edit, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeTests.An_older_document_never_overwrites_a_newer_local_copy_even_one_already_pushed]
```

When the same task was edited on two devices, the system SHALL keep on both the copy with the later edit, whole, and SHALL NOT let an older copy overwrite a newer one.

#### Scenario: Two edits to one task

- **Given** a task renamed on the desktop at ten and renamed again on the laptop at eleven
- **When** both devices have synced
- **Then** both hold the laptop's copy of the task

## Saving one task from two places

```meta
type: requirements
related: [.devbook/domain/work/features.md#refinement-and-prioritization, .devbook/domain/work/features.md#multi-device-sync, .devbook/domain/work/domain.task.md#task]
```

### Requirement: A save made on an older version is refused

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteTaskStaleWriteTests.The_first_of_two_saves_from_one_read_lands_and_the_second_is_refused, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteTaskStaleWriteTests.Of_two_concurrent_saves_from_one_read_exactly_one_lands, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteTaskStaleWriteTests.A_batch_with_one_stale_task_writes_none_of_it, unit:dotnet:Backlog.Modules.Tasks.UnitTests.StaleWriteRefusalTests.A_text_save_the_store_refuses_answers_stale_write, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.TrackerToolsTests.A_stale_write_is_refused_with_read_again_and_retry, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksSaveStateBandTests.A_save_refused_as_stale_reloads_the_entry_and_says_so, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.TrackerToolsTests.Read_item_answers_the_version_it_read, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.TrackerToolsTests.A_write_built_on_an_outdated_read_is_refused_and_writes_nothing, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.TrackerToolsTests.A_write_on_the_current_version_lands_pinned_to_it, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.TrackerToolsTests.A_no_op_on_an_outdated_read_is_refused, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.TrackerToolsTests.Of_two_comments_on_one_read_the_second_is_refused, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.TrackerToolsTests.A_save_landing_inside_the_call_refuses_the_rebuilt_text, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.TrackerToolsTests.Without_versions_a_write_is_unpinned_as_before, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteTaskPinnedWriteTests.The_version_is_the_stamp_the_store_holds_and_moves_on_every_save, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteTaskPinnedWriteTests.A_save_on_the_version_read_lands, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteTaskPinnedWriteTests.The_second_save_on_one_read_is_refused_and_writes_nothing, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteTaskPinnedWriteTests.A_link_on_an_outdated_read_is_refused_and_writes_nothing, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteTaskPinnedWriteTests.Devbook_references_on_an_outdated_read_are_refused_and_write_nothing, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteTaskPinnedWriteTests.A_comment_on_an_outdated_read_is_refused_and_writes_nothing, unit:dotnet:Backlog.Desktop.UI.UnitTests.SelfAttributedTaskItemsTests.A_pinned_save_and_its_version_are_forwarded_and_marked_as_the_panes_write]
```

When a task was saved by another writer after a save read it, the system SHALL refuse that save as `tasks.stale_write` and write nothing, so the earlier save stands.

#### Scenario: Two sessions save one task

- **Given** two sessions that read the same version of a task
- **When** the first saves its change and the second then saves its own
- **Then** the task holds the first change, and the second save is refused as `tasks.stale_write`

#### Scenario: An agent's write is refused

- **Given** an MCP write tool whose save of a task is refused as stale
- **When** the tool answers
- **Then** the refusal names `tasks.stale_write` and tells the agent to read the task again and retry

#### Scenario: An agent's write built on an older read is refused

- **Given** an agent that read a task with `read_item`, and a save of that task by another writer since
- **When** the agent calls a write tool on the task with `expectedUpdatedAt` set to the `updatedAt` that read answered
- **Then** the write is refused as `tasks.stale_write` and writes nothing, and a write whose `expectedUpdatedAt` still matches lands

#### Scenario: The editor's save is refused

- **Given** a task open in the Tasks pane whose save is refused as stale
- **When** the refusal comes back
- **Then** the pane reloads the task with the reader's unsaved edit merged onto it and says "This entry changed somewhere else. It has been reloaded." beside the editor

### Requirement: A copy pulled from another device is not refused as stale

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeStaleWriteTests.A_later_pulled_copy_is_written_over_a_row_edited_here_since_any_read, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeStaleWriteTests.An_older_pulled_copy_is_still_held_by_precedence_alone, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeStaleWriteTests.A_local_writer_that_read_before_the_merge_is_refused_after_it]
```

The system SHALL decide a copy pulled from the replica by [the later edit wins whole](#requirement-the-later-edit-wins-whole) alone, and SHALL NOT refuse it because the local task changed since a read.

#### Scenario: A pulled copy lands over a local edit

- **Given** a task edited on this device after it was last read
- **When** a copy stamped later arrives from another device
- **Then** the pulled copy is written, and a local save based on the earlier read is refused

## Planning work that has no task yet

```meta
type: requirements
related: [.devbook/domain/work/features.md#planning-work-that-has-no-task-yet]
```

> The item's rules are its
> [invariants](domain.plan.invariants.md#invariant-gathered-tasks-outrank-a-completion-by-hand).

The scenarios plan "Write the launch post" from Monday 5 to Friday 9 October, and
read the roadmap on Monday 12 October.

### Requirement: Complete is offered only while no task carries the plan

```meta
type: requirement
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
tests: [integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandEditingTests.OpenInTasksIsNotOfferedForAnItemNoTaskCarries, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandEditingTests.OpenInTasksIsOfferedForAnExistingItemWithATag_WhenTheShellCanOpenIt, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandEditingTests.CompleteRaisesTheItemsId_ForWorkNoTaskCarries, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandEditingTests.ReopenIsOfferedInsteadOfComplete_OnceTheWorkWasCompleted, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandEditingTests.CompleteIsNotOfferedForNewWork, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandEditingTests.ReopeningFromTheEditor_ClearsTheDay]
```

A plan's editor SHALL offer Open in Tasks only while tasks carry the plan, Complete only while no task does and it is open, and Reopen only while it is completed by hand.

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
related: [.devbook/domain/insights/features.md#whether-each-plan-will-land-in-its-window]
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
related: [.devbook/domain/work/features.md#carrying-the-pace-with-the-person]
```

### Requirement: Day overrides travel inside the working week

```meta
type: requirement
```

The system SHALL carry day overrides inside the working week of the synced pace document, under that week's own change stamp, so the newer change to the week wins with its overrides.

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
```

The system SHALL read a working week that carries no overrides list as one with no day overrides.

#### Scenario: A pace saved before overrides existed

- **Given** a pace document whose working week has no overrides list
- **When** the roadmap opens
- **Then** every date counts the hours its weekday's pattern gives it

## Reading the near term closely

```meta
type: requirements
related: [.devbook/domain/work/features.md#reading-the-near-term-closely]
```

### Requirement: A date that is not worked is hatched

```meta
type: requirement
```

The system SHALL draw the head and column of every date counting no hours, unworked weekday or blocked, in a pattern weekend's colours and hatching, and every other date as an ordinary head.

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
```

The system SHALL count as each week head's planned hours the sum of the working hours of its seven dates, day overrides included.

#### Scenario: A week with one blocked and one unblocked date

- **Given** today is Saturday 3 October, the person blocked Friday 9 October and unblocked Saturday 10 October, whose stored hours are 10:00 to 14:00
- **When** the axis shows week 41
- **Then** its head reads 38 hours

### Requirement: The Hours switch shows or hides the hours line

```meta
type: requirement
```

The system SHALL put one Hours switch on the roadmap toolbar, on by default and remembered on the device without syncing, and show no hours on any head while it
is off.

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
```

The system SHALL show only actual hours up to now on each begun day and week head, today and the current week included, and only planned hours on each head still to come.

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
```

The system SHALL let the person open, from the hours on a begun day or week head, the working days of its week with each day's total and the working stretches it adds up from.

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
```

The system SHALL show no hours on a head that has begun when the actual hours cannot be read, because a planned figure there would read as hours worked.

#### Scenario: Session activity is unreadable

- **Given** today is Monday 12 October and the session activity cannot be read
- **When** the roadmap opens on last week in days
- **Then** the heads of last week, today and week 42 show no hours, Tuesday 13 October's head reads "8.5h", and the bars are drawn as before

## Blocking and unblocking a day

```meta
type: requirements
related: [.devbook/domain/work/features.md#blocking-and-unblocking-a-day]
```

### Requirement: A day head is a toggle

```meta
type: requirement
```

The system SHALL make each day column's head a button that pointer, Enter or Space presses, named "Block" plus the date on a worked date and "Unblock" plus the date on any other.

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
```

The system SHALL count no working hours on a date the person blocked.

#### Scenario: Blocking a Friday

- **Given** Friday 9 October is worked from 09:00 to 17:30
- **When** the person blocks it
- **Then** its head shows no hours and week 41 counts 34 hours

### Requirement: An unblocked date counts its weekday's hours

```meta
type: requirement
```

The system SHALL count on a date the person unblocked the hours from the start to the end its weekday stores in the weekly pattern.

#### Scenario: Unblocking a Saturday

- **Given** Saturday is not worked and stores 10:00 to 14:00
- **When** the person unblocks Saturday 10 October
- **Then** that date counts 4 hours and Saturday 17 October still counts none

### Requirement: Returning to the pattern removes the override

```meta
type: requirement
```

The system SHALL remove a date's day override when a press makes the date match its weekday's pattern again.

#### Scenario: Unblocking a blocked Friday

- **Given** the person blocked Friday 9 October
- **When** the person presses its head again
- **Then** Friday 9 October holds no override and counts 8.5 hours

### Requirement: A past date can be changed

```meta
type: requirement
```

The system SHALL let the person block or unblock a date that has passed as they would a date to come.

#### Scenario: Blocking last Friday

- **Given** today is Monday 12 October
- **When** the person blocks Friday 9 October
- **Then** Friday 9 October counts no hours and the measured paces count without it

## Setting days off in a list

```meta
type: requirements
related: [.devbook/domain/work/features.md#setting-days-off-in-a-list]
```

### Requirement: The Days off button opens the dialog

```meta
type: requirement
```

The system SHALL put a Days off button on the roadmap toolbar that opens the Days off dialog.

#### Scenario: Opening the dialog

- **Given** the roadmap is open
- **When** the person presses Days off on the toolbar
- **Then** the Days off dialog opens

### Requirement: The dialog lists every day override

```meta
type: requirement
```

The Days off dialog SHALL list every day override with its date, weekday and blocked or unblocked state, dates to come first and soonest first, and past dates only once the person asks.

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
```

A range added in the Days off dialog SHALL block each date the weekly pattern works, add no override where it leaves one off, and remove any unblocked override, making every date a day off.

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
```

The system SHALL unblock a single date the person adds as worked in the Days off dialog when the weekly pattern leaves it off, and add no override when the pattern already works it.

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
```

The system SHALL remove a date's day override when the person removes its entry from the Days off dialog.

#### Scenario: Removing a blocked Friday

- **Given** the person blocked Friday 9 October
- **When** the person removes its entry in the Days off dialog
- **Then** the entry leaves the list and Friday 9 October counts 8.5 hours

### Requirement: The dialog writes the overrides a head press writes

```meta
type: requirement
```

The system SHALL record each Days off dialog change as the same day override a press on the date's head records, and lay the bars out again as soon as it is made.

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
related: [.devbook/domain/work/features.md#looking-back-at-finished-plans]
```

### Requirement: The Completed switch shows or hides finished plans

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.WithFinishedWorkHidden_AFinishedItemIsNotDrawn_NorTheArrowToIt, unit:dotnet:Backlog.Desktop.UI.UnitTests.ShellNavigationStoreTests.The_roadmap_completed_switch_is_on_until_turned_off_and_off_survives_a_restart, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandTests.TheCompletedSwitch_IsOnUntilTurnedOff_AndOffIsKeptOnThisDevice]
```

The system SHALL put one Completed switch on the roadmap toolbar beside Hours, on by default and kept per device, unsynced, and while off draw no plan that is all done or completed by hand.

#### Scenario: Turning the finished plans off

- **Given** the Completed switch is on, "Write the launch post" was finished on Friday 9 October, and "Publish the launch" waits on it in the same lane
- **When** the person turns the switch off
- **Then** "Write the launch post" is not drawn, its lane closes up over "Publish the launch", and no arrow is drawn between them

#### Scenario: The choice stays on the device

- **Given** two paired devices with sync on, and the person turned the Completed switch off on the first
- **When** the person restarts the app on the first device and opens the roadmap on the second
- **Then** the first device's switch is still off and the second device's is on

## Archiving a finished plan's project note

```meta
type: requirements
related: [.devbook/domain/work/features.md#archiving-a-finished-plans-project-note]
```

### Requirement: A plan that finishes is offered the archive move once

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.ProjectNoteArchiveTests.A_plan_that_finishes_while_the_app_is_open_is_offered_once, unit:dotnet:Backlog.Desktop.UI.UnitTests.ProjectNoteArchiveTests.A_plan_already_finished_when_the_app_opens_is_left_to_the_weekly_review, unit:dotnet:Backlog.Desktop.UI.UnitTests.ProjectNoteArchiveTests.A_Brain_with_no_archive_area_is_never_offered_the_move, unit:dotnet:Backlog.Desktop.UI.UnitTests.ProjectNoteArchiveTests.An_old_plan_is_not_offered_when_the_archive_area_appears_later, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.FinishedProjectNoteTests.Only_a_finished_plans_resolved_project_notes_are_selected, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainRoadmapProjectNotesTests.Only_a_note_in_the_projects_area_can_be_archived, unit:dotnet:Backlog.UI.Components.UnitTests.ToastTrayTests.An_offer_draws_its_button_runs_it_on_the_press_and_goes]
```

The system SHALL, when a plan whose project note sits in the projects area of a Brain with an archive area becomes finished while the app is open, raise one toast offering **Move the project note to the archive**, and SHALL offer each plan once per device.

#### Scenario: The last task of a plan is ticked off

- **Given** the plan "House move" names `#brain:projects/house.md`, and one of its tasks is still open
- **When** the person ticks that task off
- **Then** a toast says "House move is done" and offers **Move the project note to the archive**

### Requirement: The press moves the note unchanged and the Knowledge Ref follows

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.ProjectNoteArchiveTests.The_press_moves_the_file_unchanged_and_the_Knowledge_Ref_follows, unit:dotnet:Backlog.Desktop.UI.UnitTests.ProjectNoteArchiveTests.Pressing_the_offer_archives_the_note_and_says_where_it_went, unit:dotnet:Backlog.Desktop.UI.UnitTests.ProjectNoteArchiveTests.A_refused_move_leaves_the_plan_as_it_was, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.FinishedProjectNoteTests.Every_ref_naming_the_moved_note_follows_it_with_its_heading_kept, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainRoadmapProjectNotesTests.The_move_answers_the_new_path_and_keeps_the_bytes]
```

The system SHALL, on the press, move the note's file byte for byte into the Brain's archive area through the link writer, and SHALL point every Knowledge Ref of the plan that named the note at the new path, keeping the heading it named. A refused move SHALL leave the plan unchanged.

#### Scenario: Pressing the offer

- **Given** the offer for "House move", whose Knowledge Ref is `#brain:projects/house.md#budget`
- **When** the person presses **Move the project note to the archive**
- **Then** the note is at `archive/house.md` with the same bytes, and the plan's Knowledge Ref reads `#brain:archive/house.md#budget`

### Requirement: Dismissing the offer changes nothing

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.ProjectNoteArchiveTests.Dismissing_the_offer_changes_nothing_and_it_is_not_offered_again, unit:dotnet:Backlog.UI.Components.UnitTests.ToastTrayTests.A_toast_with_no_action_draws_no_button]
```

The system SHALL write nothing when the offer is dismissed or times out, and SHALL not offer the same plan again on that device, after a restart or after the plan is reopened and finished again.

#### Scenario: Dismissing the offer

- **Given** the offer for "House move"
- **When** the person dismisses it, restarts the app, then reopens a task of the plan and ticks it off again
- **Then** the note is still at `projects/house.md`, the plan's refs are unchanged, and no offer appears

### Requirement: The weekly review lists finished plans' project notes

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.BrainReviewToolTests.The_review_lists_the_project_notes_of_finished_plans_to_archive, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.BrainReviewToolTests.A_host_with_no_roadmap_lists_nothing_to_archive, unit:dotnet:Backlog.Desktop.UI.UnitTests.ProjectNoteArchiveTests.A_finished_plan_lists_its_project_notes_once_each_and_nothing_else]
```

The system SHALL answer, through `list_brain_reviews` under `toArchive`, every finished plan's project note still in the projects area, with its path, its title and the plan's title, whether or not the plan was offered the move.

#### Scenario: The weekly review after a dismissed offer

- **Given** "House move" is finished and its offer was dismissed
- **When** an assistant calls `list_brain_reviews`
- **Then** `toArchive` lists `projects/house.md` with the plan "House move"

## Forecasting work in flight

```meta
type: requirements
related: [.devbook/domain/work/features.md#forecasting-work-in-flight]
```

### Requirement: A finished part is drawn as done

```meta
type: requirement
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
related: [.devbook/domain/work/features.md#placing-a-plan-in-time]
```

### Requirement: Each repository draws its own part at its own pace

```meta
type: requirement
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
```

The system SHALL count a shared task in full in each of its bands still holding open own tasks, else in those whose own work ended last, or in all when none had own tasks.

A band whose own tasks are all done therefore draws its part as finished, where
its work ran. That holds as long as each open shared task filed under it has another band
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
```

The system SHALL count an effort-sized window forward over each date's working hours, giving a blocked date none and an unblocked date its weekday's hours, and re-place it as soon as a date is toggled.

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
```

The system SHALL measure a pace as the points finished in its measuring period, divided by the working hours that period holds with its day overrides, times the weekly pattern's total hours.

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
tests: [integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandKeepUpTests.Opening_the_roadmap_stores_the_window_the_work_makes_from_today_and_draws_it, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandKeepUpTests.A_task_marked_done_stores_the_shorter_window, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.KeepUpWithWorkTests.AnEffortPlacedItemLeftBehind_IsStoredFromToday_StillPlacedByEffort, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.KeepUpWithWorkTests.ATaskMarkedDone_PullsTheEndIn_ToWhatIsLeft, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.KeepUpWithWorkTests.AnItemPlacedByItsDueDate_IsNeverWritten, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.KeepUpWithWorkTests.AnItemPlacedByHand_IsNeverWritten, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.EffortWindowTests.ADueDateOrAHandPlacedWindow_IsReadAsStored]
```

The system SHALL lay out again, every time the roadmap is read, each plan whose window is still sized by its effort and whose work is unfinished, from today over the effort not yet done.

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
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.PlanningVelocitySettingsStoreTests.An_untouched_store_reads_seven_points_a_week_and_writes_no_file, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.PlanningVelocitySettingsStoreTests.A_hand_edited_file_the_roadmap_could_not_divide_by_reads_as_the_default]
```

The system SHALL read an unset or unreadable pace, and one that is not a positive number, as seven points a working week.

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
tests: [integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandPaceTests.A_pace_the_roadmap_could_not_divide_by_says_so_and_changes_nothing, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.PlanningVelocitySettingsStoreTests.A_pace_of_zero_or_less_is_refused_and_changes_nothing, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.PlanningVelocitySettingsStoreTests.A_pace_too_fine_to_write_down_is_refused_rather_than_rounded_away, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.PlanningVelocitySettingsStoreTests.A_pace_that_is_not_a_number_is_refused_and_changes_nothing]
```

The system SHALL refuse a typed pace of zero or less, finer than 0.0001, or with a comma as the decimal mark, say why beside the field, and leave the pace as it was.

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
related: [.devbook/domain/work/features.md#reading-and-rescheduling-on-a-timeline]
```

### Requirement: A drop that moves nothing proposes nothing

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.RoadmapTimelineTests.A_gesture_that_travelled_no_whole_week_reports_nothing, unit:dotnet:Backlog.UI.Components.UnitTests.RoadmapTimelineTests.An_edge_that_clamps_back_to_where_it_started_reports_nothing]
```

The system SHALL propose no plan change when a bar is dropped on its own lane less than half a week from its pick-up point, or a pulled edge clamps back to its previous date.

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
related: [.devbook/domain/work/requirements.md#planning-work-that-has-no-task-yet]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.AnItemTasksCarry_OffersToOpenThemInTasks_NamedForTheItem, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.AnItemTasksCarry_OffersNothing_WhereTheHostCannotShowTasks, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.AnOpenTaskLessItem_OffersToComplete_NeverToOpenInTasks, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.ACompletedTaskLessItem_OffersNothingOnItsBar, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.AnItemInTwoBands_CarriesItsActionOnTheBarThatEndsLast_Only, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.EachItemSaysWhetherTasksCarryIt_AndWhetherItWasCompletedByHand, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.AnItemTasksCarry_IsNotCompletedByHand_WhateverDayWasSet, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.EveryPartOfAnItemAnswersForTheItem, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewTests.WithoutTheGatheredWork_NoItemSaysWhatItOffers]
```

While a plan's latest-ending bar is hovered or focused, its end SHALL show Open in Tasks when tasks carry it, Complete when none does and it is open, and no action when completed by hand.

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
