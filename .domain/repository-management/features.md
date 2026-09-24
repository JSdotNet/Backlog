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
related: [.domain/tasks/features.md#multi-repo-targeting, .domain/devbook/features.md#repository-devbook-areas]
```

Maintain the working set of repositories the app acts on. Each registered
repository carries a short alias, its owner and name, an optional local clone
directory, and a flag marking one repository as primary. The alias is the name
the rest of the product uses: a task files itself against a repository
by naming that alias, and an entry without one falls back to the primary
repository. Registering more than one repository is itself an opt-in capability,
so a single-repository setup stays uncluttered.

### Following a repository rename

```meta
type: sub-feature
status: draft
related: [.domain/repository-management/domain.md#repository, .domain/tasks/features.md#multi-repo-targeting, .domain/inbox/features.md#per-item-triage-actions]
```

Let a repository that was renamed on GitHub stay the same repository in the
product. A registered repository is identified by its owner and name, and that is
also what every entry and inbox item filed against it records — so a change of
name would otherwise read as a second repository, and everything filed against
the old name would be left pointing at a coordinate nobody is configured for.

A rename is asked for on the repository itself, by giving it its new owner and
name; it is never inferred from the registered list. A list cannot tell a rename
from a replacement, and for a repository registered without an alias of its own
— whose alias simply is its name — the two edits are the same edit. So editing
the list is registering and removing, and a change of owner or name made there
is a removed repository beside a new one; the working set says so and points at
the rename, which is the edit that keeps things.

Renamed, the repository keeps everything that was decided about it — its clone
directory, colour, account, and knowledge-folder settings — under the new name,
and the old name is no longer a registered repository. Every entry and inbox
item that named the old repository follows it to the new one, including an
entry's links to issues it created there; an inbox item's record of where it was
routed is left as written, because that is the record of a decision already
taken. The working set reports what happened and how many entries and inbox
items followed. A new name that is not a repository coordinate, or is the name
the repository already has, is refused.

A new name that another registered repository already has merges the two. This
is how a placeholder, registered when a plan named a repository nobody had yet,
is folded into the real repository it meant. The renamed repository goes, the
other one keeps everything that was decided about it, and everything filed
against the old name follows it there exactly as it would for a rename. The
working set says it merged rather than renamed, on the card of the repository
that was kept.

The registered list also remembers each rename — the old name, the new one, and
when — so that the old name keeps meaning the repository it became wherever it
is still written. The list and the entries reach another workspace of the same
person by different routes and in no fixed order, and an entry that still names
the old repository must not read as naming a repository nobody has. A workspace
that receives the renamed list before the re-pointed entries follows the rename
itself on its next start, and a name given up twice is followed to where the
second rename put it. The memory of a rename is kept until its old name is
registered again: a repository re-created under a name once given up is that
new repository, not the old one.

A removal is remembered in the same way and for the same reason. Entries that
still name a removed repository outlive it, and an entry naming a repository
nobody has is otherwise read as one registered on another workspace and
registered again, so a removal would be undone on the next start. Remembered,
the removed name stays on those entries, which show no repository. The memory
lasts until the name is registered again, on purpose, from the list or by an
import that names it.

Two edits look similar and are not renames. Relabelling the alias alone changes
nothing about which repository is meant, so nothing has to follow it. Swapping
two aliases between two registered repositories is two relabels: each
repository keeps its own settings, and no entry moves.

### Repository identity colour

```meta
type: sub-feature
status: draft
related: [.design/color-scheme.md#band-identity-tokens, .domain/roadmap/features.md#telling-one-project-from-another-at-a-glance]
```

Give each registered repository one colour, so a workspace holding several of
them can be read one project at a time. The colour belongs to the repository
rather than to any screen showing it: the same project is the same colour on the
repository filter, on a plan, on a task filed against it, on the agent sessions
under that task and on the row for a session in the Sessions area, and a screen
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
related: [.domain/devbook/features.md#repository-devbook-areas]
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
related: [.domain/tasks/features.md#projection]
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
related: [.domain/tasks/features.md#task-creation]
```

Queue package updates across multiple repos, coordinate synchronized upgrades,
run custom workflows via GitHub Actions, and generate migration guides for major
versions.
