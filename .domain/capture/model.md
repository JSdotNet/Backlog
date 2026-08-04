# Domain Model: Capture

> Structural view of the domain model for this bounded context: aggregates,
> entities, value objects, and their relationships. Keep this in sync with
> `domain.md` (which describes responsibilities/invariants in prose) — this
> file focuses on structure and relationships.

## Model diagram

```mermaid
classDiagram
    class Capture {
        <<aggregate root>>
        +Id id
        +CaptureSource source
        +String device_id
        +String title
        +String body_md
        +String source_url
        +Timestamp captured_at
        +Boolean synced
    }
    class SourceMetadata {
        <<value object>>
        +Map context
    }
    class Tag {
        <<value object>>
        +String name
        +Boolean auto_generated
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

    Capture --> CaptureSource : originated from
    Capture "1" *-- "0..1" SourceMetadata : has
    Capture "1" *-- "0..*" Tag : tagged with
```

## Relationship notes

- `Capture` is the aggregate root; there are no independently identifiable child
  entities. `SourceMetadata` and `Tag` are value objects owned by the root.
- `Capture Source` selects which `Source Adapter` produced the capture and
  determines the concrete keys present in `SourceMetadata`.
- Capture holds no reference to the resulting Inbox Item. Delivery is a one-way
  handoff via the `ItemCaptured` event; the Inbox owns the item's identity and
  lifecycle from that point on (see `dependencies.md`).
- Duplicate captures are intentionally allowed: the same logical content from two
  devices produces two distinct `Capture` instances with distinct ids.
