# Invariants

```meta
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate: each rule, where it is enforced, and the unit test that proves it.

## Environment Catalog

```meta
type: invariants
related: [.devbook/domain/environment/domain.md#environment-catalog]
```

> The rules the Environment Catalog holds its environments and their shortcuts to.

### Invariant: An environment keeps its name

```meta
type: invariant
```

Each Environment keeps the same name for its whole life.

Enforced at: open

### Invariant: An environment says how to reach it

```meta
type: invariant
```

Each Environment has a type, a target reference and a launch method.

Enforced at: open

### Invariant: Arranging a shortcut leaves its target alone

```meta
type: invariant
```

Pinning, grouping, hiding or reordering an [Environment Shortcut](domain.md#environment-shortcut) never changes its target environment.

Enforced at: open

### Invariant: The catalog holds no secret

```meta
type: invariant
```

The catalog never stores a secret, only a pointer to the secret store or platform that owns it.

Enforced at: open
