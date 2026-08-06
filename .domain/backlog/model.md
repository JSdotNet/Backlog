# Domain Model: Backlog Management

```meta
status: draft
```

> Structural view of the domain model for this bounded context: aggregates,
> entities, value objects, and their relationships. Keep this in sync with
> `domain.md` (which describes responsibilities/invariants in prose) — this
> file focuses on structure and relationships.

## Model diagram

```mermaid
classDiagram
    class BacklogEntry {
        <<aggregate root>>
        +Id id
        +String title
        +String content_md
        +List~String~ repo_ids
        +EntryType type
        +EntryStatus status
        +Priority priority
        +List~String~ tags
        +String area
        +Integer order
        +String source_inbox_id
        +Timestamp created_at
    }
    class SubItem {
        <<entity>>
        +Id id
        +String title
        +SubItemStatus status
        +String notes
        +Integer order
    }
    class ProjectionRef {
        <<value object>>
        +String repo_id
        +String external_id
        +String target_type
    }
    class UsageEvent {
        <<value object>>
        +Timestamp timestamp
        +String action
    }
    class EntryType {
        <<enumeration>>
        prompt
        task
        idea
        follow_up
    }
    class EntryStatus {
        <<enumeration>>
        draft
        ready
        in_progress
        done
        archived
    }
    class Priority {
        <<enumeration>>
        low
        medium
        high
        critical
    }
    class SubItemStatus {
        <<enumeration>>
        pending
        done
    }

    BacklogEntry "1" *-- "0..*" SubItem : contains
    BacklogEntry "1" *-- "0..*" UsageEvent : records
    BacklogEntry "1" *-- "0..*" ProjectionRef : projects to
    BacklogEntry --> EntryType : classified as
    BacklogEntry --> EntryStatus : has status
    BacklogEntry --> Priority : ranked by
    SubItem --> SubItemStatus : has status
```

## Relationship notes

- `BacklogEntry` is the aggregate root and the only consistency boundary.
  `SubItem` is an owned entity (identity within the aggregate only);
  `ProjectionRef` and `UsageEvent` are immutable value objects.
- `repo_ids` is a plain list of repository identifiers, not object references —
  Backlog stays decoupled from Repository Management. Projection turns each
  `repo_id` into a `ProjectionRef` when the entry starts work.
- `area` and `order` are plain scalars on the root, not separate value objects —
  they place the entry in the person's own working set (self-chosen grouping,
  manual rank) rather than describing the entry's business state, so they carry
  no relationships to other types.
