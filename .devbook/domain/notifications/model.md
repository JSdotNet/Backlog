# Model

```meta
type: model
status: draft
related: [.devbook/domain/notifications/domain.md#notification, .devbook/domain/notifications/domain.md#reply, .devbook/domain/notifications/domain.md#answer]
```

> Structural view of the domain model for this bounded context: aggregates,
> entities, value objects, and their relationships. Keep this in sync with
> `domain.md` (which describes responsibilities in prose) — this file focuses on
> structure and relationships.

## Model diagram

```mermaid
classDiagram
    class Notification {
        <<aggregate root>>
        +NotificationId Id
        +string OwnerInstance
        +NotificationKind Kind
        +Severity Severity
        +string Title
        +string Summary
        +string Body
        +bool Private
        +string DeepLink
        +NotificationState State
        +Source Source
        +Subject? Subject
        +AnswerSpec? AnswerSpec
        +Answer? Answer
    }

    class Source {
        <<value object>>
        +string Boundary
        +string Key
    }

    class Subject {
        <<value object>>
        +string Boundary
        +string Type
        +string Id
    }

    class AnswerSpec {
        <<value object>>
        +string Shape
        +AnswerChoice[] Choices
        +int? Minimum
        +int? Maximum
        +string Placeholder
        +int? MaxLength
        +bool AnswerOnOwner
    }

    class Answer {
        <<value object>>
        +string[] ChoiceIds
        +string OpenText
    }

    class NotificationKind {
        <<enumeration>>
        Alert
        Question
    }

    class Severity {
        <<enumeration>>
        Info
        Warning
        Critical
    }

    class NotificationState {
        <<enumeration>>
        Open
        Acknowledged
        Answered
        Closed
    }

    class Reply {
        <<aggregate root>>
        +NotificationId Notification
        +string OwnerInstance
        +string Device
        +string[] ChoiceIds
        +string OpenText
    }

    Notification "1" *-- "1" Source
    Notification "1" *-- "0..1" Subject
    Notification "1" *-- "0..1" AnswerSpec
    Notification "1" *-- "0..1" Answer
    Notification --> NotificationKind
    Notification --> Severity
    Notification --> NotificationState
    Reply ..> Notification : notification by id
```

## Relationship notes

- A Notification is the consistency boundary for its answer: only its owner
  instance writes it, and only the owner accepts a [Reply](domain.md#reply) as
  its [Answer](domain.md#answer). A question has at most one answer.
- A Reply is its own aggregate. A viewer writes it once and never changes it, and
  it names its notification by id; the owner deletes it once processed.
- The [Subject](domain.md#subject) names an entity another context owns by its
  boundary, type and id. Nothing of that entity is copied.
