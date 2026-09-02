# Repository Management

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

Repository Management maintains a registry of development repositories and tracks
their health across the portfolio — package versions, dependencies, technology
stack, GitHub issues and PRs, and a computed health score — so outdated or
at-risk repos surface without manual investigation. It consumes baselines from
[Technology Stack](../technology-stack/domain.md#technology-registry).

## Repository Registry

```meta
type: aggregate
status: draft
related: [.domain/technology-stack/domain.md#technology-registry, .domain/monitoring/domain.md#progress-signal, .arc42/08-crosscutting-concepts.md#shared-data-types]
```

The global singleton that owns all registered repositories, technology/dependency
baselines, and portfolio analytics. Invariants: a repository must be registered
before health metrics are calculated; `health_score` is computed from component
scores (package, GitHub, coverage, security); package manifests are populated on
scan with outdated packages flagged; GitHub metadata is synced on scan; and each
scan generates a health-score snapshot. All repository mutations go through the
root.

### Repository

```meta
type: entity
status: draft
```

A single software repository identified by `RepositoryId` (e.g. "org/repo-name").
Holds metadata (`repo_url`, `clone_path`, `repo_type`, `primary_language`,
`team_owner`), scan/commit timestamps, owned package manifests, detected
technologies, GitHub metadata, health score and breakdown, security alerts, ADR
links, tags, and daily freshness/health/GitHub/tech-adoption history. Invariant:
technologies must match `baselines.repo_tech_baseline` or trigger deprecation
warnings.

### Package Manifest

```meta
type: entity
status: draft
```

Parsed dependencies from a single manifest file, identified by
`(repo_id, file_path)`. Owns the list of `Package` value objects parsed from that
file (e.g. `package.json`, `*.csproj`, `pyproject.toml`, `go.mod`).

### Package

```meta
type: value-object
status: draft
```

A single dependency: `name`, `version`, `constraint`, `latest_version`,
`is_outdated`. Computed: `is_outdated = version < latest_version` by semver.

### Technology Stack Entry

```meta
type: value-object
status: draft
```

A detected technology in the repo: `name`, `version`, `category`
(runtime/framework/tool/language/platform).

### GitHub Metadata

```meta
type: value-object
status: draft
```

A snapshot of GitHub stats: `stars`, `forks`, `watchers`, `open_issues`,
`open_prs`, `branch_protection_enabled`, `last_update`.

### Health Details

```meta
type: value-object
status: draft
```

Health-score breakdown: `package_score`, `github_score`, `coverage_score`,
`security_score`, and actionable `recommendations`. Computed:
`overall_score = weighted_average(...)`.

### Security Alert

```meta
type: value-object
status: draft
```

A vulnerability from GitHub Advanced Security: `alert_id`, `vulnerability`,
`severity`, `affected_package`, `fixed_version`, `first_detected`,
`last_updated`.

### Package Freshness Metric

```meta
type: value-object
status: draft
```

Daily freshness: `date`, `outdated_count`, `total_count`, `freshness_pct`
= `(total - outdated) / total * 100`.

### Health Score Snapshot

```meta
type: value-object
status: draft
```

Health at a point in time: `date`, `overall_score`, `package_score`,
`github_score`, `coverage_score`.

### GitHub Health Metric

```meta
type: value-object
status: draft
```

Daily issue/PR aging: `date`, `open_issues`, `avg_issue_age_days`, `open_prs`,
`avg_pr_age_days`.

### Technology Snapshot

```meta
type: value-object
status: draft
```

Tech-stack snapshot at a date: `date`, `technologies`.

### Tech Baselines

```meta
type: value-object
status: draft
```

Portfolio technology/dependency baselines: language, framework, and dependency
baselines plus `deprecated_tech`. Consumed from Technology Stack
(see `dependencies.md`).

### Portfolio Analytics

```meta
type: value-object
status: draft
```

Portfolio-wide rollup: daily `portfolio_health`, freshness/health `TrendMetrics`,
and `TechAdoptionTrends`.

### Repository Type

```meta
type: enum
status: draft
```

Kind of repo: `service`, `library`, `frontend`, `cli`, `template`.

### Severity

```meta
type: enum
status: draft
```

Security alert severity: `critical`, `high`, `medium`, `low`.

## Repository Scan

```meta
type: domain-service
status: draft
related: [.domain/technology-stack/domain.md#technology-registry, .arc42/06-runtime-view.md#repository-baseline-scan-and-health-signal]
```

Runs scheduled or on-demand scans of a repository: parses dependency manifests,
detects the technology stack, fetches GitHub metadata and security alerts, and
recomputes the health score from package freshness, GitHub backlog, coverage, and
security. It is a service because it coordinates external systems (file system,
GitHub, package registries) to refresh the aggregate. Invocation semantics: scheduled or on-demand orchestration service.

## Bulk Operations

```meta
type: domain-service
status: draft
related: [.domain/tasks/domain.md#task]
```

Coordinates portfolio-wide actions: queue package updates across repos, run
synchronized upgrades (e.g. "upgrade all ASP.NET Core projects to 9.0") via
GitHub Actions, and generate migration guides. It is a service because it spans
many repositories rather than a single aggregate. Invocation semantics: command-invoked orchestration service.

## Shared Enums

```meta
type: shared-enums
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Repository Management has a single aggregate; `Repository Type` and `Severity`
are documented under it. This chapter is reserved for future cross-aggregate
enums.
