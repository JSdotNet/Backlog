# Productivity

```meta
type: model
status: draft
```

> Structural view of the domain model for this bounded context: aggregates,
> entities, value objects, and their relationships. Keep this in sync with
> `domain.md` (which describes responsibilities/invariants in prose) - this
> file focuses on structure and relationships.

## Model diagram

```mermaid
classDiagram
    class ProductivityLedger {
        <<aggregate root>>
        +Id id
        +String owner_id
    }
    class ProductivityEntry {
        <<entity>>
        +Id id
        +ActivityKind activity_kind
        +String ai_tool
        +Timestamp started_at
        +Timestamp completed_at
        +String outcome_ref
    }
    class WorkSubjectRef {
        <<value object>>
        +String subject_type
        +String subject_id
    }
    class ProductivityMetric {
        <<value object>>
        +String metric_name
        +Decimal value
        +String unit
        +DateRange period
    }
    class ActivityKind {
        <<enumeration>>
        planning
        coding
        review
        research
        summarization
        documentation
        automation
        other
    }

    ProductivityLedger "1" *-- "0..*" ProductivityEntry : records
    ProductivityEntry "0..1" *-- "1" WorkSubjectRef : references
    ProductivityEntry --> ActivityKind : categorized as
    ProductivityLedger ..> ProductivityMetric : derives
```

## Relationship notes

- `ProductivityLedger` is the aggregate root and owns append-only
  `ProductivityEntry` records.
- `WorkSubjectRef` is opaque by design so Productivity can link to tasks,
  sessions, issues, pull requests, commits, or notes without owning those models.
- `ProductivityMetric` is derived from ledger entries and is not stored as the
  authoritative activity record.