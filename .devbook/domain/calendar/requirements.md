# Requirements

```meta
type: requirements
related: [.devbook/domain/calendar/features.md]
```

> What the Calendar guarantees, one chapter per feature. Each requirement is one SHALL
> sentence with the scenarios that prove it. What the Events layer promises is in
> [Events on the Calendar](../events/requirements.md#events-on-the-calendar), and is not
> repeated here.

## Calendar view

```meta
type: requirements
related: [.devbook/domain/calendar/features.md#calendar-view, .devbook/domain/events/requirements.md#events-on-the-calendar, .devbook/arc42/adr/0056-the-calendar-owns-its-grid-and-tasks-is-one-layer.md]
```

> What the person gets when they lay their work out on a month or a week.

### Requirement: The range is a Month or a Week and is remembered

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarTests.The_range_control_is_a_fused_pair_with_month_pressed_until_week_is_picked, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarTests.Picking_a_range_reports_it_and_picking_the_pressed_one_reports_nothing, unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests.The_calendar_range_is_read_from_its_view_choices_and_a_pick_is_kept, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.The_calendar_range_is_remembered_and_a_fresh_shell_reopens_on_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.ShellCalendarViewPreferencesTests.The_range_and_show_plans_round_trip_under_their_stored_names, unit:dotnet:Backlog.Desktop.UI.UnitTests.ShellCalendarViewPreferencesTests.A_range_this_build_does_not_define_reads_as_month]
```

The system SHALL show the Calendar as a Month or a Week, one of the two always picked, and remember the pick on this device but never the date shown.

#### Scenario: Month until Week is picked

- **Given** a device that never picked a range
- **When** the person opens the Calendar
- **Then** Month is pressed and the month is shown

#### Scenario: Week reopens on today's week

- **Given** the person picked Week and stepped to another week
- **When** the app starts again
- **Then** the Calendar opens on Week, on the week holding today

### Requirement: The Week opens where the month is

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarTests.Week_opens_on_todays_week_in_todays_month_and_on_the_first_week_of_another, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarTests.The_opening_week_is_todays_in_todays_month_else_the_one_holding_the_first]
```

The system SHALL open the Week on the week holding today when today is in the month shown, and on the month's first week otherwise.

#### Scenario: Today's month

- **Given** the Calendar shows November 2026 and today is 18 November 2026
- **When** the person picks Week
- **Then** the week of 16 – 22 November is shown

#### Scenario: Another month

- **Given** the Calendar shows February 2027 and today is 6 October 2026
- **When** the person picks Week
- **Then** the week of 1 – 7 February 2027 is shown

### Requirement: Back to the month holding the week's Thursday

```meta
type: requirement
tests: unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarTests.Month_after_week_is_the_month_holding_the_weeks_thursday
```

The system SHALL return from a Week to the month that holds the week's Thursday.

#### Scenario: A week across two months

- **Given** the Calendar shows the week of 28 September – 4 October 2026
- **When** the person picks Month
- **Then** October 2026 is shown, the month holding most of the week's days

### Requirement: A Week folds nothing

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarTests.Week_shows_the_seven_days_of_todays_week_monday_first_as_one_tall_row_named_by_its_range, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarTests.A_week_column_lists_every_chip_of_its_day_with_nothing_folded, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarTests.A_week_draws_the_plan_bars_milestones_hours_and_day_totals_as_the_month_does]
```

The system SHALL list every task of each of a Week's seven days, Monday first, with nothing folded.

#### Scenario: A busy day

- **Given** five tasks are due on one Wednesday
- **When** the person shows that Wednesday's week
- **Then** the Wednesday lists all five tasks

### Requirement: Tasks are drawn while Show tasks is ticked

```meta
type: requirement
```

The system SHALL draw the Tasks layer, the tasks and their planned hours, while Show tasks is ticked, tick it on a device that never chose, and remember the choice on this device.

#### Scenario: On until unticked

- **Given** a device whose `shell-navigation.json` holds no Show tasks choice
- **When** the person opens the Calendar
- **Then** the tasks and their hours are drawn, and unticking Show tasks hides both

#### Scenario: Off survives a restart

- **Given** the person unticked Show tasks
- **When** the app starts again
- **Then** no task and no hours are drawn, and Show tasks is unticked

### Requirement: No Tasks filter narrows the Calendar

```meta
type: requirement
```

The system SHALL draw every task with a due date in the range and within the repository scope on the Calendar, whatever the Tasks filter bar holds, and show no Tasks filter bar, open task, tray of undated tasks or My Day count there.

#### Scenario: A filter set on Tasks

- **Given** the Tasks view is filtered to the type idea, and a prompt is due on Monday
- **When** the person shows the Calendar
- **Then** Monday shows the prompt, and no filter bar is drawn

#### Scenario: The repository scope

- **Given** the scope chips press one repository, and tasks of two repositories are due this week
- **When** the person shows the week
- **Then** only the pressed repository's tasks are drawn

### Requirement: Pressing a task opens it in Tasks

```meta
type: requirement
```

The system SHALL show the Tasks view with a task selected and open when the person presses that task on the Calendar.

#### Scenario: A due-date chip

- **Given** the task "Refactor sync" is due on Wednesday
- **When** the person presses its chip on the Calendar
- **Then** the Tasks view replaces the Calendar, with "Refactor sync" selected and open

### Requirement: Plans and events are drawn without Tasks

```meta
type: requirement
```

The system SHALL draw the Plans and Events layers under their own choices, and offer no Show tasks, in a host with no Tasks module.

#### Scenario: A host without Tasks

- **Given** a host with no Tasks module, Show plans ticked and a plan in this month
- **When** the person opens the Calendar
- **Then** the plan's bar is drawn and no Show tasks choice is offered

### Requirement: The Calendar is a header tab of its own

```meta
type: requirement
```

The system SHALL offer the Calendar as a header tab of its own after the view switch `Tasks | Board | Roadmap`, offer neither the GitHub check nor the Tasks area of Ask AI while it shows, and reopen a stored `lastView: calendar` on it.

#### Scenario: The header

- **Given** the Calendar is the main view
- **When** the person reads the header
- **Then** the view switch reads Tasks, Board, Roadmap, the Calendar tab after it is pressed, and no GitHub check is offered

#### Scenario: A file from before the tab

- **Given** a `shell-navigation.json` with `lastView: calendar` and no Show tasks choice
- **When** the app starts
- **Then** the Calendar opens under its own tab on the stored range and choices, with the tasks drawn

### Requirement: Plans are drawn only while Show plans is ticked

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests.On_a_device_that_never_chose_the_plans_are_off_until_ticked, unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests.Unticking_show_plans_is_remembered_in_the_calendars_view_choices_and_hides_the_plans, unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests.A_stored_show_plans_choice_survives_a_restart, unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests.Show_plans_and_show_events_are_ticked_apart, unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests.Without_a_roadmap_the_calendar_offers_no_plans]
```

The system SHALL draw the Plans layer only while Show plans is ticked, leave it off on a device that never chose, and remember the choice on this device.

#### Scenario: Off until ticked

- **Given** a device that never chose Show plans
- **When** the person opens the Calendar
- **Then** no plan bar is drawn, and ticking Show plans draws them

#### Scenario: The choice survives a restart

- **Given** the person ticked Show plans
- **When** the app starts again
- **Then** the plans are drawn

### Requirement: A plan's window is a bar across its days

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarPlansTests.A_window_is_cut_at_every_week_row_and_each_later_piece_says_it_carries_on, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarPlansTests.Overlapping_plans_take_a_lane_each_and_a_lane_is_reused_once_it_is_free, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarPlansTests.The_bars_are_drawn_in_each_week_row_with_its_lanes_and_a_tooltip_of_title_tag_and_points, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarPlansTests.A_bar_wears_its_band_colour_when_given_one_and_neutral_grey_otherwise, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarPlansTests.A_milestone_is_a_diamond_chip_on_its_day, unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests.The_plans_windows_milestones_and_shelf_are_drawn_from_the_port, unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests.A_bar_wears_its_repository_colour_only_while_the_colours_are_showing]
```

The system SHALL draw each planned item's window as a bar across the days it spans, cut at every week, naming its title, its plan tag and its finished points out of the total.

#### Scenario: A window across two weeks

- **Given** Show plans is ticked and a plan's window runs from a Thursday to the next Tuesday
- **When** the person shows that month
- **Then** the plan is a bar from Thursday to Sunday and a second bar, opening with "…", from Monday to Tuesday

#### Scenario: Overlapping plans

- **Given** two plans whose windows overlap in one week
- **When** the person shows that week
- **Then** each plan's bar takes a row of its own

### Requirement: A shelf plan starts where it is dropped

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarPlansTests.The_shelf_lists_the_plans_without_a_window_as_drag_sources_under_the_tasks, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarPlansTests.A_shelf_plan_dropped_on_a_day_reports_that_day_as_its_start_and_nothing_else_does, unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests.Dropping_a_shelf_plan_on_a_day_starts_its_window_there_through_the_port, unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests.A_refused_shelf_drop_is_said_in_a_toast_and_the_plan_stays_on_the_shelf, unit:dotnet:Backlog.Modules.Calendar.UnitTests.CalendarPlansLayerTests.A_start_passes_the_tag_and_day_and_answers_the_refusal]
```

The system SHALL start a plan from the shelf on the day it is dropped on, through the roadmap, which works out where its window ends.

#### Scenario: Dropped on a day

- **Given** a plan waits on the shelf under Plans without a window
- **When** the person drops it on Monday 7 December
- **Then** the roadmap starts its window on 7 December and works out where it ends

#### Scenario: The roadmap refuses

- **Given** a plan waits on the shelf
- **When** the person drops it on a day and the roadmap refuses the start
- **Then** the refusal is said and the plan stays on the shelf

### Requirement: A layer that cannot be read leaves the others

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests.A_plans_read_that_fails_leaves_the_tasks_on_the_calendar, unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests.An_events_read_that_fails_leaves_the_tasks_on_the_calendar, unit:dotnet:Backlog.Modules.Calendar.UnitTests.CalendarPlansLayerTests.A_read_that_fails_leaves_the_plans_empty_and_still_available, unit:dotnet:Backlog.Modules.Calendar.UnitTests.CalendarEventsLayerTests.A_read_that_fails_leaves_the_events_empty_and_still_available]
```

The system SHALL keep drawing the other layers when one layer cannot be read.

#### Scenario: The plans fail to read

- **Given** Show plans is ticked
- **When** the plans cannot be read
- **Then** the tasks are still drawn by due date, with no plan bar

#### Scenario: The tasks fail to read

- **Given** Show tasks and Show plans are ticked
- **When** the tasks cannot be read
- **Then** the plan bars are still drawn, with no task

### Requirement: The Calendar keeps only its range and choices

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.ShellCalendarViewPreferencesTests.A_file_written_before_the_calendar_module_reads_back_unchanged, unit:dotnet:Backlog.Desktop.UI.UnitTests.ShellCalendarViewPreferencesTests.Show_events_is_off_until_ticked_and_kept_under_its_stored_name_across_a_restart, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.A_shell_navigation_file_written_before_the_calendar_module_reopens_the_calendar_on_its_range_and_choices, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneCalendarTests.Dropping_a_tray_entry_on_a_day_writes_its_due_date, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneCalendarTests.Dragging_a_chip_to_another_day_moves_its_due_date, unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests.Dropping_a_shelf_plan_on_a_day_starts_its_window_there_through_the_port]
```

The system SHALL store nothing for the Calendar but its range and its layer choices, and write every change through the context that owns what changes.

#### Scenario: A due date moves through Tasks

- **Given** a task is due on Monday
- **When** the person drags its chip to Wednesday
- **Then** Tasks writes Wednesday as its due date, and the Calendar stores nothing of it

#### Scenario: The choices reopen as they were

- **Given** the person picked Week, unticked Show tasks, and ticked Show plans and Show events
- **When** the app starts again
- **Then** the Calendar opens on Week with the tasks off and the plans and events on, from the choices remembered on this device

## Planned hours

```meta
type: requirements
related: [.devbook/domain/calendar/features.md#planned-hours, .devbook/domain/tasks/domain.md#planned-hours]
```

> What the person gets when they set hours aside for a task on a day.

### Requirement: Hours are set from a day's Add hours

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarHoursTests.Add_hours_on_a_day_reports_the_picked_task_and_its_hours, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarHoursTests.Add_without_a_task_picked_asks_for_one, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarHoursTests.Picking_a_task_already_planned_on_the_day_edits_its_block, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneCalendarHoursTests.Add_hours_on_a_calendar_day_writes_a_block_to_the_entry_and_draws_it]
```

The system SHALL let the person set hours for a task on a day from that day's Add hours…, which asks for the task and the figure.

#### Scenario: Three hours on a Monday

- **Given** the task "Refactor sync" has no hours on Monday
- **When** the person picks Add hours… on Monday, picks the task and gives 3
- **Then** Monday shows "Refactor sync · 3h" apart from its due-date chips

#### Scenario: No task picked

- **Given** a day's Add hours… is open
- **When** the person adds a figure with no task picked
- **Then** they are asked for a task and nothing is set

### Requirement: A Shift drag sets hours and leaves the due date

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarHoursTests.A_shift_drop_opens_the_days_editor_for_that_task_and_moves_no_due_date, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarHoursTests.A_shift_drop_on_a_day_the_task_already_has_hours_on_edits_that_block, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneCalendarHoursTests.A_shift_drop_from_the_tray_plans_hours_and_moves_no_due_date]
```

The system SHALL ask only for the figure when a task is dragged onto a day with Shift held, and leave its due date as it was.

#### Scenario: Hours on another day

- **Given** a task due on Friday
- **When** the person drags it onto Tuesday with Shift held and gives 2
- **Then** Tuesday shows the task's 2 hours and the task is still due on Friday

### Requirement: A figure that cannot be planned is refused with why

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarHoursTests.Hours_that_are_no_number_to_plan_are_said_and_not_reported, unit:dotnet:Backlog.UI.Components.UnitTests.PlannedHoursEditorTests.A_figure_that_is_no_number_of_hours_is_said_and_not_reported, unit:dotnet:Backlog.UI.Components.UnitTests.PlannedHoursEditorTests.A_refused_figure_goes_back_to_the_stored_one_and_the_refusal_still_shows, unit:dotnet:Backlog.UI.Components.UnitTests.CalendarHoursTests.A_figure_that_is_no_number_of_hours_reads_as_none, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneCalendarHoursTests.A_refused_figure_is_said_and_nothing_is_written, unit:dotnet:Backlog.Modules.Tasks.UnitTests.SetPlannedHoursTests.A_figure_out_of_range_is_refused_and_nothing_is_written]
```

The system SHALL refuse a figure that is not a number, is none, or is more than a whole day, with a sentence that says why, and write nothing.

#### Scenario: More than a day

- **Given** a day's hours editor is open
- **When** the person gives 30
- **Then** a sentence says why 30 is refused, and the day's hours stay as they were

#### Scenario: Back to the figure that stood

- **Given** in Tasks, the open task's Planned hours shows 3 on a day
- **When** the person types "abc" for that day
- **Then** the refusal is said and the day goes back to 3

### Requirement: A day adds its hours up against the working day

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarHoursTests.A_day_totals_its_blocks_against_its_capacity, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarHoursTests.A_day_over_its_capacity_is_marked_with_the_numbers_in_its_tooltip, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarHoursTests.Without_a_capacity_the_total_stands_alone_and_is_never_over, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneCalendarHoursTests.A_day_totals_its_blocks_against_the_capacity_the_port_answers_and_marks_a_day_over_it]
```

The system SHALL add up a day's hours against the hours the person works that day, from the roadmap's working week and days off, and mark a day that holds more with how much more.

#### Scenario: Within the day

- **Given** a Monday of 8 working hours holds 3 and 2.5 hours
- **When** the person shows that week
- **Then** Monday reads "5.5h / 8h" and is not marked

#### Scenario: Over the day

- **Given** a Monday of 8 working hours holds 6 and 4 hours
- **When** the person shows that week
- **Then** Monday reads "10h / 8h", is marked, and says it is 2h over
