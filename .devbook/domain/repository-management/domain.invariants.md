# Invariants

```meta
status: draft
type: invariants
```

What each aggregate and domain service on [`domain.md`](domain.md) enforces, one chapter each: each rule, where it is enforced, and the unit test that proves it.

## Repository Registry

```meta
status: draft
type: invariants
related: [.devbook/domain/repository-management/domain.md#repository-registry]
```

> The rules the Repository Registry holds every registry edit to, and the rules it will hold its health scoring to. Scanning and health scoring are planned and not built.

### Invariant: Archiving keeps the repository's identity and settings

```meta
status: draft
type: invariant
```

Archiving or unarchiving a repository changes only whether it is [archived](domain.md#archived-repository): its `RepositoryId`, alias, clone directory, colour, account, token, knowledge folder settings and pull request labels stay as they were.
Unarchive restores everything only because Archive never touched it.

Enforced at: SetRepositoryArchived() (GitHubSettingsStore)

### Invariant: The last active repository cannot be archived

```meta
status: draft
type: invariant
```

A registry that holds at least one repository holds at least one [active repository](domain.md#active-repository) (`last-active-repository`).
The default repository and every single-repository setup need a repository to fall back to.

Enforced at: SetRepositoryArchived() (GitHubSettingsStore)

### Invariant: The default repository is the first active one

```meta
status: draft
type: invariant
```

The default repository is the first repository in the registry's order that is not archived, never an archived one.

Enforced at: GitHubSettings, wherever it answers the default repository

### Invariant: A rename or merge keeps the repository archived

```meta
status: draft
type: invariant
```

A rename leaves an archived repository archived, and a merge leaves the repository it keeps archived or active as it was.

Enforced at: RenameRepository() (GitHubSettingsStore)

### Invariant: Only a registered repository is scored

```meta
status: draft
type: invariant
```

```annotation
kind: question
author: "flow-spec"
date: 2026-10-13
body: |
  Not built: no type in src/ enforces this yet. Which type and transition will?
```

Health metrics are calculated only for a registered repository.

Enforced at: open

## Repository Scan

```meta
status: draft
type: invariants
related: [.devbook/domain/repository-management/domain.md#repository-scan]
```

> The rules every Repository Scan will keep, whoever starts it.

### Invariant: A scan populates the package manifests

```meta
status: draft
type: invariant
```

```annotation
kind: question
author: "flow-spec"
date: 2026-10-13
body: |
  Not built: no type in src/ enforces this yet. Which type and transition will?
```

A scan populates the repository's [package manifests](domain.md#package-manifest) from the manifest files it parses.

Enforced at: open

### Invariant: A scan syncs the GitHub metadata

```meta
status: draft
type: invariant
```

```annotation
kind: question
author: "flow-spec"
date: 2026-10-13
body: |
  Not built: no type in src/ enforces this yet. Which type and transition will?
```

A scan syncs the repository's [GitHub metadata](domain.md#github-metadata).

Enforced at: open

### Invariant: Every scan records a health snapshot

```meta
status: draft
type: invariant
```

```annotation
kind: question
author: "flow-spec"
date: 2026-10-13
body: |
  Not built: no type in src/ enforces this yet. Which type and transition will?
```

Every scan generates a [Health Score Snapshot](domain.md#health-score-snapshot).

Enforced at: open
