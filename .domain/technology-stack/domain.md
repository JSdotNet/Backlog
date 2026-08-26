# Technology Stack

```meta
type: domain
status: draft
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context; each chapter's
> `type` records which of those it is. An Aggregate's owned Entities, Value
> Objects, and Enums are chapters directly beneath it, typed `entity`,
> `value-object`, and `enum`. Value Objects/Enums shared across multiple
> aggregates get their own chapter at the end instead of being duplicated.

Technology Stack maintains authoritative definitions of supported technologies,
tools, frameworks, and their version requirements across the portfolio. It is the
single source of truth for "what tools do we use and what versions should we be
on?", enforcing approved baselines, tracking adoption, and managing deprecation.
It provides baselines consumed by
[Dev PC Management](../dev-pc-management/domain.md#machine-registry) and
[Repository Management](../repository-management/domain.md#repository-registry).

## Technology Registry

```meta
type: aggregate
status: draft
related: [.domain/dev-pc-management/domain.md#machine-registry, .domain/repository-management/domain.md#repository-registry, .arc42/08-crosscutting-concepts.md#shared-data-types]
```

The global singleton that owns all technology definitions, baseline profiles,
deprecation notices, and portfolio adoption analytics. Invariants: every version
referenced in a baseline must match a `Technology` in the registry and be
`>= technology.min_version`; an `unsupported` technology must not appear in any
active baseline; adoption analytics are populated from daily snapshots. All
changes to technologies, baselines, and deprecations go through the root.

### Technology

```meta
type: entity
status: draft
```

A single tool, framework, language, or runtime, identified by `technology_id`
(e.g. "dotnet", "nodejs", "react"). Holds `category`, `status`, `min_version`,
`recommended_version`, `latest_available_version`, optional `eol_date` and
`migration_guidance`, owned breaking changes, daily adoption snapshots, ADR
links, and tags. Invariant:
`min_version <= recommended_version <= latest_available_version`; if
`status = deprecated`, `eol_date` and `migration_guidance` are required.

### Breaking Change

```meta
type: entity
status: draft
```

A significant breaking change between versions of a `Technology`: `from_version`,
`description`, `impact_level` (high/medium/low), `migration_effort`
(easy/moderate/hard), and step-by-step `migration_steps`.

### Deprecated Tech In Use

```meta
type: entity
status: draft
```

A deprecated technology still active in the portfolio: `technology_id`,
`usage_count`, `usage_pct`, `eol_date`, `last_updated`, and owned migration
approvals. Tracked so phase-out can be reported and enforced.

### Migration Approval

```meta
type: entity
status: draft
```

An approved exception to remain on deprecated technology: the machine/repo id,
`deprecated_technology`, `target_migration_date`, `reason`, `approved_by`,
`approved_at`.

### Technology Baseline

```meta
type: value-object
status: draft
```

An approved set of versions for a context (Default, Frontend, Backend,
Microservices, dev-pc, repositories): `baseline_name`, `technologies`
(id → version), `last_updated`, `updated_by`, `description`. Invariant: every
version must exist in the registry and be `>= technology.min_version`.

### Version Constraint

```meta
type: value-object
status: draft
```

A version alias with semantic meaning: `constraint_name` (lts, latest, stable,
current), the concrete `version`, `description`, and `effective_date`.

### Deprecation Notice

```meta
type: value-object
status: draft
```

A formal phase-out announcement: `technology_id`, `announcement_date`,
`eol_date`, `replacement_technology`, `migration_guide_url`, `support_level`
(active/maintenance-only/eol).

### Adoption Snapshot

```meta
type: value-object
status: draft
```

A point-in-time adoption view for a technology: `date`, `adoption_count`,
`adoption_pct`, and `version_distribution`. Computed:
`adoption_pct = adoption_count / total_portfolio_count * 100`.

### Version Usage Metric

```meta
type: value-object
status: draft
```

Usage distribution of one technology: `date`, `technology_id`,
`version_distribution`, `on_baseline_count`, `on_baseline_pct`.

### Adoption Trend

```meta
type: value-object
status: draft
```

Adoption velocity for a technology/version: `technology_id`, optional `version`,
`adoption_velocity`, `days_to_full_adoption`.

### Compatibility Matrix

```meta
type: value-object
status: draft
```

Known compatibility between two technologies: `technology_a`, `technology_b`,
`version_mapping` (e.g. .NET 9 → ASP.NET Core 9), and `notes`.

### Adoption Analytics

```meta
type: value-object
status: draft
```

Portfolio-wide rollup: daily `portfolio_adoption`, `adoption_trends`,
`version_usage_metrics`, `compatibility_matrix`, and `deprecated_in_use`.

### Tech Status

```meta
type: enum
status: draft
```

Lifecycle of a technology: `stable`, `beta`, `deprecated`, `unsupported`.

### Tech Category

```meta
type: enum
status: draft
```

Kind of technology: `language`, `runtime`, `framework`, `tool`, `ide`, `utility`.

## Adoption Tracking

```meta
type: domain-service
status: draft
related: [.domain/monitoring/domain.md#progress-signal]
```

Collects daily adoption from Dev PC tool reports and Repository tech-stack scans,
computes portfolio coverage, version distribution, adoption velocity, and
sprawl, and flags deprecated tech still in use. It is a service because it
aggregates data reported by other contexts into portfolio analytics that no
single `Technology` owns. Invocation semantics: scheduled/event-triggered analytics service.

## Deprecation Management

```meta
type: domain-service
status: draft
```

Drives the deprecation lifecycle: issues `Deprecation Notice`s with EOL dates and
migration guidance, enforces minimum versions, tracks approved exceptions, and on
EOL flags remaining machines/repos for forced migration. It is a service because
it coordinates policy across many technologies, baselines, and external
consumers. Invocation semantics: policy service triggered by baseline changes, EOL thresholds, and approval decisions.

## Shared Enums

```meta
type: shared-enums
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Technology Stack has a single aggregate; `Tech Status` and `Tech Category` are
documented under it. This chapter is reserved for future cross-aggregate enums.
