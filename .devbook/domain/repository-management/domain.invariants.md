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
tests: [unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.RepositoryArchiveTests.Archiving_and_unarchiving_change_nothing_but_the_flag, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.RepositoryArchiveTests.Every_other_mutator_keeps_the_flag, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.RepositoryArchiveTests.A_re_typed_repositories_list_keeps_the_flag]
```

Archiving or unarchiving a repository changes only whether it is [archived](domain.md#archived-repository): its `RepositoryId`, alias, clone directory, colour, account, token, knowledge folder settings and pull request labels stay as they were.
Unarchive restores everything only because Archive never touched it.

Enforced at: SetRepositoryArchived() (GitHubSettingsStore)

### Invariant: The last active repository cannot be archived

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.RepositoryArchiveTests.The_last_active_repository_cannot_be_removed_dropped_or_merged_away_while_others_are_archived, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsRepositoryArchiveTests.The_last_active_repository_cannot_be_archived_and_says_one_must_stay_active]
```

A registry that holds at least one repository holds at least one [active repository](domain.md#active-repository).
No edit may leave repositories with none active: archiving the last active one, removing it while the others are archived, dropping it from a retyped list, or merging it into an archived one.
Each is refused with "One repository must stay active." (`GitHubSettingsStore.LastActiveRepository`).
Removing every repository is allowed, because an empty registry has nothing to fall back to.
The default repository and every single-repository setup need a repository to fall back to.

Enforced at: SetRepositoryArchived(), RemoveRepository(), SetRepositories() and the merge in RenameRepository() (GitHubSettingsStore)

### Invariant: The default repository is the first active one

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.RepositoryArchiveTests.The_default_repository_skips_an_archived_first_entry, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubIntegrationPullRequestListTests.The_active_repositories_leave_the_archived_ones_out, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookFolderSourceBranchTests.An_unscoped_question_falls_back_to_the_first_active_repository]
```

The default repository is the first repository in the registry's order that is not archived, never an archived one.

Enforced at: GitHubSettings, wherever it answers the default repository

### Invariant: A rename or merge keeps the repository archived

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.RepositoryArchiveTests.A_rename_keeps_an_archived_repository_archived, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.RepositoryArchiveTests.A_merge_keeps_the_repository_it_keeps_archived_as_it_was, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.RepositoryArchiveTests.A_merge_of_an_archived_row_into_an_active_one_leaves_it_active]
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
