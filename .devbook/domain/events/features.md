# Features

```meta
type: features
```

> What Events lets the person do, in the context's own language. What each feature
> guarantees is in [`requirements.md`](requirements.md). The Events pane, prep tasks,
> the To Event route, the Calendar's events layer, the harvest task and Events on the
> phone are built. Every feature
> sits behind the [Events area](context.md#events-area) flag.

## Events pane

```meta
type: feature
feature-flag: .devbook/domain/events/context.md#events-area
related: [.devbook/domain/events/requirements.md#events-pane, .devbook/domain/events/domain.md#knowledge-event]
```

The desktop's place to keep track of knowledge events: add one, decide on it, and
link what it produced. The pane lists the person's events by state, the decided
ones first, and within a state the upcoming ones soonest first.

### Adding an event

```meta
type: sub-feature
```

The person adds an event by its title alone, and fills in the rest when they know
it. A new event starts Spotted. Dates typed here carry the date source Typed.

### Deciding on an event

```meta
type: sub-feature
related: [.devbook/domain/events/domain.md#knowledge-event-state]
```

The person moves an event forward as they decide: Considering, Going, Attended,
Harvested. A move may skip states, so a dotNET Friday afternoon goes from Spotted
straight to Going. A Spotted or Considering event can be Skipped, and the pane asks
why.

While deciding, the person records what they want to learn, the key dates, the
ticket state and the cost.

### Event series

```meta
type: sub-feature
related: [.devbook/domain/events/domain.md#event-series]
```

The person names the series an event belongs to, or starts a new one with its
name, organiser and link. An event opened in the pane lists the series' other
editions.

### Linking notes

```meta
type: sub-feature
related: [.devbook/domain/events/domain.md#note-link, .devbook/domain/brain/domain.md#knowledge-note]
```

The person links Brain notes holding the takeaways, and Talk notes taken on the
phone. Each link opens its note. A Brain note no longer at its path shows as
missing, and the person can link it again. A wrong link can be removed. Once the
event is Attended, the person writes the verdict.

## To Event route

```meta
type: feature
feature-flag: .devbook/domain/events/context.md#events-area
related: [.devbook/domain/events/requirements.md#to-event-route, .devbook/domain/inbox/features.md#route-to-event, .devbook/domain/capture/features.md#event-page-monitor]
tests: [unit:dotnet:Backlog.Modules.Events.UnitTests.SpotKnowledgeEventTests, unit:dotnet:Backlog.Modules.Events.UnitTests.EventsModuleRegistrationTests.The_published_contract_resolves_over_the_hosts_repositories]
```

Discovery runs through the Inbox. A capture source in Capture reads the event
pages it watches and brings each new edition into the Inbox as an item. In triage
the person routes it **To Event**, and Events makes it a Spotted knowledge event.

The event takes the item's title and link. When the capture found dates and a
place, they come along with the date source Event page. An item with no dates still
arrives, and the person fills them in on the event. A found date a rule would refuse
never refuses the route: an end before the start is dropped, and a time with no offset
keeps its day.

Events takes the route through `IKnowledgeEvents`, its published contract in
`Backlog.Modules.Events.Abstractions`.

The route is the Inbox's [Route to Event](../inbox/features.md#route-to-event),
which also picks the event's Series. The watched pages are Capture's
[event page monitor](../capture/features.md#event-page-monitor).

## Prep tasks

```meta
type: feature
feature-flag: .devbook/domain/events/context.md#events-area
related: [.devbook/domain/events/requirements.md#prep-tasks, .devbook/domain/events/domain.md#event-task-link, .devbook/domain/brain/features.md#brain-scope]
```

From an event the person files the work going takes: book the hotel, ask about the
training budget, pick the talks. A prep task is filed with no repository, so it
lands under the [Brain scope](../brain/features.md#brain-scope), and the event links
it by id. The person can also link a task that already exists.

The event lists its tasks with their status. The task shows nothing of the event:
it carries no field, tag or link naming it.

## Harvest task

```meta
type: feature
feature-flag: .devbook/domain/events/context.md#events-area
related: [.devbook/domain/events/requirements.md#harvest-task, .devbook/domain/events/domain.md#harvest-task-policy, .devbook/domain/events/context.md#desktop-clock]
```

The day after a Going event ends, a task `Harvest <event title>` appears under the
Brain scope, linked to the event. It reminds the person to write up the takeaways.
Marking the event Harvested completes the task. No notification is raised: the
task is the reminder.

The [Harvest Task Policy](domain.md#harvest-task-policy) does the work. An event the
person marked Attended before its end day passed gets no harvest task in the first
version.

## Events on the Calendar

```meta
type: feature
feature-flag: .devbook/domain/events/context.md#events-area
related: [.devbook/domain/events/requirements.md#events-on-the-calendar, .devbook/domain/calendar/features.md#calendar-view, .devbook/domain/calendar/domain.md#calendar-layer, .devbook/arc42/adr/0055-calendar-is-a-surface-of-its-own-that-reads-layers.md]
```

The Calendar shows the decided events beside the tasks and plans, under a
**Show events** choice beside **Show plans**. Each event spans its days: filled
while Going or later, outlined while Considering. Its key dates mark their own day.
Spotted and Skipped events stay off it, so nothing reaches the Calendar unasked.

The events are the Calendar's Events [layer](../calendar/domain.md#calendar-layer),
read from Events' read contract through a port the Calendar owns (local ADR 0055).
The layer changes no event: an event is not dragged to another day there.

## Events on the phone

```meta
type: feature
feature-flag: .devbook/domain/events/context.md#events-area
related: [.devbook/domain/events/requirements.md#events-on-the-phone, .devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md]
```

An **Events** tab appears once the phone holds an event. It lists the upcoming
events, soonest first, then the undated ones. Skipped events and those whose last
day has passed stay off the list.

At the venue the person opens the event on the phone: its state, series, dates,
place, link, key dates, what they wanted to learn, and its notes. The phone changes
nothing on the event. A Talk note the phone holds opens from the event. A Brain note
link reads as its title only, because the phone cannot see the Brain.

### Attach a Talk note

```meta
type: sub-feature
related: [.devbook/domain/capture/features.md#talk-note, .devbook/domain/events/domain.md#attachment-request, .devbook/domain/events/requirements.md#requirement-a-talk-note-is-attached-through-a-desktop]
```

From a [Talk note](../capture/features.md#talk-note) in the Notes tab, the person
picks one event the phone holds and attaches the note to it. The picker offers the
upcoming events first, then the past ones. Skipped events and events the note is
already attached to are left off. With no event held, the note shows no event
section.

The phone sends an [Attachment Request](domain.md#attachment-request). Until a
desktop applies it, the note says whether the request is waiting to send, sent, not
sent with the reason and a retry, or refused. Once a desktop applies it, the event
lists the note, and the note names each event it is attached to.
