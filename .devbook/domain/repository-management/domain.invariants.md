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

> The rules the Repository Registry will hold its health scoring to. Scanning and health scoring are planned and not built.

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
