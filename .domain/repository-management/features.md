# Repository Management

```meta
type: features
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Repository registration

```meta
type: feature
status: draft
```

Register repositories with metadata (name, clone path, language, team, GitHub
URL, type), support local and remote-only registrations, auto-discover repos from
configured folders, and track when each was last scanned.

### Repository registry configuration

```meta
type: sub-feature
status: draft
feature-flag: additional-repositories
related: [.domain/backlog/features.md#multi-repo-targeting, .domain/second-brain/features.md#repository-knowledge-areas]
```

Maintain the working set of repositories the app acts on. Each registered
repository carries a short alias, its owner and name, an optional local clone
directory, and a flag marking one repository as primary. The alias is the name
the rest of the product uses: a backlog entry files itself against a repository
by naming that alias, and an entry without one falls back to the primary
repository. Registering more than one repository is itself an opt-in capability,
so a single-repository setup stays uncluttered.

### Repository identity colour

```meta
type: sub-feature
status: draft
related: [.design/color-scheme.md#band-identity-tokens, .domain/roadmap/features.md#telling-one-project-from-another-at-a-glance]
```

Give each registered repository one colour, so a workspace holding several of
them can be read one project at a time. The colour belongs to the repository
rather than to any screen showing it: the same project is the same colour on the
repository filter, on a plan, on an entry filed against it, on the agent sessions
under that entry and on the row for a session in the Sessions area, and a screen
that decided its own would be a second answer to which project is which.

The registry records *which* of the colours the design system sanctions, never a
colour of its own — inventing one is a design decision and is made where design
decisions are made. A repository nobody has chosen for is placed automatically by
its position in the working set, stepping over the colours already claimed so it
never lands on the one a neighbour was deliberately given. Past the end of the
set the placement wraps and two repositories may share a colour, which is
acceptable because the colour is not the identifier — the alias is, and it is
written wherever the colour is shown.

The choice can be given back, which returns the repository to its automatic
placement rather than leaving it colourless.

### Repository knowledge folder settings

```meta
type: sub-feature
status: draft
related: [.domain/second-brain/features.md#repository-knowledge-areas]
```

Decide, per repository, which knowledge folders the product may read and where
they live. Each folder can be switched off entirely, and most can point at a
non-standard location instead of the conventional one. A folder is only readable
when the repository has a local clone directory, the folder is switched on, and
the resolved location actually exists; otherwise the product explains which of
those conditions is missing rather than silently showing nothing.

## Package and dependency tracking

```meta
type: feature
status: draft
depends-on: [.domain/repository-management/features.md#repository-registration]
```

Scan dependency files, parse package versions and constraints, track transitive
dependencies, detect outdated packages against registries, alert on critical or
security patches, and support custom feeds.

## Technology stack inventory

```meta
type: feature
status: draft
depends-on: [.domain/repository-management/features.md#repository-registration]
related: [.domain/technology-stack/features.md#portfolio-wide-adoption-tracking]
```

Detect primary languages, framework versions, build tools, runtimes, and
Docker/Kubernetes/cloud usage, with custom technology tagging.

## GitHub integration

```meta
type: feature
status: draft
```

Fetch GitHub metadata (stars, forks, last commit, branch protection), track open
issues and PRs and their age, detect unmaintained repos, and track GitHub Actions
CI/CD status.

### GitHub access resolution

```meta
type: sub-feature
status: draft
related: [.domain/backlog/features.md#projection]
```

Reach GitHub through whichever credential the machine already has. An existing
signed-in GitHub CLI is preferred so no credential has to be stored in the
product; a per-repository personal access token is the fallback for machines
without it. Access can be checked on demand and reports back in plain language
which route is in use and whether it currently works, and tokens are kept
outside the backlog folder so the backlog itself stays safe to sync or commit.

## Repository health scoring

```meta
type: feature
status: draft
depends-on: [.domain/repository-management/features.md#package-and-dependency-tracking, .domain/repository-management/features.md#github-integration]
related: [.domain/monitoring/features.md#multi-repo-scanning]
```

Compute a health score from package freshness, GitHub issue/PR backlog, test
coverage, and security alerts, surface low-health repos, and provide actionable
per-repo recommendations.

## Technology trend analysis

```meta
type: feature
status: draft
related: [.domain/technology-stack/features.md#portfolio-wide-adoption-tracking]
```

Aggregate package versions across repos to identify platform-wide adoption,
surface deprecated tech still in use, recommend upgrades, and track adoption of
new libraries/frameworks.

## Bulk operations

```meta
type: feature
status: draft
depends-on: [.domain/repository-management/features.md#repository-health-scoring]
related: [.domain/backlog/features.md#backlog-entry-creation]
```

Queue package updates across multiple repos, coordinate synchronized upgrades,
run custom workflows via GitHub Actions, and generate migration guides for major
versions.
