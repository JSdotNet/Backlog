# Invariants

```meta
status: draft
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate: each rule, where it is enforced, and the unit test that proves it.

## Environment Catalog

```meta
status: draft
type: invariants
related: [.devbook/domain/environment/domain.md#environment-catalog]
```

> The rules the Environment Catalog holds its environments and their shortcuts to.

### Invariant: An environment keeps its name

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

Each Environment keeps the same name for its whole life.

Enforced at: open

### Invariant: An environment says how to reach it

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

Each Environment has a type, a target reference and a launch method.

Enforced at: open

### Invariant: Arranging a shortcut leaves its target alone

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

Pinning, grouping, hiding or reordering an [Environment Shortcut](domain.md#environment-shortcut) never changes its target environment.

Enforced at: open

### Invariant: The catalog holds no secret

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

The catalog never stores a secret, only a pointer to the secret store or platform that owns it.

Enforced at: open
