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

### Sub-feature: Repository registry configuration

```meta
status: draft
related: [.domain/backlog/features.md#feature-multi-repo-targeting, .domain/second-brain/features.md#feature-repository-knowledge-areas]
```

Maintain the working set of repositories the app acts on. Each registered
repository carries a short alias, its owner and name, an optional local clone
directory, and a flag marking one repository as primary. The alias is the name
the rest of the product uses: a backlog entry files itself against a repository
by naming that alias, and an entry without one falls back to the primary
repository. Registering more than one repository is itself an opt-in capability,
so a single-repository setup stays uncluttered.

### Sub-feature: Repository knowledge folder settings

```meta
status: draft
related: [.domain/second-brain/features.md#feature-repository-knowledge-areas]
```

Decide, per repository, which knowledge folders the product may read and where
they live. Each folder can be switched off entirely, and most can point at a
non-standard location instead of the conventional one. A folder is only readable
when the repository has a local clone directory, the folder is switched on, and
the resolved location actually exists; otherwise the product explains which of
those conditions is missing rather than silently showing nothing.

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

### Sub-feature: GitHub access resolution

```meta
status: draft
related: [.domain/backlog/features.md#feature-projection]
```

Reach GitHub through whichever credential the machine already has. An existing
signed-in GitHub CLI is preferred so no credential has to be stored in the
product; a per-repository personal access token is the fallback for machines
without it. Access can be checked on demand and reports back in plain language
which route is in use and whether it currently works, and tokens are kept
outside the backlog folder so the backlog itself stays safe to sync or commit.

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
