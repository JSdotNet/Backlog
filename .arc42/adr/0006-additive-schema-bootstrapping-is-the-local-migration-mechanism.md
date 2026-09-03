# ADR 0006: Additive, idempotent bootstrapping is the local store's migration mechanism

```meta
status: proposed
related: [".arc42/08-crosscutting-concepts.md#storage-and-sync", ".arc42/11-risks-and-technical-debt.md", ".arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md", ".arc42/adr/guidelines/0014-persistence-and-repository-boundaries.md"]
issue: null
```

## Status

Proposed. Written to discharge the debt recorded as **D3** in
`.arc42/11-risks-and-technical-debt.md`, which says the migration story is owed
before the first schema change that has to preserve existing user data — and
that change is the `updated_at` and `deleted_at` columns local ADR 0005
requires.

A **local** decision, numbered in the local sequence. Every reference below to
ADR 0001–0006 without a qualifier means the local one; inherited ADRs are named
as such.

This record does not change what the local store is. Local ADR 0003 stands
whole. What it does is write down the rule that was already being followed, and
say where that rule stops being enough.

## Context

**The debt was recorded as missing machinery, and that is the wrong diagnosis.**
D3 reads *"No migration mechanism for the local SQLite store. The adapter creates
its schema and adds columns idempotently, but there is no versioned migration
path."* Inherited ADR 0014 records the same gap, and asks for two things that
pull in opposite directions here: *"Migrations are owned per module, created in
the same change as the model change that needs them, named for business
intent"*, and *"Destructive automatic migration at startup is prohibited."*

What the adapter actually does today has been doing a migration's job for three
schema changes already, without being called one:

- `effort`, `import_plan_id` and `import_item_id` were each added to a live table
  by `EnsureColumnAsync`, which reads `PRAGMA table_info(tasks)` and issues one
  additive `ALTER TABLE` when the column is absent.
- `NormalizeRetiredTypeAsync` rewrites the retired `follow_up` task type to
  `task`, because `EnumMap.ParseType` no longer knows the word and a row still
  carrying it would throw on every read — somebody's entry lost to a rename.

Both run unconditionally on the way into every operation. Both are true after
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

**The local store's migration mechanism is additive, idempotent bootstrapping
performed by the adapter on every open. It is a real mechanism with a stated
boundary, not an absence of one.**

### What is allowed

Three shapes, and only these three:

| Shape | How | Why it is safe |
|---|---|---|
| **Add a column** | `EnsureColumnAsync` — `PRAGMA table_info` then one additive `ALTER TABLE ... ADD COLUMN`, nullable. | Adds information. No existing value is read, moved, or destroyed. |
| **Seed a new column for existing rows** | One `UPDATE ... WHERE <column> IS NULL`. | Writes only where there was nothing. After it runs no row matches it again. |
| **Rewrite a retired value to its replacement** | One `UPDATE ... WHERE <column> = '<retired>'`. | The retired value is unreadable by definition — the code that understood it is gone — so the row was already lost. Matches nothing once it has run. |

Every statement must be **idempotent by construction** rather than by
bookkeeping: true after it runs, and matching nothing the next time. That
property is what makes running the whole set on every open cheaper than
remembering what has run, and it is the reason no version column is needed.

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

**A column added this way is nullable, always.** `ALTER TABLE ADD COLUMN` cannot
add a `NOT NULL` column to a populated table without a default, so the schema in
`CREATE TABLE` and the schema an `ALTER` produces must agree on nullability or a
fresh database and an upgraded one diverge. Where the domain wants a non-nullable
value, the seeding `UPDATE` supplies it and the read coalesces what the seed
could not reach.

### The boundary

**The first non-additive change to the local store requires a versioned
migration mechanism, and this record is superseded at that point.** Concretely,
that mechanism needs: a schema version persisted in the database, an ordered and
named list of steps, a record of which have run, a decision about what a
downgrade means when an older build opens a newer file, and a backup taken before
the first destructive statement.

None of that is built now, and building it now would be machinery for a change
nobody has needed in the store's lifetime. But the trigger is written down, so
the next person meets a stated boundary rather than a comment that only says what
this is not.

### Applying it to ADR 0005's change

`updated_at` and `deleted_at` are added as nullable columns by
`EnsureColumnAsync`. `updated_at` is then seeded with
`UPDATE tasks SET updated_at = created_at WHERE updated_at IS NULL` — creation
time is a true lower bound on when a task last changed, and the only true thing
the device holds about a row it has no other record of. A task nobody has edited
since really did last change when it was made. `deleted_at` gets no seed, because
there null **is** the value: it is what "this task is live" says.

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
- **The mechanism runs on every open**, so its cost grows with the number of
  statements. A `PRAGMA` and a handful of no-match `UPDATE`s over a single-table
  personal database is nothing, and this is not a shape that scales to a long
  list — which is another reason the boundary matters.
- **No backup is taken**, because nothing destructive is permitted. That is only
  as safe as the prohibition, and the prohibition is enforced by review rather
  than by code.

Neutral:

- The store still has no version column, and deliberately so — the idempotence
  requirement is what replaces it.
- This does not change what is canonical, what the content format is, or where
  the store lives. ADR 0003 is untouched.

## Open questions

- **Where a check belongs, if anywhere.** Nothing enforces that a new statement
  in the bootstrap is one of the three allowed shapes; a reviewer does. Whether
  that is worth an architecture test is not decided.
- **Whether the roadmap plan needs the same record.** `_roadmap/plan.json` is
  read and written whole as one document, so schema change there is a parsing
  concern rather than a DDL one, and this record does not reach it.
