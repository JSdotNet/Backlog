# Model

```meta
type: model
related: [.devbook/domain/events/domain.md#knowledge-event, .devbook/domain/events/domain.md#event-series, .devbook/domain/events/domain.md#attachment-request]
```

> Structural view of the domain model for this bounded context: aggregates,
> value objects, and their relationships. [`domain.md`](domain.md) says what each
> one means and draws the knowledge event's lifecycle.

## Model diagram

```mermaid
classDiagram
    class KnowledgeEvent {
        <<aggregate root>>
        +Guid Id
        +string Title
        +Guid? Series
        +EventDate? Start
        +EventDate? End
        +DateSource? DateSource
        +Place? Place
        +string? Link
        +KnowledgeEventState State
        +SkipReason? SkipReason
        +string? WhatToLearn
        +KeyDate[] KeyDates
        +TicketState? Ticket
        +Cost? Cost
        +NoteLink[] Notes
        +EventTaskLink[] Tasks
        +Verdict? Verdict
    }

    class EventSeries {
        <<aggregate root>>
        +Guid Id
        +string Name
        +string? Organiser
        +string? Link
    }

    class AttachmentRequest {
        <<aggregate root>>
        +KnowledgeEventId Event
        +string InboxNoteId
    }

    class EventDate {
        <<value object>>
        +Date Date
        +Time? Time
        +Offset? UtcOffset
    }

    class Place {
        <<value object>>
        +string? Venue
        +bool IsOnline
    }

    class KeyDate {
        <<value object>>
        +string Label
        +Date Date
    }

    class Cost {
        <<value object>>
        +decimal Amount
        +string Currency
    }

    class NoteLink {
        <<value object>>
        +NoteLinkKind Kind
        +string Reference
        +string Title
    }

    class NoteLinkKind {
        <<enumeration>>
        Brain
        Talk
    }

    class EventTaskLink {
        <<value object>>
        +string TaskId
        +TaskPurpose Purpose
    }

    class KnowledgeEventState {
        <<enumeration>>
        Spotted
        Considering
        Going
        Attended
        Harvested
        Skipped
    }

    class TaskPurpose {
        <<enumeration>>
        Prep
        Harvest
        Linked
    }

    KnowledgeEvent ..> EventSeries : series by id
    KnowledgeEvent "1" *-- "0..2" EventDate
    KnowledgeEvent "1" *-- "0..1" Place
    KnowledgeEvent "1" *-- "many" KeyDate
    KnowledgeEvent "1" *-- "0..1" Cost
    KnowledgeEvent "1" *-- "many" NoteLink
    KnowledgeEvent "1" *-- "many" EventTaskLink
    KnowledgeEvent --> KnowledgeEventState
    NoteLink --> NoteLinkKind
    EventTaskLink --> TaskPurpose
    AttachmentRequest ..> KnowledgeEvent : event by id
```

## Relationship notes

- A Knowledge Event names its Event Series by id. Each is its own aggregate, so
  editing a series never rewrites its editions.
- An Attachment Request names its event by id. The phone writes it once, and a
  desktop turns it into a Note Link and deletes it.
- A Note Link names a Brain note by its path from the Brain's root, or an Inbox
  note by its id. An Event Task Link names a task by its id. The links run one way:
  Tasks, the Brain and the Inbox hold nothing of the event.
- The Skip Reason, Verdict, Ticket State and Date Source are left out of the
  diagram as types. They are plain text or small enums on the event, described
  in [`domain.md`](domain.md#knowledge-event).
