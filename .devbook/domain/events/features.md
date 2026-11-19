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
related: [.devbook/domain/events/requirements.md#events-pane, .devbook/domain/events/domain.md#knowledge-event, .devbook/domain/events/requirements.md#requirement-an-open-pane-shows-a-change-made-elsewhere, .devbook/arc42/08-crosscutting-concepts.md#change-notices]
```

The desktop's place to keep track of knowledge events: add one, decide on it, and
link what it produced. The pane lists the person's events by state, the decided
ones first, and within a state the upcoming ones soonest first.

An event added or changed elsewhere shows in an open pane at once, without a reload
by hand, in the main window and in a popped-out one: over MCP, through the To Event
route, or in another window. A field the person is typing keeps what they typed.

### Adding an event

```meta
type: sub-feature
```

The person adds an event by its title alone, and fills in the rest when they know
it. A new event starts Spotted. Dates typed here carry the date source Typed. Beside
the dates the pane shows where they came from, Typed or Event page, because a listing
and the event's own page can disagree.

### Add an event over MCP

```meta
type: sub-feature
depends-on: [.devbook/domain/events/features.md#adding-an-event]
related: [.devbook/arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md, .devbook/domain/events/requirements.md#requirement-an-assistant-adds-an-event-in-one-write, .devbook/domain/events/requirements.md#requirement-the-mcp-tool-is-withdrawn-while-the-area-is-off, .devbook/domain/inbox/features.md#work-the-queue-over-mcp]
tests: [unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.EventToolsTests, integration:dotnet:Backlog.Modules.Events.UnitTests.EventsPaneAddTests, integration:dotnet:Backlog.HostComposition.UnitTests.McpServerCompositionTests.An_event_added_over_mcp_reaches_an_events_pane_open_in_another_scope]
```

Let an assistant add an event it hears of, such as a conference named in a mail,
without opening the pane. The app's MCP server carries one Events tool,
`add_knowledge_event`, behind the [Events area](context.md#events-area) flag. It
does what [Adding an event](#adding-an-event) does, in one write: the event starts
Spotted, and its dates carry the date source Typed.

Only the title is required. The call may also name the series, the start and end
in ISO 8601, online or a venue, the link, what to learn, the ticket state and the
cost. A series name joins the series of that name, whatever its case, and an unknown
name starts one. The tool answers the new event's id, title, state, series and dates.
Calling it twice adds two events.

A rule the pane would refuse is refused with its code, and nothing is written: no
event and no series. So is a name two series share, a date that is not ISO 8601,
and an unknown ticket word. A time with no offset takes this desktop's offset for
that day, as the pane does.

### Deciding on an event

```meta
type: sub-feature
related: [.devbook/domain/events/domain.md#knowledge-event-state]
```

The person moves an event forward as they decide: Considering, Going, Attended,
Harvested. A move may skip states, so a dotNET Friday afternoon goes from Spotted
straight to Going. A Spotted, Considering or Going event can be Skipped, and the
pane asks why. A Going one is Skipped when it is called off or the person gives up
the ticket.

While deciding, the person records what they want to learn, the key dates, the
ticket state and the cost. A cost needs its currency: emptying the currency of a saved
cost keeps the cost as it was, and the field says a currency is needed.

### Removing an event

```meta
type: sub-feature
related: [.devbook/domain/events/requirements.md#requirement-an-event-added-by-mistake-can-be-removed, .devbook/domain/events/domain.invariants.md#invariant-only-an-event-with-no-harvest-task-is-removed]
```

The person removes an event added by mistake, such as a second copy of an item
routed To Event twice, once they confirm. The pane offers no remove for an event
with a harvest task, because its harvest is worth keeping. A Going one that was
called off is Skipped instead.

Removing takes nothing with it. The prep and linked tasks stay in Tasks, the notes
stay in the Brain and the Inbox, and the series stays. The removal reaches the other
desktops and the phone with their next sync. A removed event cannot be brought back
in the first version.

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
phone. Each link opens its note: a Brain note in the Devbook on the Brain side, a
Talk note in the Inbox, in the list it is filed in. While the Inbox is off, a Talk note shows without the jump. The note picker narrows its
list to the notes whose title or path holds what is typed. A Brain path names one
note whatever its case, and with or without `./`, so a note is never linked twice. A
Brain note no longer at its path shows as missing, and the person can link it again.
A wrong link can be removed. Once the event is Attended, the person writes the
verdict.

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
[event page monitor](../capture/features.md#event-page-monitor). Undoing the route
leaves the event it made, and the person [removes it](#removing-an-event) in the pane.

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

An **Events** tab appears once the phone holds an event, and leaves again when a
desktop turns the [Events area](context.md#events-area) off. A person still on the
Events list, or following a link to an event, then reads that Events are off. It lists the upcoming
events, soonest first, then the undated ones. Skipped and removed events, and those
whose last day has passed, stay off the list.

At the venue the person opens the event on the phone: its state, series, dates,
place, link, key dates, what they wanted to learn, and its notes. The phone changes
nothing on the event. A Talk note the phone holds opens from the event. A Brain note
link reads as its title only, because the phone cannot see the Brain.

### Attach a Talk note

```meta
type: sub-feature
related: [.devbook/domain/capture/features.md#talk-note, .devbook/domain/events/domain.md#attachment-request, .devbook/domain/events/requirements.md#requirement-a-talk-note-is-attached-through-a-desktop]
```

From a [Talk note](../capture/features.md#talk-note) on the phone, the person
picks one event the phone holds and attaches the note to it. The picker offers the
upcoming events first, then the past ones. Skipped events and events the note is
already attached to are left off. With no event held, the note shows no event
section.

The phone sends an [Attachment Request](domain.md#attachment-request). Until a
desktop applies it, the note says whether the request is waiting to send, sent, not
sent with the reason and a retry, or refused. Once a desktop applies it, the event
lists the note, and the note names each event it is attached to.
