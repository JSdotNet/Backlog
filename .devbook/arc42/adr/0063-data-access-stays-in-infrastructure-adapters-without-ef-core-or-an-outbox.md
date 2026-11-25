# ADR 0063: Data access stays in infrastructure adapters, on raw SQLite and the Cosmos SDK, with no EF Core and no event outbox

```meta
date: 2026-11-25
related: [".devbook/arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".devbook/arc42/adr/0006-additive-schema-bootstrapping-is-the-local-migration-mechanism.md", ".devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md", ".devbook/arc42/adr/guidelines/0005-modular-monolith-structure.md", ".devbook/arc42/adr/guidelines/0014-persistence-and-repository-boundaries.md", ".devbook/arc42/05-building-block-view.md", ".devbook/arc42/09-architecture-decisions.md"]
c4: [backlog.desktop, backlog.cloud]
c4-touches: [backlog.mobile, backlog.taskStore, backlog.cloudDb]
```

Backlog keeps its data access in `Backlog.Infrastructure.Sqlite` and
`Backlog.Infrastructure.Cosmos`. They use `Microsoft.Data.Sqlite` and the Cosmos SDK
directly. There are no `{Module}.Data.{Provider}` projects, no EF Core, no
migration-runner resource, and no integration events through a transactional outbox.

## Status

```meta
```

Proposed, 2026-11-25. Plan `app-defaults-rollout`, entry 37 (`backlog-deviation-adrs`).

A **local** decision. It records a deliberate difference from two org ADRs:

- [org ADR 0003: Solution Structure](https://github.com/JSdotNet/app-defaults/blob/main/defaults/arc42/adr/0003-solution-structure.md),
  its `{Module}.Data.{Provider}` rule;
- [org ADR 0006: Persistence and Messaging](https://github.com/JSdotNet/app-defaults/blob/main/defaults/arc42/adr/0006-persistence-and-messaging.md),
  its EF Core, migration-runner, outbox and integration-event rules.

It keeps local ADR 0003 (SQLite is the canonical store) and local ADR 0006 (additive,
idempotent bootstrapping), and applies them to the org rules.

## Context

```meta
```

Org ADR 0006 assumes a server app: each module's API owns a schema in a database the
AppHost hosts, a migration runner upgrades it before the API starts, and modules in
separate deployables exchange events.

Backlog is mostly a client app:

- **The canonical store is a SQLite file on the person's desktop** (local ADR 0003).
  It opens inside a MAUI process on Windows and on Android. There is no API host to
  wait for a migration runner, so the app has to upgrade its own file when it starts.
  Local ADR 0006 says how: additive, idempotent `CREATE ... IF NOT EXISTS` and
  column-adds, so any older file opens.
- **The cloud replica is a set of Cosmos document containers** (local ADR 0005) that
  hold whole documents per kind. Nothing in it is relational. The EF Core Cosmos
  provider would map these documents and add nothing the SDK lacks.
- **The modules run in one process on each device.** They call each other through
  their `*.Abstractions` contracts, which the architecture tests guard. Between
  devices, the only channel is the replica: each document kind carries its own
  version and tombstone, and the phone's outbox (`Backlog.Mobile.UI/Outbox`) retries
  every write until the service acknowledges it. That is already at-least-once
  delivery with idempotent consumers, and there is no broker between modules.

## Decision

```meta
```

**Keep.**

1. **Data access stays in the infrastructure adapters.** `Backlog.Infrastructure.Sqlite`
   and `Backlog.Infrastructure.Cosmos` implement the store interfaces that the modules
   declare. No `{Module}.Data.{Provider}` project is created. The architecture tests
   still keep module code out of the adapters' internals.
2. **No EF Core.** SQLite is read and written with `Microsoft.Data.Sqlite` and
   hand-written SQL. Cosmos is read and written with `Microsoft.Azure.Cosmos`.
3. **No migration-runner resource.** The desktop and the phone bootstrap their own file
   at start (local ADR 0006). Cosmos containers are declared in the Bicep (local ADR
   0062).
4. **No integration events and no transactional outbox between modules.** In one
   process the modules call each other's contracts. Between devices the replica and
   the phone's write outbox carry the change, as local ADR 0005 describes.

This record does not decide org ADR 0006's concurrency-token rule. That rule still
applies: some Cosmos writes already check an ETag (`CosmosAnnotationReplica`), and
the rest are a gap for their own change, not a deliberate difference.

**Revisit** if a module ever runs in a deployable of its own beside the sync service.

## Consequences

```meta
```

- Every query is hand-written SQL that the adapter's tests cover. There is no LINQ
  provider and no generated migration.
- A schema change is an additive bootstrap step. Dropping or renaming a column needs
  its own decision, as local ADR 0006 says.
- An app-defaults review reads this record for the 0003 Data and 0006 rows.

## Rejected

```meta
```

- **Adopt EF Core in `{Module}.Data.Sqlite` and `.Cosmos` projects (XL).** Every store
  behind the 15 modules would be rewritten, and every existing desktop and phone file
  would be migrated in place. EF Core's migrations assume a runner the client does not
  have, and its start-up and trimming cost lands on Android. No defect today comes from
  the raw SQL.
- **Add an outbox and integration events.** There is no second process between modules
  for an event to cross. The replica already gives the delivery guarantees the outbox
  pattern exists for.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-25 | Proposed: keep raw SQLite and the Cosmos SDK in the infrastructure adapters, self-bootstrapping files and no inter-module outbox. | Step 15 of plan `app-defaults-rollout` (PR #1967) listed the org ADR 0003 and 0006 differences as L and XL with no local decision. |
