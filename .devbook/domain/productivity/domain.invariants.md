# Invariants

```meta
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate: each rule, where it is enforced, and the unit test that proves it.

## Productivity Ledger

```meta
type: invariants
related: [.devbook/domain/productivity/domain.md#productivity-ledger]
```

> The rules the Productivity Ledger holds its entries to.

### Invariant: The ledger is append-only

```meta
type: invariant
```

A recorded [Productivity Entry](domain.md#productivity-entry) is never edited or removed; a correction arrives as a new entry.

Enforced at: open

### Invariant: An entry names its source

```meta
type: invariant
```

Every Productivity Entry names its source.

Enforced at: open

### Invariant: An entry has an activity kind

```meta
type: invariant
```

Every Productivity Entry has an [Activity Kind](domain.md#activity-kind).

Enforced at: open

### Invariant: An entry has a time

```meta
type: invariant
```

Every Productivity Entry has a time range or a timestamp.

Enforced at: open

### Invariant: An AI-assisted entry names its tool

```meta
type: invariant
```

An AI-assisted entry names the AI tool or session that did the work whenever that is known.

Enforced at: open

### Invariant: Summaries never change entries

```meta
type: invariant
```

Computing a summary never changes a source entry.

Enforced at: open
