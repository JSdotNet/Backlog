# Invariants

```meta
status: draft
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate: each rule, where it is enforced, and the unit test that proves it.

## Productivity Ledger

```meta
status: draft
type: invariants
related: [.devbook/domain/productivity/domain.md#productivity-ledger]
```

> The rules the Productivity Ledger holds its entries to.

### Invariant: The ledger is append-only

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

A recorded [Productivity Entry](domain.md#productivity-entry) is never edited or removed; a correction arrives as a new entry.

Enforced at: open

### Invariant: An entry names its source

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

Every Productivity Entry names its source.

Enforced at: open

### Invariant: An entry has an activity kind

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

Every Productivity Entry has an [Activity Kind](domain.md#activity-kind).

Enforced at: open

### Invariant: An entry has a time

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

Every Productivity Entry has a time range or a timestamp.

Enforced at: open

### Invariant: An AI-assisted entry names its tool

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

An AI-assisted entry names the AI tool or session that did the work whenever that is known.

Enforced at: open

### Invariant: Summaries never change entries

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

Computing a summary never changes a source entry.

Enforced at: open
