# Features: Repository Management

```meta
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: Repository registration

```meta
status: draft
```

Register repositories with metadata (name, clone path, language, team, GitHub
URL, type), support local and remote-only registrations, auto-discover repos from
configured folders, and track when each was last scanned.

## Feature: Package and dependency tracking

```meta
status: draft
depends-on: [.domain/repository-management/features.md#feature-repository-registration]
```

Scan dependency files, parse package versions and constraints, track transitive
dependencies, detect outdated packages against registries, alert on critical or
security patches, and support custom feeds.

## Feature: Technology stack inventory

```meta
status: draft
depends-on: [.domain/repository-management/features.md#feature-repository-registration]
related: [.domain/technology-stack/features.md#feature-portfolio-wide-adoption-tracking]
```

Detect primary languages, framework versions, build tools, runtimes, and
Docker/Kubernetes/cloud usage, with custom technology tagging.

## Feature: GitHub integration

```meta
status: draft
```

Fetch GitHub metadata (stars, forks, last commit, branch protection), track open
issues and PRs and their age, detect unmaintained repos, and track GitHub Actions
CI/CD status.

## Feature: Repository health scoring

```meta
status: draft
depends-on: [.domain/repository-management/features.md#feature-package-and-dependency-tracking, .domain/repository-management/features.md#feature-github-integration]
related: [.domain/monitoring/features.md#feature-multi-repo-scanning]
```

Compute a health score from package freshness, GitHub issue/PR backlog, test
coverage, and security alerts, surface low-health repos, and provide actionable
per-repo recommendations.

## Feature: Technology trend analysis

```meta
status: draft
related: [.domain/technology-stack/features.md#feature-portfolio-wide-adoption-tracking]
```

Aggregate package versions across repos to identify platform-wide adoption,
surface deprecated tech still in use, recommend upgrades, and track adoption of
new libraries/frameworks.

## Feature: Bulk operations

```meta
status: draft
depends-on: [.domain/repository-management/features.md#feature-repository-health-scoring]
related: [.domain/backlog/features.md#feature-backlog-entry-creation]
```

Queue package updates across multiple repos, coordinate synchronized upgrades,
run custom workflows via GitHub Actions, and generate migration guides for major
versions.
