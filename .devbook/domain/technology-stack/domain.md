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

## Technology Registry

```meta
type: aggregate
status: draft
related: [.devbook/domain/dev-pc-management/domain.md#machine-registry, .devbook/domain/repository-management/domain.md#repository-registry, .devbook/arc42/08-crosscutting-concepts.md#shared-data-types]
aliases: [TechnologyRegistry]
```

The global singleton that owns all technology definitions, baseline profiles,
deprecation notices, and portfolio adoption analytics. All changes to
technologies, baselines, and deprecations go through the root.

Invariants:

- Every version referenced in a baseline matches a `Technology` in the registry
  and is `>= technology.min_version`.
- An `unsupported` technology appears in no active baseline.
- Adoption analytics are populated from daily snapshots.

### Technology

```meta
type: entity
status: draft
aliases: [technology_id]
```

A single tool, framework, language, or runtime, identified by `technology_id`
(e.g. "dotnet", "nodejs", "react"). It holds:

- `category` and `status`.
- `min_version`, `recommended_version`, and `latest_available_version`.
- An optional `eol_date` and `migration_guidance`.
- Its owned breaking changes and daily adoption snapshots.
- ADR links and tags.

Invariants:

- `min_version <= recommended_version <= latest_available_version`.
- When `status = deprecated`, `eol_date` and `migration_guidance` are required.

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
aliases: [TechnologyBaseline, TeamToolsBaseline, TechBaselines]
```

An approved set of versions for a context (Default, Frontend, Backend,
Microservices, dev-pc, repositories): `baseline_name`, `technologies`
(id → version), `last_updated`, `updated_by`, `description`. Its versions obey
the [Technology Registry](#technology-registry)'s first invariant.

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
aliases: [DeprecationNotice]
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
aliases: [TechStatus]
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
related: [.devbook/domain/monitoring/domain.md#progress-signal]
```

Collects daily adoption from Dev PC Management tool-version reports and
Repository Management tech-stack scans. From them it:

- computes portfolio coverage, version distribution, and adoption velocity;
- measures [Technology Sprawl](#technology-sprawl);
- flags deprecated technology still in use.

It is a service because it aggregates data reported by other contexts into
portfolio analytics that no single `Technology` owns. Invocation semantics:
scheduled/event-triggered analytics service. The
[adoption tracking flow](flow.md#adoption-tracking) shows its inputs and outputs.

## Deprecation Management

```meta
type: domain-service
status: draft
```

Drives the deprecation lifecycle. It:

- issues `Deprecation Notice`s with end-of-life (EOL) dates and migration guidance;
- enforces minimum versions;
- tracks approved exceptions as `Migration Approval`s;
- at EOL, flags every machine and repository still on the technology as one that
  must migrate.

It is a service because it coordinates policy across many technologies,
baselines, and external consumers. Invocation semantics: policy service
triggered by baseline changes, EOL thresholds, and approval decisions.

## Shared Enums

```meta
type: shared-enums
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Technology Stack has a single aggregate; `Tech Status` and `Tech Category` are
documented under it. This chapter is reserved for future cross-aggregate enums.

## Ubiquitous Language

```meta
status: draft
type: ubiquitous-language
```

> The terms this context owns that are not chapters above.

### Technology Sprawl

```meta
status: draft
type: term
aliases: [sprawl]
related: [.devbook/domain/technology-stack/domain.md#adoption-tracking, .devbook/domain/technology-stack/domain.md#technology-baseline]
```

The spread of technologies and versions in use across the portfolio beyond what
the baselines approve. It counts overlapping technologies used for the same
purpose and several versions of one technology in use at once.
[Adoption Tracking](#adoption-tracking) measures it from the same reports it
uses for coverage.
