# Domain Model: Environment

```meta
status: draft
```

> Structural view of the domain model for this bounded context: aggregates,
> entities, value objects, and their relationships. Keep this in sync with
> `domain.md` (which describes responsibilities/invariants in prose) - this
> file focuses on structure and relationships.

## Model diagram

```mermaid
classDiagram
    class EnvironmentCatalog {
        <<aggregate root>>
        +Id id
        +String owner_id
    }
    class Environment {
        <<entity>>
        +Id id
        +String name
        +EnvironmentType type
        +Boolean archived
    }
    class EnvironmentShortcut {
        <<entity>>
        +Id id
        +String display_name
        +String group
        +Integer order
        +Boolean pinned
        +Boolean hidden
    }
    class LaunchTarget {
        <<value object>>
        +String target_type
        +String target_value
    }
    class AccessHint {
        <<value object>>
        +String label
        +String value
    }
    class EnvironmentType {
        <<enumeration>>
        local
        development
        test
        staging
        production
        cloud
        repository
        tooling
    }

    EnvironmentCatalog "1" *-- "0..*" Environment : catalogs
    Environment "1" *-- "0..*" EnvironmentShortcut : exposes
    Environment "1" *-- "1" LaunchTarget : opens
    Environment "1" *-- "0..*" AccessHint : describes access
    Environment --> EnvironmentType : classified as
```

## Relationship notes

- `EnvironmentCatalog` owns the user's quick-access configuration.
- `Environment` is an owned entity because it has identity inside the catalog and
  can have multiple shortcuts.
- `LaunchTarget` and `AccessHint` are value objects. `AccessHint` never stores
  secrets, only references or reminders.