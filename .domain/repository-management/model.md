# Repository Management

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
    class RepositoryRegistry {
        <<aggregate root>>
        +Map~RepositoryId,Repository~ repositories
        +TechBaselines baselines
        +PortfolioAnalytics analytics
    }
    class Repository {
        <<entity>>
        +RepositoryId repo_id
        +String repo_name
        +String repo_url
        +String clone_path
        +RepositoryType repo_type
        +String primary_language
        +String team_owner
        +Timestamp last_scanned
        +Timestamp last_commit
        +Integer health_score
    }
    class PackageManifest {
        <<entity>>
        +String file_path
        +List~Package~ packages
    }
    class Package {
        <<value object>>
        +String name
        +Version version
        +Version latest_version
        +Boolean is_outdated
    }
    class TechnologyStackEntry {
        <<value object>>
        +String name
        +Version version
        +String category
    }
    class GitHubMetadata {
        <<value object>>
        +Integer open_issues
        +Integer open_prs
        +Boolean branch_protection_enabled
    }
    class HealthDetails {
        <<value object>>
        +Percentage package_score
        +Percentage github_score
        +Percentage security_score
        +List~String~ recommendations
    }
    class SecurityAlert {
        <<value object>>
        +String vulnerability
        +Severity severity
        +String affected_package
    }
    class HealthScoreSnapshot {
        <<value object>>
        +Date date
        +Integer overall_score
    }
    class RepositoryType {
        <<enumeration>>
        service
        library
        frontend
        cli
        template
    }
    class Severity {
        <<enumeration>>
        critical
        high
        medium
        low
    }

    RepositoryRegistry "1" *-- "0..*" Repository : manages
    Repository "1" *-- "0..*" PackageManifest : has
    Repository "1" *-- "0..*" TechnologyStackEntry : uses
    Repository "1" *-- "1" GitHubMetadata : has
    Repository "1" *-- "1" HealthDetails : scored by
    Repository "1" *-- "0..*" SecurityAlert : flags
    Repository "1" *-- "0..*" HealthScoreSnapshot : records daily
    Repository --> RepositoryType : classified as
    PackageManifest "1" *-- "0..*" Package : contains
    SecurityAlert --> Severity : rated
```

## Relationship notes

- `RepositoryRegistry` is the single aggregate root (global singleton).
  `Repository` and `PackageManifest` are owned entities; the rest are value
  objects. `PackageManifest` identity is `(repo_id, file_path)`.
- Daily history value objects (`PackageFreshnessMetric`, `HealthScoreSnapshot`,
  `GitHubHealthMetric`, `TechnologySnapshot`, see `domain.md`) roll up into
  `PortfolioAnalytics`; only the root writes them.
- `TechBaselines` is a local copy consumed from Technology Stack; repos are
  validated against it without holding the foreign aggregate.
- `repo_id` aligns with the Backlog `repo_ids` and the workspace `repos.json`
  registry so a repo resolves consistently across contexts.
