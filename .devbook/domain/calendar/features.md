# Features

```meta
type: features
related: [.devbook/arc42/adr/0055-calendar-is-a-surface-of-its-own-that-reads-layers.md, .devbook/arc42/adr/0056-the-calendar-owns-its-grid-and-tasks-is-one-layer.md]
```

## Calendar view

```meta
type: feature
setting: [.devbook/domain/calendar/context.md#calendar-range, .devbook/domain/calendar/context.md#show-tasks, .devbook/domain/calendar/context.md#show-plans, .devbook/domain/calendar/context.md#show-events]
related: [.devbook/domain/calendar/requirements.md#calendar-view, .devbook/domain/tasks/features.md#tasks-on-the-calendar, .devbook/domain/tasks/domain.md#due-date, .devbook/domain/roadmap/features.md#laying-out-a-plan-whose-tasks-arrived-first, .devbook/design/interaction-guidelines.md#group-shape-says-cardinality, .devbook/domain/events/features.md#events-on-the-calendar, .devbook/design/color-scheme.md#calendar-event-tones, .devbook/arc42/adr/0055-calendar-is-a-surface-of-its-own-that-reads-layers.md, .devbook/arc42/adr/0056-the-calendar-owns-its-grid-and-tasks-is-one-layer.md#7-the-calendar-gets-a-header-tab-of-its-own]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarGridTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.The_calendar_range_is_remembered_and_a_fresh_shell_reopens_on_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarGridEventsTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.A_shell_navigation_file_written_before_the_calendar_module_reopens_the_calendar_on_its_range_and_choices, unit:dotnet:Backlog.Desktop.UI.UnitTests.ShellCalendarViewPreferencesTests, unit:dotnet:Backlog.Modules.Calendar.UnitTests.CalendarPlansLayerTests, unit:dotnet:Backlog.Modules.Calendar.UnitTests.CalendarEventsLayerTests]
```

The Calendar lays its [layers](domain.md#calendar-layer) out on a month or a week, on a
grid of its own. It has a tab of its own in the header, after the view switch
`Tasks | Board | Roadmap` (local ADR 0056 §7). Each layer is drawn while its
[layer choice](domain.md#layer-choice) is on. While the Calendar shows, the GitHub check
and the Tasks area of Ask AI are not offered: no task list is on screen.

The [Tasks layer](../tasks/features.md#tasks-on-the-calendar) lays each task with a due
date out on that date, within the repository scope. No Tasks filter narrows it, and no
open task stands beside the grid. A **Show tasks** choice beside the month's name turns
the tasks and their hours off. It is on by default, offered only while Tasks is there,
and remembered on this device. Pressing a task shows the Tasks view with that task
selected and open. In a host with no Tasks, the Calendar still draws its plans and
events.

A **Month | Week** choice beside the month's name picks the
[range](domain.md#range). One of the two is always pressed, and the choice is
remembered on this device. The month runs Monday to Sunday in whole weeks, so its first
and last rows reach into the neighbouring months. Those days are dimmed, and weekends
are shaded. Today is marked.

The week shows its seven days, Monday first, as tall columns. Each day lists every
task, with nothing folded and no day dimmed. The reader picked the week, so a day of
the neighbouring month is part of it. The layers, the [planned hours](#planned-hours)
and the drags work as on the month. Previous and next step a week at a time. The
heading names the week's days, such as "5 – 11 October 2026".

- Switching to the week opens on the week holding today when today is in the
  month shown, and on the month's first week otherwise.
- Switching back to the month shows the month holding the week's Thursday. A
  week across two months therefore lands in the month that holds most of its
  days.
- Only the range is remembered, never the date shown.

The roadmap's plans are the Plans layer. A **Show plans** choice beside the month's
name turns them on; it is off by default, and remembered on this device.

- Each planned item's window is a bar across the days it spans, cut at every
  week, under the day numbers and above the tasks.
- Plans that overlap in a week each take a row of their own. A bar carried on
  from the week before opens with "…".
- A bar says its title, its plan tag and how many of its points are finished out
  of the total.
- A bar wears its repository's colour while repository colours are showing, as on
  the roadmap. It wears a neutral grey while they are not.
- A milestone is a filled diamond on its date.

The decided [knowledge events](../events/domain.md#knowledge-event) are the Events
layer ([Events on the Calendar](../events/features.md#events-on-the-calendar)). A
**Show events** choice after Show plans turns them on. It is offered only while the
[Events area](../events/context.md#events-area) is on, is off by default, and is
remembered on this device apart from Show plans.

- A Considering, Going, Attended or Harvested event is a bar across its days, cut at every
  week as a plan's is. Events take rows of their own above the plans.
- A bar says the event's title, state and place. It is filled while Going, Attended or
  Harvested, and has a dashed outline while Considering.
- An event with a start and no end takes its start day. An event with no start has no bar.
- Each [key date](../events/domain.md#key-date) of a decided event is a hollow diamond on its
  date, named by its label, whether or not the event has a bar.
- A bar takes no drag.

Beside the calendar, **Plans without a window** lists the plans
waiting on the roadmap's
[shelf](../roadmap/features.md#laying-out-a-plan-whose-tasks-arrived-first).
Dropping one on a day starts its window there. The roadmap, never the Calendar,
works out where it ends from its points and the pace, as it places any plan.

The drop is a pointer gesture. The keyboard ways are on the
roadmap: its shelf's **Plan it** places the plan from today, and its item editor
moves the window to any day.

The Calendar reads its layers and stores nothing but its range and layer choices. It
starts a shelf plan through the roadmap, and sets due dates and hours through Tasks. It
changes nothing else.

### Planned hours

```meta
type: sub-feature
related: [.devbook/domain/calendar/requirements.md#planned-hours, .devbook/domain/tasks/domain.md#planned-hours, .devbook/domain/calendar/features.md#calendar-view, .devbook/domain/tasks/features.md#tasks-on-the-calendar, .devbook/domain/roadmap/features.md, .devbook/design/interaction-guidelines.md#combobox]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarGridHoursTests, unit:dotnet:Backlog.UI.Components.UnitTests.PlannedHoursEditorTests, unit:dotnet:Backlog.UI.Components.UnitTests.CalendarHoursTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPanePlannedHoursTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests.Hours_added_changed_and_removed_on_a_day_go_through_the_port, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.TasksCalendarTasksTests]
```

The person can set [Planned Hours](../tasks/domain.md#planned-hours) aside for a task
on a day of the [Calendar](#calendar-view). They are part of the Tasks layer, and every
change goes through Tasks. A day shows each such task as a chip of its own with the
figure, such as "Refactor sync · 3h", apart from the due-date chips. The day adds its
hours up against the hours the person works that day, read from the roadmap's working
week and days off. A day holding more is marked, and says by how much.

Hours are set in either of two ways:

- From a day's **Add hours…**, which asks for the task and the figure.
- By dragging a task onto another day with Shift held. This asks for the figure
  only and leaves the due date.

Pressing a task's hours on a day changes or removes them. In Tasks, the open task's
**Planned hours** lists every day it has hours on, to change, remove or add one.
The hours follow the repository scope and colours like the task chips, and Show tasks
turns them off with the tasks.

The figure is typed, or taken from suggestions: half an hour, and
1, 2, 3, 4, 6 and 8 hours. A typed figure is rounded to the quarter hour. A figure
that is not a number, is none, or is more than a whole day is refused with a
sentence that says why.

In Tasks, in the open task's **Planned hours**, a refused figure goes back to the
figure that stood. Taking a suggestion for a day there changes that day at once.
A typed figure is kept on Enter or when the field is left. On the line that adds
a day, a suggestion only fills in the figure, and **Add hours** adds the day and
the figure together.

When the day's hours editor closes, the keyboard focus goes back to what opened
it: the task's hours on the day, or the day's **Add hours…**. After the hours are removed, the
focus goes to **Add hours…**.

The roadmap never reads planned hours. A plan's window and pace come from points and finished work, so setting hours aside moves no bar on
the roadmap or the Calendar.
