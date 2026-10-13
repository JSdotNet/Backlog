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

## Repository Registry

```meta
type: aggregate
status: draft
related: [.devbook/domain/technology-stack/domain.md#technology-registry, .devbook/domain/monitoring/domain.md#progress-signal, .devbook/arc42/08-crosscutting-concepts.md#shared-data-types]
aliases: [RepositoryRegistry]
```

The global singleton that owns all registered repositories, the technology and
dependency baselines, and portfolio analytics. All repository mutations go
through the root.

Besides the repositories, the registry remembers what became of names it no
longer holds, because entries elsewhere still write them:

- a [Rename Record](#rename-record) for each rename, so an old name keeps meaning
  the repository it became;
- a [Removal Record](#removal-record) for each removal, so a removed name is not
  registered again by accident.

A **placeholder** is a repository an import registered because a plan named it
before anyone had configured it. Renaming the placeholder to the real
repository's owner and name merges the two, and the real repository keeps its
own settings ([Following a repository rename](features.md#following-a-repository-rename)).

Scanning and health scoring are planned and not built. Their invariants are:

- A repository must be registered before health metrics are calculated.
- `health_score` is computed from component scores: package, GitHub, coverage,
  and security.
- Package manifests are populated on scan, with outdated packages flagged.
- GitHub metadata is synced on scan.
- Each scan generates a health-score snapshot.

### Repository

```meta
type: entity
status: draft
aliases: [repo_id, repo_ids]
```

A single software repository, identified by `RepositoryId`: its owner and name on
GitHub, such as "org/repo-name". The registry holds for each repository:

- its [alias](#repository-alias);
- its owner and name;
- an optional local clone directory;
- its [identity colour](features.md#repository-identity-colour), as one of the
  colours the design system sanctions;
- the account it is worked as, and optionally a personal access token;
- its [knowledge folder settings](features.md#repository-knowledge-folder-settings).

The registry is ordered, and the first repository serves as the default. While
[Additional repositories](context.md#additional-repositories) is off, every entry
belongs to it. A knowledge reader asked for no repository also falls back to it.

The scan, when it is built, adds:

- metadata: `repo_url`, `repo_type`, `primary_language`, `team_owner`;
- scan and commit timestamps;
- owned package manifests, detected technologies, and GitHub metadata;
- the health score and its breakdown, and security alerts;
- ADR links and tags;
- daily freshness, health, GitHub and tech-adoption history.

A detected technology that the portfolio's technology
baseline does not allow will then trigger a deprecation warning.

### Repository Alias

```meta
type: value-object
status: draft
aliases: [alias, repository alias]
related: [.devbook/domain/roadmap/domain.md#repository-scope, .devbook/domain/repository-management/features.md#repository-registry-configuration]
```

The short name a person and the rest of the product use for a registered
repository, such as `app`. A task files itself against a repository by naming its
alias, and a Roadmap [Repository Scope](../roadmap/domain.md#repository-scope)
holds aliases as opaque strings. Equality is by value: an alias is stored trimmed
and in lower case. A repository registered without an alias of its own takes its
name as its alias.

Aliases are unique in the registry, because every consumer keys on them: the
filters, the colours, and the settings subpages. Relabelling an alias changes
nothing about which repository is meant.

### Rename Record

```meta
type: value-object
status: draft
aliases: [RepositoryRename, memory of a rename]
related: [.devbook/domain/repository-management/features.md#following-a-repository-rename]
```

One rename the registry remembers: the old owner and name, the new one, and when.
An old name still written on an entry resolves through the records to the
repository it became, however many renames ago. A name given up twice follows the
latest record. The records travel with the registry, because the registry and the
entries reach another workspace by different routes and in no fixed order.

A record lasts until its old name is registered again. A configured repository
always wins over a record, so a repository re-created under a name once given up
is that new repository, not the old one.

### Removal Record

```meta
type: value-object
status: draft
aliases: [RepositoryRemoval, memory of a removal]
related: [.devbook/domain/repository-management/features.md#following-a-repository-rename]
```

One repository somebody removed, and when. Entries that still name it outlive it.
Without the record, the next start would read such a name as a repository
registered on another workspace and register it again, undoing the removal. The
record lasts until the name is registered again on purpose, from the list or by an
import that names it.

### Package Manifest

```meta
type: entity
status: draft
aliases: [PackageManifest]
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
aliases: [HealthDetails, health score, health_score]
```

The breakdown of a repository's 0-100 health score, tracked over time as
`Health Score Snapshot`; `flow.md` says how it is computed. It holds
`package_score`, `github_score`, `coverage_score`,
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
aliases: [TechBaselines]
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
related: [.devbook/domain/technology-stack/domain.md#technology-registry, .devbook/arc42/06-runtime-view.md#repository-baseline-scan-and-health-signal]
```

Runs scheduled or on-demand scans of a repository. A scan:

- parses dependency manifests;
- detects the technology stack;
- fetches GitHub metadata and security alerts;
- recomputes the health score from package freshness, GitHub backlog, coverage,
  and security.

It is a service because it coordinates external systems (file system, GitHub,
package registries) to refresh the aggregate. Invocation semantics: scheduled or
on-demand orchestration service. It is planned and not built.

## Bulk Operations

```meta
type: domain-service
status: draft
related: [.devbook/domain/tasks/domain.md#task]
```

Coordinates portfolio-wide actions:

- queue package updates across repositories;
- run synchronized upgrades through GitHub Actions, such as "upgrade all ASP.NET
  Core projects to 9.0";
- generate migration guides.

It is a service because it spans many repositories rather than a single
aggregate. Invocation semantics: command-invoked orchestration service.

## Shared Enums

```meta
type: shared-enums
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Repository Management has a single aggregate; `Repository Type` and `Severity`
are documented under it. This chapter is reserved for future cross-aggregate
enums.
