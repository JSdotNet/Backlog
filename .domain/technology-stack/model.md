# Domain Model: Technology Stack

> Structural view of the domain model for this bounded context: aggregates,
> entities, value objects, and their relationships. Keep this in sync with
> `domain.md` (which describes responsibilities/invariants in prose) — this
> file focuses on structure and relationships.

## Model diagram

```mermaid
classDiagram
    class TechnologyRegistry {
        <<aggregate root>>
        +Map~TechnologyId,Technology~ technologies
        +Map~String,TechnologyBaseline~ baselines
        +List~DeprecationNotice~ deprecations
        +AdoptionAnalytics adoption_metrics
    }
    class Technology {
        <<entity>>
        +TechnologyId technology_id
        +String technology_name
        +TechCategory category
        +TechStatus status
        +Version min_version
        +Version recommended_version
        +Version latest_available_version
        +Date eol_date
        +String migration_guidance
    }
    class BreakingChange {
        <<entity>>
        +Version from_version
        +String description
        +String impact_level
        +String migration_effort
    }
    class DeprecatedTechInUse {
        <<entity>>
        +TechnologyId technology_id
        +Integer usage_count
        +Date eol_date
    }
    class MigrationApproval {
        <<entity>>
        +String machine_id_or_repo_id
        +Date target_migration_date
        +String approved_by
    }
    class TechnologyBaseline {
        <<value object>>
        +String baseline_name
        +Map~TechnologyId,Version~ technologies
    }
    class DeprecationNotice {
        <<value object>>
        +TechnologyId technology_id
        +Date eol_date
        +TechnologyId replacement_technology
    }
    class AdoptionSnapshot {
        <<value object>>
        +Date date
        +Integer adoption_count
        +Map~Version,Integer~ version_distribution
    }
    class AdoptionAnalytics {
        <<value object>>
        +Map portfolio_adoption
        +List~CompatibilityMatrix~ compatibility_matrix
    }
    class TechStatus {
        <<enumeration>>
        stable
        beta
        deprecated
        unsupported
    }
    class TechCategory {
        <<enumeration>>
        language
        runtime
        framework
        tool
        ide
        utility
    }

    TechnologyRegistry "1" *-- "0..*" Technology : owns
    TechnologyRegistry "1" *-- "0..*" TechnologyBaseline : defines
    TechnologyRegistry "1" *-- "0..*" DeprecationNotice : records
    TechnologyRegistry "1" *-- "1" AdoptionAnalytics : aggregates
    AdoptionAnalytics "1" *-- "0..*" DeprecatedTechInUse : lists
    DeprecatedTechInUse "1" *-- "0..*" MigrationApproval : tracks
    Technology "1" *-- "0..*" BreakingChange : documents
    Technology "1" *-- "0..*" AdoptionSnapshot : records daily
    Technology --> TechStatus : has status
    Technology --> TechCategory : classified as
```

## Relationship notes

- `TechnologyRegistry` is the single aggregate root (global singleton).
  `Technology`, `BreakingChange`, `DeprecatedTechInUse`, and `MigrationApproval`
  are owned entities; the remaining classes are immutable value objects.
- `AdoptionSnapshot`, `VersionUsageMetric`, `AdoptionTrend`, and
  `CompatibilityMatrix` (see `domain.md`) roll up into `AdoptionAnalytics`; only
  the aggregate root writes them.
- Baselines and adoption reference machines/repos by id only; Dev PC and
  Repository Management report versions inward and consume baselines outward, so
  no foreign aggregate is held here.
