# Invariants

```meta
status: draft
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate: each rule, where it is enforced, and the unit test that proves it.

## Technology Registry

```meta
status: draft
type: invariants
related: [.devbook/domain/technology-stack/domain.md#technology-registry, .devbook/domain/technology-stack/domain.md#technology]
```

> The rules the Technology Registry holds its technologies and [baselines](domain.md#technology-baseline) to, including those a [Technology](domain.md#technology) enforces.

### Invariant: All changes go through the registry

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

Technologies, baselines and deprecations change only through the Technology Registry.

Enforced at: open

### Invariant: Baseline versions name a known technology

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

Every version in a Technology Baseline belongs to a Technology in the registry.

Enforced at: open

### Invariant: Baseline versions meet the minimum

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

Every version in a Technology Baseline is at or above that technology's `min_version`.

Enforced at: open

### Invariant: No active baseline holds an unsupported technology

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

An `unsupported` technology appears in no active baseline.

Enforced at: open

### Invariant: Versions are ordered

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

A technology's `min_version` is at or below its `recommended_version`, which is at or below its `latest_available_version`.

Enforced at: open

### Invariant: A deprecated technology has an end-of-life date

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

A `deprecated` technology has an `eol_date`.

Enforced at: open

### Invariant: A deprecated technology has migration guidance

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

A `deprecated` technology has `migration_guidance`.

Enforced at: open
