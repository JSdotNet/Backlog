# Domain: Technology Stack

```meta
status: draft
order: ["features.md", "model.md", "flow.md", "dependencies.md", "naming.md"]
```

> One chapter per Aggregate or Domain Service in this bounded context.
> Aggregate chapters include sub-chapters for their owned Entities, Value
> Objects, and Enums. Value Objects/Enums shared across multiple aggregates
> get their own chapter at the end instead of being duplicated.

Technology Stack maintains authoritative definitions of supported technologies,
tools, frameworks, and their version requirements across the portfolio. It is the
single source of truth for "what tools do we use and what versions should we be
on?", enforcing approved baselines, tracking adoption, and managing deprecation.
It provides baselines consumed by
[Dev PC Management](../dev-pc-management/domain.md#aggregate-machine-registry) and
[Repository Management](../repository-management/domain.md#aggregate-repository-registry).

## Aggregate: Technology Registry

```meta
status: draft
related: [.domain/dev-pc-management/domain.md#aggregate-machine-registry, .domain/repository-management/domain.md#aggregate-repository-registry, .arc42/08-crosscutting-concepts.md#shared-data-types]
```

The global singleton that owns all technology definitions, baseline profiles,
deprecation notices, and portfolio adoption analytics. Invariants: every version
referenced in a baseline must match a `Technology` in the registry and be
`>= technology.min_version`; an `unsupported` technology must not appear in any
active baseline; adoption analytics are populated from daily snapshots. All
changes to technologies, baselines, and deprecations go through the root.

### Entities

#### Technology

A single tool, framework, language, or runtime, identified by `technology_id`
(e.g. "dotnet", "nodejs", "react"). Holds `category`, `status`, `min_version`,
`recommended_version`, `latest_available_version`, optional `eol_date` and
`migration_guidance`, owned breaking changes, daily adoption snapshots, ADR
links, and tags. Invariant:
`min_version <= recommended_version <= latest_available_version`; if
`status = deprecated`, `eol_date` and `migration_guidance` are required.

#### Breaking Change

A significant breaking change between versions of a `Technology`: `from_version`,
`description`, `impact_level` (high/medium/low), `migration_effort`
(easy/moderate/hard), and step-by-step `migration_steps`.

#### Deprecated Tech In Use

A deprecated technology still active in the portfolio: `technology_id`,
`usage_count`, `usage_pct`, `eol_date`, `last_updated`, and owned migration
approvals. Tracked so phase-out can be reported and enforced.

#### Migration Approval

An approved exception to remain on deprecated technology: the machine/repo id,
`deprecated_technology`, `target_migration_date`, `reason`, `approved_by`,
`approved_at`.

### Value Objects

#### Technology Baseline

An approved set of versions for a context (Default, Frontend, Backend,
Microservices, dev-pc, repositories): `baseline_name`, `technologies`
(id → version), `last_updated`, `updated_by`, `description`. Invariant: every
version must exist in the registry and be `>= technology.min_version`.

#### Version Constraint

A version alias with semantic meaning: `constraint_name` (lts, latest, stable,
current), the concrete `version`, `description`, and `effective_date`.

#### Deprecation Notice

A formal phase-out announcement: `technology_id`, `announcement_date`,
`eol_date`, `replacement_technology`, `migration_guide_url`, `support_level`
(active/maintenance-only/eol).

#### Adoption Snapshot

A point-in-time adoption view for a technology: `date`, `adoption_count`,
`adoption_pct`, and `version_distribution`. Computed:
`adoption_pct = adoption_count / total_portfolio_count * 100`.

#### Version Usage Metric

Usage distribution of one technology: `date`, `technology_id`,
`version_distribution`, `on_baseline_count`, `on_baseline_pct`.

#### Adoption Trend

Adoption velocity for a technology/version: `technology_id`, optional `version`,
`adoption_velocity`, `days_to_full_adoption`.

#### Compatibility Matrix

Known compatibility between two technologies: `technology_a`, `technology_b`,
`version_mapping` (e.g. .NET 9 → ASP.NET Core 9), and `notes`.

#### Adoption Analytics

Portfolio-wide rollup: daily `portfolio_adoption`, `adoption_trends`,
`version_usage_metrics`, `compatibility_matrix`, and `deprecated_in_use`.

### Enums

#### Tech Status

Lifecycle of a technology: `stable`, `beta`, `deprecated`, `unsupported`.

#### Tech Category

Kind of technology: `language`, `runtime`, `framework`, `tool`, `ide`, `utility`.

## Domain Service: Adoption Tracking

```meta
status: draft
related: [.domain/monitoring/domain.md#aggregate-progress-signal]
```

Collects daily adoption from Dev PC tool reports and Repository tech-stack scans,
computes portfolio coverage, version distribution, adoption velocity, and
sprawl, and flags deprecated tech still in use. It is a service because it
aggregates data reported by other contexts into portfolio analytics that no
single `Technology` owns. Invocation semantics: scheduled/event-triggered analytics service.

## Domain Service: Deprecation Management

```meta
status: draft
```

Drives the deprecation lifecycle: issues `Deprecation Notice`s with EOL dates and
migration guidance, enforces minimum versions, tracks approved exceptions, and on
EOL flags remaining machines/repos for forced migration. It is a service because
it coordinates policy across many technologies, baselines, and external
consumers. Invocation semantics: policy service triggered by baseline changes, EOL thresholds, and approval decisions.

## Shared Enums

```meta
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Technology Stack has a single aggregate; `Tech Status` and `Tech Category` are
documented under it. This chapter is reserved for future cross-aggregate enums.
