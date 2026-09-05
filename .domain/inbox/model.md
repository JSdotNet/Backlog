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
        +String source_url
        +Timestamp captured_at
        +Timestamp received_at
        +InboxStatus status
        +Date deferred_until
        +ContentKind kind
        +ParaLean para_lean
    }
    class ContentKind {
        <<enumeration>>
        text
        article
        link
        youtube
        image
        document
        email
        code
        voice
        claude_artifact
    }
    class ParaLean {
        <<enumeration>>
        projects
        areas
        resources
        archive
    }
    class Source {
        <<value object>>
        +CaptureSource channel
        +String person
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
    InboxItem --> ContentKind : is a
    InboxItem --> ParaLean : leans towards (optional)
    InboxItem "1" *-- "1" Source : came from
    Source --> CaptureSource : arrived through
    InboxItem "1" *-- "0..*" Tag : tagged with
    InboxItem "1" *-- "0..1" RoutingTarget : routed to
```

## Relationship notes

- `InboxItem` is the aggregate root; `Tag`, `RoutingTarget` and `Source` are
  owned value objects.
- `Source` carries the `CaptureSource` channel (mirrored from Capture) and the
  optional person who shared the item; the channel is no longer a bare field on
  the root.
- `ContentKind` says what the content is; `CaptureSource` says how it arrived.
  The two are independent — the same video can arrive through any channel.
- `ParaLean` is optional and is a reading aid over the unprocessed queue; it
  never stands in for `RoutingTarget`.
- `captured_at` is set by Capture and preserved; `received_at` is set by the
  Inbox on intake — the two are kept distinct on purpose.
- Routing does not embed the target aggregate; it records the destination
  (`domain`, optional `repo_id`) and hands off via `ItemTriaged`. Tasks and
  Second Brain own the created entities.
