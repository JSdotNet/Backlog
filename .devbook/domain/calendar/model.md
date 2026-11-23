# Model

```meta
type: model
related: [.devbook/arc42/adr/0056-the-calendar-owns-its-grid-and-tasks-is-one-layer.md, .devbook/domain/calendar/domain.md#calendar-layer, .devbook/domain/calendar/domain.md#layer-choice, .devbook/domain/calendar/domain.md#range]
```

> Structural view of what the Calendar holds and reads. It owns no aggregate, so the
> diagram draws its [layers](domain.md#calendar-layer), its [range](domain.md#range) and
> [layer choices](domain.md#layer-choice), and the ports each source answers.
> [`domain.md`](domain.md) says what each word means.

## Model diagram

```mermaid
classDiagram
    class Calendar {
        <<surface>>
        Range range
        LayerChoice choices
    }
    class Range {
        <<enumeration>>
        Month
        Week
    }
    class LayerChoice {
        <<value object>>
        bool ShowTasks
        bool ShowPlans
        bool ShowEvents
    }
    class CalendarLayer {
        <<abstract>>
    }
    class TasksLayer {
        <<ICalendarTasksLayer>>
        bool Offered
        bool Available
        CalendarTasksDto Tasks
        SetDueDateAsync(task, day)
        SetPlannedHoursAsync(task, day, hours)
    }
    class PlansLayer {
        <<ICalendarPlansLayer>>
        bool Offered
        bool Available
        CalendarPlansDto Plans
        StartAsync(tag, start)
    }
    class EventsLayer {
        <<ICalendarEventsLayer>>
        bool Offered
        bool Available
        CalendarEventsDto Events
    }
    class ICalendarTasks {
        <<port>>
        ReadAsync(window) CalendarTasksDto
        Changed
        SetDueDateAsync(task, day) refusal
        SetPlannedHoursAsync(task, day, hours) refusal
    }
    class ICalendarPlans {
        <<port>>
        ReadAsync(today) CalendarPlansDto
        StartAsync(tag, start) refusal
    }
    class ICalendarCapacity {
        <<port>>
        HoursOn(day) decimal
    }
    class ICalendarEvents {
        <<port>>
        ReadAsync() CalendarEventsDto
    }
    class CalendarTasksDto {
        <<DTO>>
        CalendarTaskDto[] Tasks
        CalendarHoursBlockDto[] Hours
        bool Available
    }
    class CalendarPlansDto {
        <<DTO>>
        CalendarPlanWindowDto[] Windows
        CalendarMilestoneDto[] Milestones
        CalendarShelfPlanDto[] Shelf
        bool Available
    }
    class CalendarEventsDto {
        <<DTO>>
        CalendarEventDto[] Events
        CalendarKeyDateDto[] KeyDates
        bool Available
    }
    class Tasks {
        <<context>>
    }
    class RoadmapPlanning {
        <<context>>
    }
    class Events {
        <<context>>
    }

    Calendar --> Range : shows
    Calendar --> LayerChoice : remembers
    Calendar "1" --> "3" CalendarLayer : lays out
    CalendarLayer <|-- TasksLayer
    CalendarLayer <|-- PlansLayer
    CalendarLayer <|-- EventsLayer
    LayerChoice ..> TasksLayer : Show tasks
    LayerChoice ..> PlansLayer : Show plans
    LayerChoice ..> EventsLayer : Show events
    TasksLayer --> ICalendarTasks : reads, sets a due date and hours
    TasksLayer --> CalendarTasksDto : holds
    PlansLayer --> ICalendarPlans : reads, starts a shelf plan
    PlansLayer --> CalendarPlansDto : holds
    EventsLayer --> ICalendarEvents : reads
    EventsLayer --> CalendarEventsDto : holds
    Calendar --> ICalendarCapacity : reads a day's working hours
    ICalendarTasks ..> Tasks : answered over
    ICalendarPlans ..> RoadmapPlanning : answered over
    ICalendarCapacity ..> RoadmapPlanning : answered over
    ICalendarEvents ..> Events : answered over
```

## Relationship notes

- The Calendar stores only its range and its layer choices, on this device
  (`ICalendarViewPreferences`, kept in `shell-navigation.json`). Every other box is
  read fresh from its source; a layer's DTO is a copy, never kept.
- A write goes back through the context that owns what changes. A due date and planned
  hours go through `ICalendarTasks` to Tasks, and a shelf plan's start goes through `ICalendarPlans` to
  Roadmap Planning, which decides whether to take it and where the window ends. The
  Events layer writes nothing.
- The ports live in `Backlog.Modules.Calendar.Abstractions` and belong to the Calendar.
  Adapters under `Infrastructure.FileSystem/Calendar/` answer them: `TasksCalendarTasks`
  over Tasks' published contract, or `NoCalendarTasks` in a host without Tasks;
  `RoadmapCalendarPlans` and `RoadmapCalendarCapacity` over Roadmap Planning; and
  `EventsCalendarEvents` over Events' read contract, or `NoCalendarEvents` in a host
  without Events. The source contexts' own types appear here by reference only.
- The Tasks layer is built like the other two (local ADR 0056 §2). It reads the tasks
  with a due date in the window, narrowed by the window's repository scope, which the
  Calendar reads from the window and not from Tasks. No Tasks filter, open task or
  selection reaches it. Built as `ICalendarTasks` and `CalendarTasksDto` in the Calendar's
  abstractions, with the layer behind `ICalendarTasksLayer` and the window's scope behind
  `ICalendarWindowScope`.
- A layer with no port behind it, or whose area is off, answers Off (`Available` false)
  and its choice is not offered. A read that fails leaves the layer empty but available,
  and the other layers stay drawn.
