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

Register the repositories the product works with:

- record each one's metadata: name, clone path, language, team, GitHub URL, and
  type;
- register a repository with a local clone, or remote-only;
- discover repositories automatically from configured folders;
- track when each was last scanned.

### Repository registry configuration

```meta
type: sub-feature
status: draft
feature-flag: .devbook/domain/repository-management/context.md#additional-repositories
related: [.devbook/domain/tasks/features.md#multi-repo-targeting, .devbook/domain/devbook/features.md#repository-devbook-areas]
```

Maintain the [registry](domain.md#repository-registry) of repositories the app
acts on. Each registered repository carries a short
[alias](domain.md#repository-alias), its owner and name, and an optional local
clone directory. The alias is the name the rest of the product uses: a task files
itself against a repository by naming that alias. Registering more than one
repository is itself an opt-in capability, so a single-repository setup stays
uncluttered. While it is off, every entry belongs to the first repository in the
registry.

The working set is read as one list: a row per repository naming its alias with
its colour beside it, where it is cloned on this machine, where its devbook was
found — or that it has none — and the account it is worked as, under a sentence
counting the repositories and how many of them carry a devbook. Everything
decided about one repository is edited under its own row, one repository at a
time, and a repository just registered opens there, ready to be set up. An
[archived](#archiving-a-repository) repository keeps its row, marked Archived.

### Following a repository rename

```meta
type: sub-feature
status: draft
related: [.devbook/domain/repository-management/domain.md#repository, .devbook/domain/tasks/features.md#multi-repo-targeting, .devbook/domain/inbox/features.md#per-item-triage-actions]
```

A registered repository is identified by its owner and name, and every entry and
inbox item filed against it records that coordinate too. Without a rename, a
change of name on GitHub would read as a second repository. Everything filed
against the old name would then point at a coordinate nobody is configured for.

Seven edits to the registry look alike and do different things:

| Edit | How it is made | What follows it | What is kept |
|---|---|---|---|
| Rename | A new owner and name, given on the repository itself | Every entry and inbox item naming the old repository, including an entry's links to issues it created there | The clone directory, colour, account and devbook folder settings, under the new name, and a [Rename Record](domain.md#rename-record) of the old name |
| Merge | A rename to a name another registered repository already has | Everything filed against the old name, to the other repository | The other repository's settings; the renamed one goes |
| Removal | Removing the repository from the list | Nothing | A [Removal Record](domain.md#removal-record); entries keep the removed name and show no repository |
| Archive | Choosing Archive on the repository's row ([Archiving a repository](#archiving-a-repository)) | Nothing is re-pointed; the panes stop reading the repository as active work | Everything: the identity, the settings, the tasks filed against it, and its devbook |
| Unarchive | Choosing Unarchive on the archived row | Nothing; the repository is active work again | Everything, as it was when it was archived |
| Relabel | Changing the alias alone | Nothing, because the same repository is meant | Everything |
| Alias swap | Swapping two aliases between two repositories | Nothing, because it is two relabels | Each repository's own settings |

A rename is asked for on the repository itself and is never inferred from the
list. A list cannot tell a rename from a replacement. For a repository registered
without an alias of its own, whose alias is its name, the two edits are the same
edit. So a change of owner or name made in the list is a removed repository beside
a new one. The repository list says so and points at the rename, which is the
edit that keeps things.

An inbox item's record of where it was routed is left as written, because that is
the record of a decision already taken. The repository list reports what happened
and how many entries and inbox items followed. A new name that is not a
repository coordinate, or is the name the repository already has, is refused.

A merge is how a [placeholder](domain.md#repository-registry) is folded into the
real repository it meant. The list says it merged rather than renamed, on the
card of the repository that was kept.

The list and the entries reach another workspace of the same person by different
routes and in no fixed order:

```mermaid
sequenceDiagram
    participant A as Workspace A
    participant Folder as Workspace folder (registry)
    participant Feed as Sync service (entries)
    participant B as Workspace B

    A->>A: rename me/old to me/new, re-point the entries
    A->>Folder: renamed registry, with its rename record
    A->>Feed: re-pointed entries
    Folder->>B: renamed registry arrives first
    B->>B: an entry still names me/old
    B->>B: the record resolves me/old to me/new
    B->>B: next start: re-point the entry itself
    Feed->>B: re-pointed entries arrive, already followed
```

An entry that still names the old repository must therefore not read as naming a
repository nobody has. The [Rename Record](domain.md#rename-record) says how long
the memory lasts and how a name given up twice is followed. A removal is
remembered for the same reason, in a [Removal Record](domain.md#removal-record).

### Archiving a repository

```meta
type: sub-feature
status: draft
related: [.devbook/domain/repository-management/requirements.md#archiving-a-repository, .devbook/domain/repository-management/domain.md#archived-repository, .devbook/arc42/adr/0029-an-archived-repository-stays-in-the-registry.md]
```

Quiet a repository that is no longer worked on, without forgetting it. An
[archived repository](domain.md#archived-repository) stays in the registry with
its identity, its settings and the tasks filed against it. Backlog stops treating
it as active work: it reads nothing from GitHub for it and leaves it out of the
scope and the pickers. Unarchive brings all of it back.

Removal is the other way to quiet a repository, and it forgets:

| | Removal | Archive |
|---|---|---|
| Reversible | No | Yes, by Unarchive |
| Identity and settings | Gone, with a [Removal Record](domain.md#removal-record) of the name | Kept |
| Tasks filed against it | Keep the name and show no repository | Keep the repository and show it |
| Devbook | Unavailable | Readable, and read-only |
| GitHub reads | None | None |

A person archives a repository from its row in the repository list, and nothing
archives one on its own. When GitHub reports the repository archived, or cannot
find it, the row suggests Archive and waits for the person.

Archiving asks what becomes of the repository's open tasks: move them to another
repository, drop them, or keep them as they are. The same choice stays on the
archived row for later.

What each pane does with an archived repository is in
[its requirements](requirements.md#archiving-a-repository). The registry's own
rules, such as the last active repository staying active, are in
[its invariants](domain.invariants.md#repository-registry). Local ADR 0029
records why the repository stays in the registry rather than being removed.

### Repository identity colour

```meta
type: sub-feature
status: draft
related: [.devbook/design/color-scheme.md#band-identity-tokens, .devbook/domain/roadmap/features.md#telling-one-project-from-another-at-a-glance]
```

Give each registered repository one colour, so a workspace holding several of
them can be read one project at a time. The colour belongs to the repository
rather than to any screen showing it. The same project is the same colour
everywhere it appears:

- on the repository filter;
- on a plan;
- on a task filed against it, and on the agent sessions under that task;
- on the row for a session in the Sessions area.

A screen that decided its own colour would be a second answer to which project is
which.

The registry records *which* of the colours the design system sanctions, never a
colour of its own. Inventing a colour is a design decision, and is made where
design decisions are made. A repository nobody has chosen for is placed
automatically by its position in the registry. The placement steps over the
colours already claimed, so it never lands on the one a neighbour was deliberately
given. Past the end of the set the placement wraps, and two repositories may share
a colour. That is acceptable, because the colour is not the identifier: the alias
is, and it is written wherever the colour is shown.

The choice can be given back, which returns the repository to its automatic
placement rather than leaving it colourless.

### Repository devbook folder settings

```meta
type: sub-feature
status: draft
related: [.devbook/domain/devbook/features.md#repository-devbook-areas]
```

Decide, per repository, which devbook folders the product may read and where
they live. Each folder can be switched off entirely, and most can point at a
non-standard location instead of the conventional one. A folder is readable only
when all three of these hold:

- the repository has a local clone directory;
- the folder is switched on;
- the resolved location actually exists.

Otherwise the product explains which condition is missing rather than silently
showing nothing.

## Package and dependency tracking

```meta
type: feature
status: draft
depends-on: [.devbook/domain/repository-management/features.md#repository-registration]
```

Keep track of each repository's dependencies:

- scan dependency files and parse package versions and constraints;
- track transitive dependencies;
- detect outdated packages against their registries, including custom feeds;
- alert on critical or security patches.

## Technology stack inventory

```meta
type: feature
status: draft
depends-on: [.devbook/domain/repository-management/features.md#repository-registration]
related: [.devbook/domain/technology-stack/features.md#portfolio-wide-adoption-tracking]
```

Detect each repository's primary languages, framework versions, build tools,
runtimes, and use of Docker, Kubernetes and the cloud. A person can also tag a
repository with technologies of their own.

## GitHub integration

```meta
type: feature
status: draft
```

Read each repository's GitHub side:

- metadata: stars, forks, last commit, branch protection;
- open issues and pull requests, and their age;
- whether the repository looks unmaintained;
- the status of its GitHub Actions CI/CD runs.

GitHub keeps an `archived` mark of its own on a repository, and an archived
GitHub repository stays readable. Backlog never copies that mark into the
registry. It suggests [archiving](#archiving-a-repository) the repository instead,
as it does when GitHub cannot find the repository at all, and reads nothing from
GitHub for a repository once it is archived.

### GitHub access resolution

```meta
type: sub-feature
status: draft
related: [.devbook/domain/tasks/features.md#projection]
```

Reach GitHub through whichever credential the machine already has. An existing
signed-in GitHub CLI is preferred, so no credential has to be stored in the
product. A per-repository personal access token is the fallback for machines
without it. Access can be checked on demand. The check reports in plain language
which route is in use and whether it currently works. Tokens are kept outside the
backlog folder, so the backlog itself stays safe to sync or commit.

## Repository health scoring

```meta
type: feature
status: draft
depends-on: [.devbook/domain/repository-management/features.md#package-and-dependency-tracking, .devbook/domain/repository-management/features.md#github-integration]
related: [.devbook/domain/monitoring/features.md#multi-repo-scanning]
```

Compute a health score from package freshness, the GitHub issue and pull request
backlog, test coverage, and security alerts. Low-health repositories surface on
their own, each with actionable recommendations
([Health scoring flow](flow.md#health-scoring-flow)).

## Technology trend analysis

```meta
type: feature
status: draft
related: [.devbook/domain/technology-stack/features.md#portfolio-wide-adoption-tracking]
```

Aggregate package versions across repositories to see platform-wide adoption.
The analysis surfaces deprecated technology still in use, recommends upgrades,
and tracks how new libraries and frameworks are adopted.

## Bulk operations

```meta
type: feature
status: draft
depends-on: [.devbook/domain/repository-management/features.md#repository-health-scoring]
related: [.devbook/domain/tasks/features.md#task-creation]
```

Act on many repositories at once: queue package updates, coordinate synchronized
upgrades, run custom workflows through GitHub Actions, and generate migration
guides for major versions.
