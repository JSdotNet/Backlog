# ADR 0014: Persistence and repository boundaries

```meta
status: active
related: [".arc42/08-crosscutting-concepts.md#storage-and-sync", ".arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0014 (decided 2026-06-04,
`guide/adrs/0014-persistence-strategy-and-repository-boundaries.md`), imported
2026-08-27.

## Decision

**Persistence belongs to the module that owns the data.** A module owns its
schema, its migrations, its repository implementations, and its query access. No
module reads or writes another module's tables — collaboration goes through
abstractions.

**ORM code stays in adapter projects.** Domain projects carry no ORM attributes
or base types; mapping is configured externally in the adapter. Abstractions and
API projects hold no data access at all.

**Repositories are aggregate-focused write ports.** One per aggregate root where
practical, the interface next to the code that uses it and the implementation in
the adapter. They load aggregates for command handling and persist state changes;
they do not become general-purpose query services.

**Reads may bypass the aggregate.** A query path that enforces no invariant may
project directly — inside the owning module, for reads only. A query model is
optimized for retrieval and is not a domain entity.

**Migrations are owned per module**, created in the same change as the model
change that needs them, named for business intent. Destructive automatic
migration at startup is prohibited.

## How Backlog applies it

- Local storage is **one SQLite database, canonical** — see local ADR 0003,
  `.arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md`. A task's content
  is markdown text inside it.
- `src/Infrastructure/Backlog.Infrastructure.Sqlite` holds the repository
  implementations (`SqliteTaskRepository`, `RootedSqliteTaskRepository`) and the
  persistence-only mapping types (`TaskPayloads`, `EnumMap`).
- Repository **ports** stay in the module (`ITaskRepository` at the root of
  `Backlog.Modules.Tasks`), exactly as the decision requires.
- Domain models are persistence-agnostic: no ORM attributes anywhere in a module
  project.

## Deviations and gaps

- **No EF Core.** Persistence is `Microsoft.Data.Sqlite` and hand-written SQL.
  The decision permits EF Core in an adapter; it does not require it, and a
  local-first single-file store does not need a full ORM.
- **Adapters are shared infrastructure projects, not per-module `Data.*`
  projects.** `Backlog.Infrastructure.Sqlite` serves the modules that persist
  locally. See the deviation note in
  [0005](0005-modular-monolith-structure.md).
- **No schema-per-module and no migration mechanism.** One local database, one
  schema, created by the adapter. A migration story is owed before the first
  schema change that has to preserve existing user data — tracked in
  `.arc42/11-risks-and-technical-debt.md`.
- The cloud tier persists only sync-oriented state, never canonical domain data
  (`.arc42/08-crosscutting-concepts.md#shared-data-types`).
