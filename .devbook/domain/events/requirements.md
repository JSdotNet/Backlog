# Requirements

```meta
type: requirements
related: [.devbook/domain/events/features.md]
```

> What Events guarantees, one chapter per feature. Each requirement is one SHALL
> sentence with the scenarios that prove it, and names its tests once it is built.
> The Events pane, prep tasks, the To Event route, the Calendar's events layer and
> the harvest task are built.

## Events pane

```meta
type: requirements
related: [.devbook/domain/events/features.md#events-pane, .devbook/domain/events/domain.md#knowledge-event]
```

> What the person gets when they add an event and decide on it.

### Requirement: An event needs only a title

```meta
type: requirement
tests: [integration:dotnet:Backlog.Modules.Events.UnitTests.EventsScreenTests.A_title_alone_adds_a_spotted_event_and_opens_it, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsScreenTests.A_blank_title_is_refused_in_the_modules_words_and_nothing_is_added, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneTests.A_title_alone_adds_one_spotted_event, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneTests.A_blank_title_is_refused_and_nothing_is_saved]
```

The system SHALL let the person add a knowledge event with a title alone, in the state Spotted.

#### Scenario: A quick entry

- **Given** the Events area is on
- **When** the person adds "dotNET Friday, 27 November" with no other field
- **Then** the pane lists a Spotted event with that title

### Requirement: An event moves forward and may skip states

```meta
type: requirement
tests: [integration:dotnet:Backlog.Modules.Events.UnitTests.EventsScreenTests.A_spotted_event_offers_every_later_state_and_skip_and_marked_going_offers_neither_back_nor_skip, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneDecideTests.A_spotted_event_marked_going_is_going, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneDecideTests.A_going_event_offers_no_way_back_and_refuses_one, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneDecideTests.A_harvested_event_moves_nowhere]
```

The system SHALL offer only the states after an event's current one, including states further on than the next.

#### Scenario: Straight to Going

- **Given** a Spotted event
- **When** the person marks it Going
- **Then** the event is Going

#### Scenario: No way back

- **Given** a Going event
- **When** the person opens its state choice
- **Then** Spotted and Considering are not offered

### Requirement: Skipping asks why

```meta
type: requirement
tests: [integration:dotnet:Backlog.Modules.Events.UnitTests.EventsScreenTests.Skip_asks_why_refuses_a_blank_reason_and_shows_the_reason_once_skipped, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneDecideTests.A_considering_event_skipped_with_a_reason_shows_the_reason, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneDecideTests.A_blank_skip_reason_is_refused, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneDecideTests.A_going_event_cannot_be_skipped]
```

The system SHALL skip a Spotted or Considering event only with the reason the person gives, and show that reason on the event.

#### Scenario: Skipped with a reason

- **Given** a Considering event
- **When** the person skips it with the reason "Clashes with the release"
- **Then** the event is Skipped and shows "Clashes with the release"

#### Scenario: A Going event cannot be skipped

- **Given** a Going event
- **When** the person opens its state choice
- **Then** Skipped is not offered

### Requirement: A moved Brain note shows as missing

```meta
type: requirement
tests: [integration:dotnet:Backlog.Modules.Events.UnitTests.EventsScreenTests.A_linked_note_that_is_there_opens_in_the_devbook_and_a_moved_one_reads_missing, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneLinkTests.A_note_that_moved_shows_as_missing_with_the_title_it_had, integration:dotnet:Backlog.Infrastructure.Brain.UnitTests.EventsBrainNotesTests.A_note_that_moved_is_left_out_though_the_database_still_names_it]
```

The system SHALL show a linked Brain note that is no longer at its path as missing.

#### Scenario: The note was moved

- **Given** an event linking the Brain note `areas/learning/techorama.md`
- **When** the note is moved to another folder and the person opens the event
- **Then** the link shows as missing

### Requirement: The pane is withdrawn while the area is off

```meta
type: requirement
tests: [integration:dotnet:Backlog.HostComposition.UnitTests.WebHarnessHostTests.The_desktop_harness_composes_the_events_pane_behind_its_switch_which_is_off, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsScreenTests.Add_events_surface_registers_one_flagged_takeover_with_the_event_key_and_a_second_call_adds_nothing]
```

The system SHALL show no Events pane while the `events` flag is off.

#### Scenario: Area off

- **Given** the `events` flag is off
- **When** the person opens the desktop shell
- **Then** no Events pane is offered

### Requirement: The pane lists events by state, upcoming first

```meta
type: requirement
tests: [integration:dotnet:Backlog.Modules.Events.UnitTests.EventsScreenTests.The_list_groups_events_by_state_with_upcoming_first_and_draws_no_empty_group, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsScreenTests.With_no_events_the_pane_says_so_and_offers_to_add_one, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneTests.The_list_groups_by_state_in_decided_order_and_leaves_empty_groups_out, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneTests.Within_a_group_upcoming_come_soonest_first_then_undated_then_past_most_recent_first, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneTests.An_event_still_running_today_counts_as_upcoming_and_one_that_ended_yesterday_as_past]
```

The system SHALL list the events grouped by state, Going, Considering, Spotted, Attended, Harvested, Skipped, drawing no empty group, and within a group the events not yet over soonest first, then the undated ones, then the past ones most recent first.

#### Scenario: Upcoming first

- **Given** a Going event "NDC Oslo", and the Spotted events "Techorama Belgium" in May 2027 and "dotNET Friday" on 27 November 2026
- **When** the person opens the Events pane
- **Then** Going comes before Spotted, no other group is drawn, and "dotNET Friday" leads Spotted

#### Scenario: No events yet

- **Given** the person has no events
- **When** they open the Events pane
- **Then** the pane says there are no events yet and offers to add one

## To Event route

```meta
type: requirements
related: [.devbook/domain/events/features.md#to-event-route]
```

> What an Inbox item routed To Event becomes.

### Requirement: A routed item becomes a Spotted event

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.SpotKnowledgeEventTests.A_routed_item_becomes_a_Spotted_event_with_its_title_and_link, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxToEventTests.To_Event_in_triage_routes_the_item_says_so_and_moves_on]
```

The system SHALL turn an Inbox item routed To Event into a Spotted knowledge event with the item's title and link.

#### Scenario: An event page routed

- **Given** an Inbox item "Techorama Netherlands 2026" with a link to its page
- **When** the person routes it To Event
- **Then** a Spotted event "Techorama Netherlands 2026" with that link exists, and the item leaves the queue

### Requirement: Found dates travel with the item

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.SpotKnowledgeEventTests.Dates_read_from_the_page_travel_with_the_date_source_Event_page, unit:dotnet:Backlog.Modules.Events.UnitTests.SpotKnowledgeEventTests.A_multi_day_edition_keeps_its_end, unit:dotnet:Backlog.Infrastructure.Capture.UnitTests.InboxCaptureDeliveryTests.An_event_page_captures_facts_travel_to_the_intake, unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteToEventTests.An_event_page_capture_keeps_its_facts_on_the_item]
```

The system SHALL give the event the dates and place its capture found, with the date source Event page.

#### Scenario: Dates read from the page

- **Given** an Inbox item whose capture found 27 November 2026 from 16:30 `+01:00` in Nieuwegein
- **When** the person routes it To Event
- **Then** the event starts on 27 November 2026 at 16:30 `+01:00` in Nieuwegein, from the event page

### Requirement: An item without dates still arrives

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.SpotKnowledgeEventTests.An_item_without_dates_still_arrives_with_none, unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteToEventTests.An_item_with_no_facts_still_routes]
```

The system SHALL create the event without dates when the capture found none.

#### Scenario: No date found

- **Given** an Inbox item whose capture found no date
- **When** the person routes it To Event
- **Then** a Spotted event with no dates and no date source exists

## Prep tasks

```meta
type: requirements
related: [.devbook/domain/events/features.md#prep-tasks]
```

> What a task filed from an event, or linked to one, is born with.

### Requirement: A prep task files under the Brain

```meta
type: requirement
tests: [integration:dotnet:Backlog.Modules.Events.UnitTests.EventsScreenTests.Add_prep_task_files_the_title_alone_and_lists_it_with_its_status, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneLinkTests.A_prep_task_is_filed_with_its_title_alone_and_listed_by_id_with_its_status, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneLinkTests.A_refused_filing_links_nothing, integration:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.EventsTaskTargetTests.Filing_appends_after_the_backlog_with_no_repository_and_answers_the_id]
```

The system SHALL file a prep task created from an event with no repository, and link it to the event by its id.

#### Scenario: Book the hotel

- **Given** a Going event "Techorama Belgium"
- **When** the person creates the prep task "Book hotel in Antwerp" from it
- **Then** the task is listed under the Brain scope, and the event lists it as a prep task

### Requirement: A task does not show its event

```meta
type: requirement
tests: [integration:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.EventsTaskTargetTests.A_prep_task_parses_back_to_its_title_alone, integration:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.EventsTaskTargetTests.Nothing_a_title_holds_becomes_structure]
```

The system SHALL leave every task linked to an event without a field, tag or link that names the event.

#### Scenario: The task reads as any other

- **Given** a prep task filed from an event
- **When** the person opens the task in Tasks
- **Then** nothing on it names the event

### Requirement: An existing task can be linked

```meta
type: requirement
tests: [integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneLinkTests.An_existing_task_is_linked_by_its_id_and_nothing_is_written_to_it, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneLinkTests.A_task_this_backlog_does_not_hold_is_refused_in_words, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsScreenTests.Link_task_refuses_an_id_this_backlog_does_not_hold_in_words]
```

The system SHALL let the person link an existing task to an event by its id, leaving the task unchanged.

#### Scenario: Link the budget question

- **Given** a task "Ask about training budget" and a Considering event
- **When** the person links the task to the event
- **Then** the event lists the task, and the task is unchanged

#### Scenario: An unknown id is refused

- **Given** no task with that id in this backlog
- **When** the person links the id to an event
- **Then** the pane says "No task with that id in this backlog", and the event links nothing

### Requirement: The event lists its tasks with their status

```meta
type: requirement
tests: [integration:dotnet:Backlog.Modules.Events.UnitTests.EventsScreenTests.A_linked_task_this_backlog_no_longer_holds_reads_not_in_this_backlog, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsScreenTests.A_task_changing_underneath_reads_the_open_events_chips_again, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneLinkTests.A_linked_task_this_backlog_no_longer_holds_reads_as_not_in_this_backlog, integration:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.EventsTaskTargetTests.A_lookup_answers_the_held_tasks_with_their_status_in_words]
```

The system SHALL list each task linked to an event with its title and status, and a task this backlog does not hold as not in this backlog.

#### Scenario: A task no longer held

- **Given** an event linking a task this backlog no longer holds
- **When** the person opens the event
- **Then** the task reads as not in this backlog

#### Scenario: A task changed elsewhere

- **Given** an open event listing the task "Book hotel in Antwerp" as Draft
- **When** the task is marked Done in Tasks
- **Then** the event lists the task as Done

## Harvest task

```meta
type: requirements
related: [.devbook/domain/events/features.md#harvest-task, .devbook/domain/events/domain.md#harvest-task-policy]
```

> What the Harvest Task Policy promises. No user triggers it, so its proof is
> `integration`.

### Requirement: A harvest task is filed the day after

```meta
type: requirement
tests: [integration:dotnet:Backlog.Modules.Events.UnitTests.HarvestTaskPolicyTests.A_going_event_gets_its_harvest_task_the_day_after_it_ends, integration:dotnet:Backlog.Modules.Events.UnitTests.HarvestTaskPolicyTests.A_going_event_is_not_due_on_its_last_day, integration:dotnet:Backlog.Modules.Events.UnitTests.HarvestTaskWorkerTests.A_due_event_is_harvested_shortly_after_the_app_starts, integration:dotnet:Backlog.Modules.Events.UnitTests.HarvestTaskWorkerTests.An_event_ending_today_is_harvested_once_the_local_day_turns]
```

The system SHALL file a task titled `Harvest <event title>` with no repository on the first local day after a Going event's end date, and link it to the event as its harvest task.

#### Scenario: The day after Techorama

- **Given** a Going event "Techorama Belgium" that ends on 14 May
- **When** a desktop starts its local day on 15 May
- **Then** the task "Harvest Techorama Belgium" exists under the Brain scope, linked to the event as its harvest task

#### Scenario: Not on the last day

- **Given** a Going event that ends today
- **When** the desktop checks during the day
- **Then** no harvest task is filed

### Requirement: One harvest task per event

```meta
type: requirement
tests: [integration:dotnet:Backlog.Modules.Events.UnitTests.HarvestTaskPolicyTests.A_second_check_files_no_second_harvest_task, integration:dotnet:Backlog.Modules.Events.UnitTests.HarvestTaskPolicyTests.An_event_already_holding_a_harvest_task_gets_none_filed]
```

The system SHALL file at most one harvest task per event, however many of the person's desktops see its end day pass.

#### Scenario: Two desktops

- **Given** a Going event whose end day has passed, and two desktops that both run that morning
- **When** both have synced
- **Then** exactly one harvest task exists for the event

### Requirement: Harvested completes the harvest task

```meta
type: requirement
tests: [integration:dotnet:Backlog.Modules.Events.UnitTests.HarvestTaskPolicyTests.Marking_the_event_harvested_completes_its_open_harvest_task, integration:dotnet:Backlog.Modules.Events.UnitTests.HarvestTaskPolicyTests.A_finished_harvest_task_is_left_as_it_is, integration:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.EventsTaskTargetTests.Completing_an_open_task_marks_it_done_where_it_stands, integration:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.EventsTaskTargetTests.Completing_an_open_task_ticks_it_off_today_with_its_steps, integration:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.EventsTaskTargetTests.Completing_a_finished_task_leaves_it_as_it_is]
```

The system SHALL complete an event's harvest task when the event is marked Harvested, if the task is still open.

#### Scenario: Takeaways written

- **Given** an Attended event whose harvest task is open
- **When** the person marks the event Harvested
- **Then** the harvest task is Done

#### Scenario: The task was already finished

- **Given** an Attended event whose harvest task is already Done
- **When** the person marks the event Harvested
- **Then** the task is unchanged

### Requirement: Finishing the task leaves the event

```meta
type: requirement
tests: [integration:dotnet:Backlog.Modules.Events.UnitTests.HarvestTaskPolicyTests.Finishing_the_harvest_task_in_tasks_leaves_the_event_going]
```

The system SHALL leave an event's state unchanged when its harvest task is completed in Tasks.

#### Scenario: Done in Tasks

- **Given** a Going event with an open harvest task
- **When** the person marks the task Done in Tasks
- **Then** the event is still Going

### Requirement: The harvest raises no notification

```meta
type: requirement
```

The system SHALL raise no notification when it files or completes a harvest task.

#### Scenario: Filed quietly

- **Given** a Going event whose end day has passed
- **When** the harvest task is filed
- **Then** the notification centre holds nothing about it

## Events on the Calendar

```meta
type: requirements
related: [.devbook/domain/events/features.md#events-on-the-calendar]
```

> What the Calendar's events layer shows, and what it leaves alone.

### Requirement: Decided events span their days

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarEventsTests.A_three_day_conference_is_a_filled_bar_across_its_days_named_by_title_state_and_place, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarEventsTests.An_event_still_being_weighed_up_is_dashed_and_a_decided_one_is_filled, unit:dotnet:Backlog.Modules.Events.UnitTests.KnowledgeEventReaderTests.Only_decided_events_are_read_in_the_order_they_were_saved, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneCalendarTests.The_events_and_their_key_dates_are_drawn_from_the_port_once_the_box_is_ticked]
```

The system SHALL draw each Considering, Going, Attended and Harvested event with dates across its days on the Calendar while Show events is on.

#### Scenario: A three-day conference

- **Given** a Going event from 12 to 14 May, and Show events on
- **When** the person opens the Calendar on May
- **Then** the event spans 12, 13 and 14 May

#### Scenario: Spotted stays off

- **Given** a Spotted event on 27 November, and Show events on
- **When** the person opens the Calendar on November
- **Then** 27 November shows no event

### Requirement: Key dates mark their day

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarEventsTests.Early_bird_is_a_diamond_on_its_day_naming_the_event_and_hidden_while_the_box_is_unticked, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.EventsCalendarEventsTests.An_event_with_no_start_draws_no_bar_but_keeps_its_key_dates, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneCalendarTests.The_events_and_their_key_dates_are_drawn_from_the_port_once_the_box_is_ticked]
```

The system SHALL mark each key date of an event the layer draws on that date.

#### Scenario: Early bird

- **Given** a Considering event with the key date "Early bird ends" on 3 March
- **When** the person opens the Calendar on March with Show events on
- **Then** 3 March shows "Early bird ends"

### Requirement: The Calendar changes no event

```meta
type: requirement
tests: unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarEventsTests.An_event_bar_is_no_drag_source_and_no_drop_reports_a_change_for_it
```

The system SHALL change no knowledge event from the Calendar.

#### Scenario: An event is not dragged

- **Given** a Going event on the Calendar
- **When** the person tries to drag it to another day
- **Then** the event keeps its dates

### Requirement: No events layer while the area is off

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneCalendarTests.With_the_events_area_off_there_is_no_box_and_nothing_drawn_whatever_was_ticked, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneCalendarTests.No_show_events_box_is_offered_before_the_events_read_lands, unit:dotnet:Backlog.HostComposition.UnitTests.WebHarnessHostTests.The_desktop_harness_composes_the_events_store_and_shows_nothing_of_it, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.EventsCalendarEventsTests.With_the_events_area_off_nothing_is_read_and_no_events_are_offered]
```

The system SHALL offer no Show events choice on the Calendar while the `events` flag is off.

#### Scenario: Layer withdrawn

- **Given** the `events` flag is off
- **When** the person opens the Calendar
- **Then** Show events is not offered

## Events on the phone

```meta
type: requirements
related: [.devbook/domain/events/features.md#events-on-the-phone]
```

> What the phone shows of an event, and the one thing it may add.

### Requirement: The phone reads events

```meta
type: requirement
```

The system SHALL show the person's knowledge events on the phone without offering any change to them.

#### Scenario: At the venue

- **Given** a Going event synced to the phone
- **When** the person opens it on the phone
- **Then** its dates, place, link, key dates and notes show, and no field can be edited

### Requirement: A Brain note shows by its title

```meta
type: requirement
```

The system SHALL show a Brain note linked to an event on the phone by its title only.

#### Scenario: Takeaways on the phone

- **Given** an event linking the Brain note "Techorama takeaways"
- **When** the person opens the event on the phone
- **Then** "Techorama takeaways" shows, and does not open

### Requirement: A Talk note is attached through a desktop

```meta
type: requirement
```

The system SHALL attach a Talk note chosen on the phone to the event once one of the person's desktops applies the request.

#### Scenario: A note from the keynote

- **Given** a Going event and a Talk note "Keynote" in the phone's Notes tab
- **When** the person attaches the note to the event and a desktop syncs
- **Then** the event links "Keynote" on every device

#### Scenario: Attached twice

- **Given** an event already linking the Talk note "Keynote"
- **When** a second request for the same note is applied
- **Then** the event still links "Keynote" once

### Requirement: No Events screen while the area is off

```meta
type: requirement
```

The system SHALL show no Events screen on the phone while the `events` flag is off.

#### Scenario: Phone area off

- **Given** the `events` flag is off
- **When** the person opens the phone app
- **Then** no Events screen is offered
