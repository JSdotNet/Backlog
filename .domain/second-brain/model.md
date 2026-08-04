# Domain Model: Second Brain

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
    class KnowledgeNote {
        <<aggregate root>>
        +Id id
        +String title
        +String body_md
        +String topic
        +PARACategory category
        +NoteSource source
        +Timestamp created_at
        +Timestamp updated_at
    }
    class ProjectRef {
        <<value object>>
        +String repo_id
        +String project_name
    }
    class Tag {
        <<value object>>
        +String name
    }
    class BacklogLink {
        <<value object>>
        +String backlog_entry_id
        +String link_type
        +Timestamp linked_at
    }
    class PARACategory {
        <<enumeration>>
        projects
        areas
        resources
        archive
    }
    class NoteSource {
        <<enumeration>>
        inbox
        manual
        import
    }

    KnowledgeNote --> PARACategory : organized under
    KnowledgeNote --> NoteSource : originated from
    KnowledgeNote "1" *-- "0..*" ProjectRef : scoped to
    KnowledgeNote "1" *-- "0..*" Tag : tagged with
    KnowledgeNote "1" *-- "0..*" BacklogLink : linked to
```

## Relationship notes

- `KnowledgeNote` is the aggregate root; `ProjectRef`, `Tag`, and `BacklogLink`
  are owned value objects. There are no separately identified child entities.
- `BacklogLink` references a Backlog Entry by id only, never by object reference,
  so the two contexts stay decoupled; the Cross-Linking service keeps both
  directions consistent.
