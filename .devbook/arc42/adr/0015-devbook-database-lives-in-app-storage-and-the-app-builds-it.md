# ADR 0015: The devbook database lives in the app's storage, one per repository path, and the app builds it

```meta
date: 2026-09-25
related: [".devbook/arc42/adr/0004-knowledge-index-is-a-generated-local-database.md", ".devbook/arc42/adr/0008-knowledge-reads-from-a-branch-snapshot-when-there-is-no-clone.md", ".devbook/arc42/08-crosscutting-concepts.md#devbook-database", ".devbook/arc42/07-deployment-view.md#local-deployment-desktop", ".devbook/tech/tooling.md#devbook-database-writer", ".devbook/domain/devbook/features.md#a-devbook-that-stays-current"]
```

## Status

Accepted, 2026-09-25 — the owner's decision, taken while reviewing the pull
request that moved this repository's folders under `.devbook/`.

A **local** decision, numbered in the local sequence — not to be confused with
inherited ADR 0015 under `.devbook/arc42/adr/guidelines/`, which is about
resilience for outbound dependencies. Every reference below to a local ADR by
bare number means the local one.

It **supersedes** two sections of ADR 0004 — *Where it lives* and *The generator
is the only writer* — together with the location sentence of *One database, not
one per scope*, and it **closes** ADR 0004's first open question, how the app
invokes the generator, by removing the need to. Everything else ADR 0004 decided
stands: Markdown is canonical, the authored reading order is committed text, one
database per repository with a scope as `WHERE folder = ?`, the two tiers, the
refresh-is-an-optimisation rule, and the degrade ladder rung for rung. It
**amends** ADR 0008's *What this narrows in ADR 0004*, whose argument this
record makes unnecessary rather than wrong.

## Context

ADR 0004 put the database beside the folders it describes —
`.devbook/_meta/devbook.db` in this repository's layout, `_meta/devbook.db` in
the root layout — ignored by git, and written only by a Node script. Three
things about that arrangement turned out to cost more than the reasons for it
bought.

**A build output inside the repository, even an ignored one, is still inside the
repository.** Every layout needs its own ignore lines, times the `-wal` and `-shm`
sidecars, plus the name the file had before the Knowledge → Devbook rename. A
repository the app reads but this project does not control has no such lines, so
building a database there leaves an untracked file in somebody else's working
tree. The owner does not want generated state in a repository's structure at
all.

**Nothing built it where the app needs it.** Because the generator was the only
writer and how the app would start it was left open, the refresh paths ADR 0004
describes stayed unbuilt. A fresh clone, a new worktree, and any repository the
contributor never ran `node tools/devbook/build-database.mjs` in had no database,
so search there said it was unavailable until somebody opened a terminal. A
repository read from a branch snapshot (ADR 0008) could never have one: nothing
runs the generator inside the snapshot cache.

**The two ways of answering ADR 0004's open question were both bad for the same
reason the question was open.** Spawning Node from the desktop makes Node a
runtime dependency of an MSIX-packaged app and of the Android head. Porting the
parse to C# creates the second implementation "the generator is the only writer"
existed to prevent. The first cost lands on every user; the second lands on this
repository's tests. The second is the one a test can pay.

## Decision

**The database lives in the Backlog app's own storage, one per repository path,
outside every repository. The desktop app builds and refreshes it, in C#.**

### Where it lives

```text
<devbook cache folder>/_databases/<name>-<hash>/devbook.db
```

- **The devbook cache folder** is the one ADR 0008's branch snapshots already use,
  resolved the same way: `devbook-cache` under the storage folder by default —
  `%LOCALAPPDATA%\Backlog` in a release build, `%LOCALAPPDATA%\Backlog.Debug` in a
  debug build and the desktop web harness — or the folder named on Settings ›
  Storage. Snapshots and databases are the same kind of state: derived from a
  repository, disposable, rebuilt on demand, never backed up, and large enough per
  repository that a person with a small system drive wants a say in where it goes.
  One setting covers both.
- **`_databases`** starts with an underscore because a snapshot's folder is
  `<owner>-<name>` and a GitHub owner cannot start with one, so the two can never
  collide.
- **`<name>`** is the repository root folder's name, made path-safe, so a person
  browsing the folder can tell which repository a database belongs to.
- **`<hash>`** is the first 12 hexadecimal characters of the SHA-256 of the
  normalized absolute path of the repository root: the full path, trailing
  separator removed, lower-cased on Windows where paths compare case-insensitively.
  The path, not the remote or the name, is the key: two worktrees of one
  repository are two working trees with two sets of files, and each gets its own
  database. A branch snapshot is keyed by its own `tree/` folder, so a repository
  nobody cloned gets a database too.

A clone that moves, or a worktree that is removed, leaves its database behind
under a key nothing asks for. That is a cache entry nobody reads, not a wrong
answer; deleting the cache folder is always safe. Pruning is not built.

### The app is the writer

`Backlog.Infrastructure.Devbook` gains a builder beside its reader. It walks the
adopted devbook folders, parses each chapter's headings and `meta` block the way
the devbook generator does, resolves the committed `_reading-order.json` files
into the outline, slices chapter text into `text` and `search_text`, hashes it,
fills the FTS5 index, and copies each `_archify/index.json` into rows — every
table ADR 0004 names, with the same columns and the same values.

It builds into a temporary file beside the target and renames it into place, as
the Node writer always did, so a reader never opens a half-built database. The
reader keeps opening read-only: the builder creates the file, the reader never
does, and "absent" stays a state of its own.

**Validation is not ported.** The builder records in `problem` only what it could
not read — a file, an `_archify/index.json` — and not the convention's metadata
rules. A broken reference or a status off a folder's ladder remains the devbook
checker's to report, in a terminal and in CI, which is where somebody can fix it.

**The schema is one text for both languages.** The DDL moves from a string inside
`tools/devbook/devbook-schema.mjs` to `tools/devbook/devbook-schema.sql`, which
the Node tooling reads and the C# assembly embeds, with the schema version stated
in the file. Neither side restates it.

### When it builds

On open, in the background, never waited on:

- **When a repository's database is first asked for** — the first read in a
  session, and again after a quiet interval — the app schedules a check for that
  repository and the read carries on down the ladder with whatever exists now.
  The check compares the database's `chapter` rows with the files on disk, one
  `stat` each; an absent database, a schema version it does not know, a file
  added, removed, resized or touched is a rebuild. One check per repository at a
  time, off the UI thread, cancelled when the app stops.
- **On the app's own write** — nothing, as before. The edited file is drifted, the
  reader serves it from its Markdown, and the next check rebuilds.
- **Not on startup** — ADR 0004's rule stands. Startup schedules nothing; a
  repository nobody opens costs nothing.

The debounced watcher and the idle pass ADR 0004 describes remain unbuilt, as
optimisations nobody needs yet. A rebuild is whole rather than incremental: this
repository's corpus is about 1,100 chapters and builds in a few seconds, and an
incremental writer is a second set of rules about what a change touches.

### What becomes of `build-database.mjs`

It stays, as a command-line tool with two jobs, and it no longer writes anything
into a repository:

- **CI's build check.** `devbook-metadata.yml` still builds the database against
  the real corpus as a blocking step, with `--check`, which builds to a temporary
  file and deletes it.
- **The reference the C# builder is held to.** `--out <file>` writes the database
  where it is told. A test builds this repository's own `.devbook/` with both
  writers and compares every table except `meta` and `problem`, row for row.
  The .NET test job installs Node for it.

The two parsers are kept in step by that test and by nothing else. The Node
writer imports the devbook plugin's own generator, which the plugin updates on
release; a release that changes how a chapter parses changes the Node output, the
comparison fails, and the C# builder is updated in the same pull request that
takes the release. That is ADR 0004's named drift hazard, accepted rather than
avoided, with the treatment ADR 0004 prescribed for it — pinned by a test from
both sides, as `DiagramSourceHash` already is.

### Databases already inside a repository

Ignored. The reader stops looking inside repositories, so a leftover
`.devbook/_meta/devbook.db` or `_meta/devbook.db` is never opened again, and the
app deletes nothing in a working tree it does not own. The `.gitignore` entries
for them go, so a leftover shows up once as an untracked file; its owner deletes
it by hand, and the `update-devbook-index` command says how.

## Consequences

Positive:

- Nothing generated is inside any repository, so nothing needs ignoring, and a
  repository this project does not control is left exactly as it was found.
- Every clone, every worktree and every branch snapshot gets a database without
  anybody running a command, so search works wherever browsing does, after the
  first background build.
- The desktop gains no Node dependency, and the Android head and the IDE channels
  could build their own database with the same C# code.
- ADR 0004's open question about invoking the generator is gone rather than
  answered.

Negative:

- Two implementations of the parse, one of them a port of code a plugin owns and
  changes. The comparison test is the only thing that notices when they differ,
  and it needs Node 22 or later wherever the .NET tests run.
- The app does indexing work: a few seconds of background CPU the first time a
  repository is opened and after every change a check notices.
- Databases for moved clones and removed worktrees accumulate until somebody
  deletes the cache folder.
- A database is no longer beside the folders it describes, so finding it means
  knowing the storage folder and the key. The Storage settings tab shows the
  cache folder; the key is in the folder name.

Neutral:

- The degrade ladder is unchanged, and so is every consumer of the database
  except the one function that says where it is.
- Semantic retrieval stays wired and empty. Which embedding model, and whether
  chapter text belongs in the database, remain ADR 0004's open questions.
