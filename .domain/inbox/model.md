# Inbox

```meta
type: model
status: draft
```

> Structural view of the domain model for this bounded context: aggregates,
> entities, value objects, and their relationships. Keep this in sync with
> `domain.md` (which describes responsibilities/invariants in prose) — this
> file focuses on structure and relationships.

## Model diagram

```mermaid
classDiagram
    class InboxItem {
        <<aggregate root>>
        +Id id
        +String title
        +String body_md
        +CaptureSource source
        +String source_url
        +Timestamp captured_at
        +Timestamp received_at
        +InboxStatus status
        +Date deferred_until
    }
    class InboxStatus {
        <<enumeration>>
        unprocessed
        triaged
        deferred
        archived
    }
    class CaptureSource {
        <<enumeration>>
        mobile
        youtube
        website
        email
        web_clipper
        ide
        manual
    }
    class RoutingTarget {
        <<value object>>
        +String domain
        +String repo_id
        +Timestamp routed_at
    }
    class Tag {
        <<value object>>
        +String name
        +Boolean auto_generated
    }

    InboxItem --> InboxStatus : has status
    InboxItem --> CaptureSource : originated from
    InboxItem "1" *-- "0..*" Tag : tagged with
    InboxItem "1" *-- "0..1" RoutingTarget : routed to
```

## Relationship notes

- `InboxItem` is the aggregate root; `Tag` and `RoutingTarget` are owned value
  objects.
- `captured_at` is set by Capture and preserved; `received_at` is set by the
  Inbox on intake — the two are kept distinct on purpose.
- Routing does not embed the target aggregate; it records the destination
  (`domain`, optional `repo_id`) and hands off via `ItemTriaged`. Tasks and
  Second Brain own the created entities.
