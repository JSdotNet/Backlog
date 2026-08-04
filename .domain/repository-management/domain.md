# Domain: Repository Management

> One chapter per Aggregate or Domain Service in this bounded context.
> Aggregate chapters include sub-chapters for their owned Entities, Value
> Objects, and Enums. Value Objects/Enums shared across multiple aggregates
> get their own chapter at the end instead of being duplicated.

Repository Management maintains a registry of development repositories and tracks
their health across the portfolio — package versions, dependencies, technology
stack, GitHub issues and PRs, and a computed health score — so outdated or
at-risk repos surface without manual investigation. It consumes baselines from
[Technology Stack](../technology-stack/domain.md#aggregate-technology-registry).

## Aggregate: Repository Registry

```meta
status: draft
related: [.domain/technology-stack/domain.md#aggregate-technology-registry, .domain/monitoring/domain.md#aggregate-progress-signal, .arc42/08-crosscutting-concepts.md#shared-data-types]
```

The global singleton that owns all registered repositories, technology/dependency
baselines, and portfolio analytics. Invariants: a repository must be registered
before health metrics are calculated; `health_score` is computed from component
scores (package, GitHub, coverage, security); package manifests are populated on
scan with outdated packages flagged; GitHub metadata is synced on scan; and each
scan generates a health-score snapshot. All repository mutations go through the
root.

### Entities

#### Repository

A single software repository identified by `RepositoryId` (e.g. "org/repo-name").
Holds metadata (`repo_url`, `clone_path`, `repo_type`, `primary_language`,
`team_owner`), scan/commit timestamps, owned package manifests, detected
technologies, GitHub metadata, health score and breakdown, security alerts, ADR
links, tags, and daily freshness/health/GitHub/tech-adoption history. Invariant:
technologies must match `baselines.repo_tech_baseline` or trigger deprecation
warnings.

#### Package Manifest

Parsed dependencies from a single manifest file, identified by
`(repo_id, file_path)`. Owns the list of `Package` value objects parsed from that
file (e.g. `package.json`, `*.csproj`, `pyproject.toml`, `go.mod`).

### Value Objects

#### Package

A single dependency: `name`, `version`, `constraint`, `latest_version`,
`is_outdated`. Computed: `is_outdated = version < latest_version` by semver.

#### Technology Stack Entry

A detected technology in the repo: `name`, `version`, `category`
(runtime/framework/tool/language/platform).

#### GitHub Metadata

A snapshot of GitHub stats: `stars`, `forks`, `watchers`, `open_issues`,
`open_prs`, `branch_protection_enabled`, `last_update`.

#### Health Details

Health-score breakdown: `package_score`, `github_score`, `coverage_score`,
`security_score`, and actionable `recommendations`. Computed:
`overall_score = weighted_average(...)`.

#### Security Alert

A vulnerability from GitHub Advanced Security: `alert_id`, `vulnerability`,
`severity`, `affected_package`, `fixed_version`, `first_detected`,
`last_updated`.

#### Package Freshness Metric

Daily freshness: `date`, `outdated_count`, `total_count`, `freshness_pct`
= `(total - outdated) / total * 100`.

#### Health Score Snapshot

Health at a point in time: `date`, `overall_score`, `package_score`,
`github_score`, `coverage_score`.

#### GitHub Health Metric

Daily issue/PR aging: `date`, `open_issues`, `avg_issue_age_days`, `open_prs`,
`avg_pr_age_days`.

#### Technology Snapshot

Tech-stack snapshot at a date: `date`, `technologies`.

#### Tech Baselines

Portfolio technology/dependency baselines: language, framework, and dependency
baselines plus `deprecated_tech`. Consumed from Technology Stack
(see `dependencies.md`).

#### Portfolio Analytics

Portfolio-wide rollup: daily `portfolio_health`, freshness/health `TrendMetrics`,
and `TechAdoptionTrends`.

### Enums

#### Repository Type

Kind of repo: `service`, `library`, `frontend`, `cli`, `template`.

#### Severity

Security alert severity: `critical`, `high`, `medium`, `low`.

## Domain Service: Repository Scan

```meta
status: draft
related: [.domain/technology-stack/domain.md#aggregate-technology-registry, .arc42/06-runtime-view.md#repository-baseline-scan-and-health-signal]
```

Runs scheduled or on-demand scans of a repository: parses dependency manifests,
detects the technology stack, fetches GitHub metadata and security alerts, and
recomputes the health score from package freshness, GitHub backlog, coverage, and
security. It is a service because it coordinates external systems (file system,
GitHub, package registries) to refresh the aggregate. Invocation semantics: scheduled or on-demand orchestration service.

## Domain Service: Bulk Operations

```meta
status: draft
related: [.domain/backlog/domain.md#aggregate-backlog-entry]
```

Coordinates portfolio-wide actions: queue package updates across repos, run
synchronized upgrades (e.g. "upgrade all ASP.NET Core projects to 9.0") via
GitHub Actions, and generate migration guides. It is a service because it spans
many repositories rather than a single aggregate. Invocation semantics: command-invoked orchestration service.

## Shared Enums

```meta
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Repository Management has a single aggregate; `Repository Type` and `Severity`
are documented under it. This chapter is reserved for future cross-aggregate
enums.
