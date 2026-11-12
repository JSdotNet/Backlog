# Features

```meta
type: features
related: [.devbook/arc42/adr/0055-calendar-is-a-surface-of-its-own-that-reads-layers.md]
```

## Calendar view

```meta
type: feature
setting: [.devbook/domain/calendar/context.md#calendar-range, .devbook/domain/calendar/context.md#show-plans, .devbook/domain/calendar/context.md#show-events]
related: [.devbook/domain/tasks/features.md#tasks-on-the-calendar, .devbook/domain/tasks/domain.md#due-date, .devbook/domain/tasks/features.md#my-day, .devbook/domain/roadmap/features.md#laying-out-a-plan-whose-tasks-arrived-first, .devbook/design/interaction-guidelines.md#group-shape-says-cardinality, .devbook/domain/events/features.md#events-on-the-calendar, .devbook/arc42/adr/0055-calendar-is-a-surface-of-its-own-that-reads-layers.md]
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.The_calendar_range_is_remembered_and_a_fresh_shell_reopens_on_it, unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarEventsTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneCalendarTests.On_a_device_that_never_chose_the_events_are_off_until_ticked_and_the_tick_is_kept, unit:dotnet:Backlog.Desktop.UI.UnitTests.CalendarSurfaceTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.A_shell_navigation_file_written_before_the_calendar_module_reopens_the_calendar_on_its_range_and_choices, unit:dotnet:Backlog.Desktop.UI.UnitTests.ShellCalendarViewPreferencesTests, unit:dotnet:Backlog.Modules.Calendar.UnitTests.CalendarPlansLayerTests]
```

The Calendar lays its [layers](domain.md#calendar-layer) out on a month or a week. It
keeps its slot in the header's view switch, `Tasks | Board | Calendar | Roadmap`. The
[Tasks layer](../tasks/features.md#tasks-on-the-calendar) is always drawn: the filtered
tasks by due date, under the Tasks filter bar and beside the open task. The Plans and
Events layers are drawn while their [layer choice](domain.md#layer-choice) is on.

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

Beside the tasks with no due date, **Plans without a window** lists the plans
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
related: [.devbook/domain/tasks/domain.md#planned-hours, .devbook/domain/calendar/features.md#calendar-view, .devbook/domain/tasks/features.md#tasks-on-the-calendar, .devbook/domain/roadmap/features.md, .devbook/design/interaction-guidelines.md#combobox]
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarHoursTests, unit:dotnet:Backlog.UI.Components.UnitTests.PlannedHoursEditorTests, unit:dotnet:Backlog.UI.Components.UnitTests.CalendarHoursTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneCalendarHoursTests]
```

The person can set [Planned Hours](../tasks/domain.md#planned-hours) aside for a task
on a day of the [Calendar](#calendar-view). They are part of the Tasks layer, and every
change goes through Tasks. A day shows each such task as a chip of its own with the
figure, such as "Refactor sync · 3h", apart from the due-date chips. The day adds its
hours up against the hours the person works that day, read from the roadmap's working
week and days off. A day holding more is marked, and says by how much.

Hours are set in either of two ways:

- From a day's **Add hours…**, which asks for the task and the figure.
- By dragging a task onto a day with Shift held, from the month or from the
  tasks with no due date. This asks for the figure only and leaves the due date.

Pressing a task's hours on a day changes or removes them. The open task's
**Planned hours** lists every day it has hours on, to change, remove or add one.
The hours follow the filters and repository colours like the task chips.

The figure is typed, or taken from suggestions: half an hour, and
1, 2, 3, 4, 6 and 8 hours. A typed figure is rounded to the quarter hour. A figure
that is not a number, is none, or is more than a whole day is refused with a
sentence that says why.

In the open task's **Planned hours**, a refused figure goes back to the
figure that stood. Taking a suggestion for a day there changes that day at once.
A typed figure is kept on Enter or when the field is left. On the line that adds
a day, a suggestion only fills in the figure, and **Add hours** adds the day and
the figure together.

When the day's hours editor closes, the keyboard focus goes back to what opened
it: the task's hours on the day, or the day's **Add hours…**. After the hours are removed, the
focus goes to **Add hours…**.

The roadmap never reads planned hours. A plan's window and pace come from points and finished work, so setting hours aside moves no bar on
the roadmap or the Calendar.
