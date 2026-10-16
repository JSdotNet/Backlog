# ADR 0006: Additive, idempotent bootstrapping is the local store's migration mechanism

```meta
status: proposed
date: 2026-10-13
related: [".devbook/arc42/08-crosscutting-concepts.md#storage-and-sync", ".devbook/arc42/11-risks-and-technical-debt.md", ".devbook/arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md", ".devbook/arc42/adr/guidelines/0014-persistence-and-repository-boundaries.md", ".devbook/tech/shared.md#sqlite"]
```

## Status

Proposed. Written to discharge the debt recorded as **D3** in
`.devbook/arc42/11-risks-and-technical-debt.md`, which says the migration story is owed
before the first schema change that has to preserve existing user data — and
that change is the `updated_at` and `deleted_at` columns local ADR 0005
requires.

A **local** decision, numbered in the local sequence. Every reference below to
ADR 0001–0006 without a qualifier means the local one; inherited ADRs are named
as such.

This record does not change what the local store is. Local ADR 0003 stands
whole. What it does is write down the rule that was already being followed, and
say where that rule stops being enough.

> **Amended 2026-10-13: the setup runs once per schema definition, not on every
> open.** Each store now declares its setup as data, and `backlog.db` holds one
> `schema_stamps` row per store with a fingerprint of that setup. The setup runs
> only on an open whose file is not stamped with the store's current fingerprint.
> The three shapes, the forbidden list and the boundary are unchanged. What moved
> is when the statements run, and **When it runs** below says how. The Context
> describes the adapter as it was when this record was written; the sections after
> it are written to the current code.

> **Amended 2026-10-13: an added column may be `NOT NULL` with a constant
> default.** The rule said "nullable, always", and five added columns already
> broke it: the tasks store's `devbook_refs` and `planned_hours` (`'[]'`), and the
> inbox store's `dismissed_suggestions` (`'[]'`), `duplicate_of_task` and
> `note_push_pending` (`0`). The reason behind the rule admits both definitions, so
> the rule now says so. **What is forbidden** carries it.

## Context

**The debt was recorded as missing machinery, and that is the wrong diagnosis.**
D3 reads *"No migration mechanism for the local SQLite store. The adapter creates
its schema and adds columns idempotently, but there is no versioned migration
path."* Inherited ADR 0014 records the same gap, and asks for two things that
pull in opposite directions here: *"Migrations are owned per module, created in
the same change as the model change that needs them, named for business
intent"*, and *"Destructive automatic migration at startup is prohibited."*

What the adapter did when this record was written had already done a migration's
job for three schema changes, without being called one:

- `effort`, `import_plan_id` and `import_item_id` were each added to a live table
  by `EnsureColumnAsync`, which reads `PRAGMA table_info(tasks)` and issues one
  additive `ALTER TABLE` when the column is absent.
- `NormalizeRetiredTypeAsync` rewrites the retired `follow_up` task type to
  `task`, because `EnumMap.ParseType` no longer knows the word and a row still
  carrying it would throw on every read — somebody's entry lost to a rename.

Both ran unconditionally on the way into every operation. Both are true after
they run and true again the next time. Neither reads what ran before, and there
is no version column to read.

**The honest question is not "why is there no framework" but "what is this, and
when does it stop working".** The existing code answers the first half in a
comment and declines the second: *"Deliberately not the start of a migration
system: there is no version column, no ordered script list, and nothing here
reads what ran before."* That is a good disclaimer and a missing decision. A
reader cannot tell from it whether the next schema change is allowed to use the
same trick.

**ADR 0005 forces the question because its change is the first one with live user
data at stake.** A modification timestamp has to reach rows that already exist,
and it has to reach them with a value the sync push can compare — not with a
null, because the push asks for documents changed since a watermark and
`null > watermark` is never true, so a row left null would never travel and
would stay invisible to the person's other machine for good.

## Decision

**The local store's migration mechanism is additive, idempotent bootstrapping,
performed by the adapter on any open whose file is not stamped with the store's
current schema definition. It is a real mechanism with a stated boundary, not an
absence of one.**

### When it runs

Each store that keeps tables in `backlog.db` declares its whole setup as one
`SqliteSchemaDefinition` (`src/Infrastructure/Backlog.Infrastructure.Sqlite/SqliteSchema.cs`),
in three parts, run in this order:

1. **The DDL**: the `IF NOT EXISTS` tables and indexes.
2. **The columns**: each column added after its table already had rows. The setup
   reads `PRAGMA table_info` once per table and adds only the columns missing.
3. **The statements that need those columns** (`afterColumns`): seeds, rewrites of
   a retired value, and indexes over an added column, in order.

The file records which setup it has had in a `schema_stamps(owner, fingerprint)`
table, one row per store. The fingerprint is a SHA-256 of all three parts. On an
open, the adapter reads the store's row. The setup runs only when that row is
missing or holds a different fingerprint, and the stamp is written last. So a setup
that fails halfway leaves no stamp and runs again on the next open. A new file, a
replaced file and a file from before the stamp have no row, so each gets the
setup.

**The record is per owner, not `PRAGMA user_version`.** Five stores share
`backlog.db`: tasks, inbox, roadmap, Claude API requests and model prices. One
number per file would have to answer for all five schemas at once.

**It is a fingerprint, not a version number.** Any change to the definition
changes the fingerprint, so the setup reruns without anybody having to remember a
bump. A forgotten bump passes every test, because tests start from a new file. It
fails only on the files people already have.

### What is allowed

Three shapes, and only these three:

| Shape | How | Why it is safe |
|---|---|---|
| **Add a column** | An entry in the definition's `columns` list. The setup reads the table's columns and issues one additive `ALTER TABLE ... ADD COLUMN` when the column is absent, nullable or `NOT NULL` with a constant default. | Adds information. No existing value is read, moved, or destroyed. |
| **Seed a new column for existing rows** | One `UPDATE ... WHERE <column> IS NULL` in `afterColumns`. | Writes only where there was nothing. After it runs no row matches it again. |
| **Rewrite a retired value to its replacement** | One `UPDATE ... WHERE <column> = '<retired>'` in `afterColumns`. | The retired value is unreadable by definition — the code that understood it is gone — so the row was already lost. Matches nothing once it has run. |

Every statement must be **idempotent by construction** rather than by
bookkeeping: true after it runs, and matching nothing the next time. That
property is what keeps the stamp a plain "setup already ran" marker. A file
stamped by an earlier definition, by none, or by a setup that failed halfway gets
the whole set again, and every statement in it is safe to run again. The stamp
only lets an open skip work. It never decides which statement runs, and that is
why no version column is needed.

Each statement carries a comment saying what it is for and why it is one of the
three shapes above. A statement whose safety needs explaining at the call site is
a statement in the wrong record.

### What is forbidden

**Anything that is not additive.** Dropping or renaming a column, narrowing a
type, splitting or merging columns, changing a primary key, or any `UPDATE` that
overwrites a value the previous version wrote deliberately. Inherited ADR 0014's
prohibition on destructive automatic migration at startup applies with full
force, and this mechanism is not a way around it — it is a mechanism that cannot
express a destructive change in the first place.

**A column added this way is nullable, or `NOT NULL` with a constant default.**
The schema in `CREATE TABLE` and the schema an `ALTER` produces must agree, or a
fresh database and an upgraded one diverge. SQLite's `ALTER TABLE ADD COLUMN`
accepts `NOT NULL` on a populated table only with a non-null constant default, so
these are the only two definitions both paths can produce.

A default is right when one constant is true of every row that predates the
column: `'[]'` for a list an older row never had entries in, `0` for a flag it
never set. Where the true value differs per row, the column is nullable. The
seeding `UPDATE` supplies what it can and the read coalesces what the seed could
not reach. `tasks.updated_at` is that case: no single constant is true of every
existing task, so it is nullable and seeded from `created_at`.

### The boundary

**The first non-additive change to the local store requires a versioned
migration mechanism, and this record is superseded at that point.** Concretely,
that mechanism needs: a schema version persisted in the database, an ordered and
named list of steps, a record of which have run, a decision about what a
downgrade means when an older build opens a newer file, and a backup taken before
the first destructive statement.

The schema stamp is not a start on that list. It records that a whole setup ran,
never which step ran, and nothing reads it except the comparison that skips the
setup. A versioned mechanism needs its own ordered record of steps and replaces
the stamp rather than growing out of it.

None of that is built now, and building it now would be machinery for a change
nobody has needed in the store's lifetime. But the trigger is written down, so
the next person meets a stated boundary rather than a comment that only says what
this is not.

### Applying it to ADR 0005's change

`updated_at` and `deleted_at` are nullable entries in the tasks store's `columns`
list. `updated_at` is then seeded in its `afterColumns` with
`UPDATE tasks SET updated_at = created_at WHERE updated_at IS NULL` — creation
time is a true lower bound on when a task last changed, and the only true thing
the device holds about a row it has no other record of. A task nobody has edited
since really did last change when it was made. `deleted_at` gets no seed, because
there null **is** the value: it is what "this task is live" says.

### Applying it to the roadmap plan

**The roadmap plan is inside this record now, and was not when it was written.** It
was `_roadmap/plan.json`, read and written whole as one document, so schema change
there was a parsing concern rather than a DDL one and this record did not reach it.
Since 2026-09-05 it is a `roadmap_plan` row in `backlog.db`, so it does.

Note which half, because the seam is easy to get wrong. *Creating* that table is not
one of the three shapes above, which are all column-level — it is covered by local ADR
0003's idempotent `IF NOT EXISTS` DDL, the first part of the roadmap store's setup,
which is why a database full of tasks gains the table and loses nothing. Any *column* added to `roadmap_plan` later
is this record's business and must take one of the three shapes.

One asymmetry is worth recording, because it looks like a violation and is not.
`roadmap_plan.updated_at` is `NOT NULL` with no default, which no added column may
be. The rule on added columns governs a column added by `ALTER TABLE` to a table
that already has rows, which cannot answer for the rows that predate it; this
column shipped with its table, so no row has ever been without one. That is also why it needs no seeding
statement of the kind `tasks` required.

The Inbox's three tables — `inbox_items`, `inbox_lists`, `inbox_groups`, in
`backlog.db` since 2026-09-15 (local ADR 0009) — sit on the same seam: their
creation is ADR 0003's `IF NOT EXISTS` DDL, their `updated_at` is `NOT NULL` for
the reason `roadmap_plan`'s is, and any column added to them later takes one of
the three shapes above, as an entry in the inbox store's `columns` list. Six
have been added to `inbox_items` so far, three of them `NOT NULL` with a constant
default.

## Consequences

Positive:

- **D3 is discharged as written.** It asked for a story, not a framework, and the
  story is now findable, named, and bounded.
- **The rule that was already being followed becomes checkable.** Three past
  changes were made this way and none of them recorded that a rule existed; the
  next one can be judged against something.
- **The next person hits a stated trigger.** "Is this additive?" is a question
  with an answer, where "there is no migration mechanism" was an invitation to
  either over-build or improvise.
- Nothing is built that is not needed, and inherited ADR 0014's prohibition is
  satisfied structurally rather than by care.

Negative:

- **An older build opening a newer database is still undefined.** The columns it
  does not know about are simply ignored, and a write from it silently drops
  whatever they held — for `deleted_at` that means an older build resurrects a
  deleted task. Tolerable while one person runs one version per machine, and a
  real hole the moment two versions of the app share a database. The versioned
  mechanism above is where it gets fixed; recorded here so it is not discovered.
- **The whole setup runs on the first open after its definition changes.** A
  stamped open costs one indexed read, so the list no longer slows every open.
  But the first open after an upgrade still runs every probe and every no-match
  `UPDATE`. This is still not a shape that scales to a long list, which is another
  reason the boundary matters.
- **A build older than a seed can leave rows the seed is meant to repair.** The
  seeds repair rows that only an old build writes: one from before `updated_at`
  writes rows with a null `updated_at`, and one from before `follow_up` was retired
  writes that type. If such a build writes to a file a newer build has already
  stamped, the newer build reads its own stamp on the next open and skips the
  setup. Those rows stay unrepaired until the definition next changes: a null
  `updated_at` never syncs, and a `follow_up` row throws on every read. This is
  the older-build hole above, and the versioned mechanism is where it gets fixed.
- **Two builds with different definitions sharing one file rerun the setup each
  time they alternate.** Each build overwrites the other's stamp, so each pays the
  whole setup on its first open after the other one opened the file. Two worktrees
  on the debug workspace's shared `backlog.db` do this. It costs time only, because
  every statement is safe to run again.
- **No backup is taken**, because nothing destructive is permitted. That is only
  as safe as the prohibition, and the prohibition is enforced by review rather
  than by code.

Neutral:

- The store still has no migration version, and deliberately so. The idempotence
  requirement is what replaces it. `schema_stamps` is a "setup already ran" marker
  and not a version: it has no ordered steps, and nothing reads what ran before. A
  missing or wrong stamp costs one rerun of statements that are safe to rerun. The
  boundary to a versioned mechanism stands as written above.
- This does not change what is canonical, what the content format is, or where
  the store lives. ADR 0003 is untouched.

## Open questions

- **Where a check belongs, if anywhere.** Nothing enforces that a new statement
  in the bootstrap is one of the three allowed shapes; a reviewer does. Whether
  that is worth an architecture test is not decided.
*(The roadmap plan was listed here as an open question until 2026-09-05, when it
moved into `backlog.db` and this record began to reach it. See **Applying it to the
roadmap plan** above.)*
