# ADR 0003: SQLite is the canonical local task store; markdown is the content

```meta
status: accepted
related: [".arc42/02-constraints.md#technical-constraints", ".arc42/08-crosscutting-concepts.md#storage-and-sync", ".arc42/07-deployment-view.md", ".arc42/adr/0002-backlog-module-owns-the-entry-text-language.md", ".domain/tasks/domain.md#task"]
issue: null
```

## Status

Accepted. Supersedes the "markdown is the canonical format" constraint recorded in
`.arc42/02-constraints.md` and restated in chapters 04, 06, 07 and 08, all of which
this ADR corrects. It does **not** supersede ADR 0002.

## Context

Every task was a markdown document with YAML frontmatter, one file per task under
`_backlog/`. In front of the documents sat two derived files: `index.json`, a list
of summary projections rebuilt by re-reading every document, and
`_backlog/_meta/index.json`, a sidecar holding manual list order because the order
could not live in the document without the rewrite fighting the editor.

That is three files that can disagree about the same task, and the code showed it:

- Listing the backlog read the index, then re-read every document behind it one at
  a time, because the index was derived and could be stale. The projection type
  existed only to be the row in that index.
- Saving a task rewrote the document, then rewrote the order sidecar, then rebuilt
  the whole index by re-reading every other document.
- The frontmatter serializer needed a repair pass on the way back in, because
  YamlDotNet writes a date-shaped .NET type as a nested map of alternative
  renderings that then has to be flattened. A round trip that needs a repair pass
  is not a round trip.
- Storage-shaped concerns leaked into the aggregate. `EntryView`, a presentation
  preference, is held on the aggregate for one reason, stated in its own doc
  comment: the markdown is canonical, so a preference the file did not keep would
  be deleted by the next save.

The frontmatter also tied the store to the knowledge-folder metadata conventions —
the ```meta fence — which is bookkeeping for a documentation generator and not a
fact about a task.

None of this was wrong when a task *was* a document a person might open in an
editor. It stopped being a fair trade once the app owned the editing experience.

## Decision

**One local SQLite database is the canonical store for tasks. The task's content
stays markdown, as a text column.**

The distinction is the whole of the decision: *markdown as a storage format* is
gone; *markdown as the content of a task* is unchanged and is still the published
language ADR 0002 defines. `EntryTextParser` is untouched, so the sigil line, the
`##` sub-item headings, and every token a person can type mean exactly what they
meant before.

### What the schema is

One `tasks` table. Scalar columns for the scalar fields; JSON text columns for the
six owned collections (`tags`, `repo_ids`, `depends_on`, `sub_items`,
`usage_events`, `projections`).

A task is one consistency boundary and the port only ever reads or writes a whole
one, so those collections are payload rather than query surface. Child tables would
buy six joins and a cascade policy for a shape nothing queries into. The fields
that ordering and filtering actually run on — status, priority, rank, area, due
date, creation time — stay real columns, indexed as `(sort_order, created_at DESC)`
to match the existing rank-then-recency rule.

### How values are written

Enums are stored as the ubiquitous-language wire tokens already used by the old
frontmatter (`in_progress`), not as ordinals: the database should read
the way the domain reads, and an ordinal would silently change meaning the day
somebody inserts a member into the middle of an enum. `Recurrence` and `EntryView`
go through `EntryTextParser`'s own token helpers, so the storage vocabulary and the
text vocabulary cannot drift apart. `remind_at` is written without an offset,
because the domain holds it as wall-clock intent and an offset would pin a reminder
to the zone it was written in.

### Raw ADO.NET, not an ORM

`Microsoft.Data.Sqlite`. The port has four members over one table; an ORM's
schema-migration machinery would be a second thing to version for a single local
file, and the schema statements are idempotent `IF NOT EXISTS` DDL run on the way
into every operation so a first run, a moved folder and a deleted file all behave
the same.

### Its own project

`Backlog.Infrastructure.Sqlite`, not a file inside
`Backlog.Infrastructure.FileSystem`. Infrastructure projects in this solution are
named for the technology they wrap, and the file-system project describes itself as
"markdown + JSON on local disk". It keeps the adapters that really are files: the
workspace settings, the feature flags, the knowledge folder resolver, and Roadmap
Planning's stored plan document.

This decision is about the task store specifically, and not a claim that a database
is the right home for everything. Roadmap Planning's plan arrived as JSON on disk in
the same period and stays there: it is one document per workspace, read and written
whole, with none of the per-item indexing that made markdown-per-task expensive.

### No migration

A clean break. Existing markdown under the workspace root is ignored and left on
disk. Chosen deliberately over an importer: the product has one user, the old files
are readable by hand if anything is wanted from them, and an importer is code that
would have to be correct forever to be worth the one time it runs.

## Consequences

Positive:

- One file holds the truth, so no two files can disagree about a task. The derived
  index, the order sidecar, and the summary projection type are all deleted.
- Listing the backlog is one query returning whole aggregates, replacing an index
  read followed by one document read per task.
- Saving a task is one statement instead of a document write plus a sidecar write
  plus a full index rebuild over every other task.
- The content round-trips byte for byte. Nothing normalises it, because nothing
  parses it.
- The store no longer depends on the knowledge-folder metadata conventions.
- Backing up or moving a backlog is one file to copy.

Negative:

- A task is no longer readable or editable outside the app. Anyone wanting to see
  the raw data needs a SQLite browser rather than a text editor, and hand-editing
  is gone. This is the real cost of the decision, and it is accepted: the app owns
  the editing experience, and a format nobody hand-edits was paying for a
  capability nobody used.
- Data already in markdown is not carried over.
- WAL mode writes `-wal` and `-shm` sidecars beside the database. If somebody points
  the workspace root at a cloud-synced folder, a database is a worse thing to sync
  than a set of text files were — a conflicted copy of a database is not
  mergeable, where two markdown files could be.
- The repeat grammar's existing limitation is now also the store's: a
  weekday-restricted repeat has exactly one spelling (Monday to Friday), so any
  other weekday set keeps its interval and loses its days. Deliberate — storing a
  repeat the text cannot express would mean the next metadata rewrite dropped it,
  and two stores that disagree is worse than one that is honestly narrow.

Neutral:

- `EntryView` still sits on the aggregate. Its stated reason — that a preference
  kept outside the canonical markdown would not survive — is now weaker, since the
  preference is a column and the text is not the store. Left alone here because
  moving a field off an aggregate is a domain change, not a storage one.
- The aggregate is now `TaskItem` in code, and the ubiquitous-language term is
  **Task**. `TaskItem` rather than the bare `Task` because the module's own
  namespace declares dozens of `Task`-returning async methods and `TaskType.Task`
  already means something narrower. The `.domain` vocabulary has not been renamed
  to match yet; that is a governed-vocabulary change and belongs to `orch-domain`.
