# ADR 0004: One generated local database holds the derived knowledge layer; markdown stays canonical

```meta
status: proposed
related: [".arc42/02-constraints.md#technical-constraints", ".arc42/08-crosscutting-concepts.md#knowledge-index", ".arc42/07-deployment-view.md#local-deployment-desktop", ".arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".domain/second-brain/features.md#repository-knowledge-areas", ".tech/tooling.md#knowledge-meta-generator", ".tech/shared.md#sqlite"]
issue: null
```

## Status

Proposed. Nothing is built yet; this records the direction and the questions it
deliberately leaves open.

A **local** decision, numbered in the local sequence — not to be confused with
inherited ADR 0004 (result objects for expected failures) under
`.arc42/adr/guidelines/`. Every reference below to ADR 0001–0004 without a
qualifier means the local one.

It **extends** local ADR 0003 to a second corpus rather than superseding it. Nothing
here changes what canonical means for a task, and — the distinction matters —
nothing here makes a database canonical for knowledge. The knowledge folders stay
markdown, hand-edited and diffed in pull requests. Only the generated layer over
them moves.

## Context

`.arc42/`, `.domain/`, `.backlog/`, `.tech/` and `.design/` are markdown, and
the [knowledge-meta generator](../../.tech/tooling.md#knowledge-meta-generator)
derives two JSON artifacts from them per scope: `graph.json`, the reference
graph, and `index.json`, the ordered reading outline. Six scopes, twelve files,
about 1.8 MB — the repository-wide `_meta/graph.json` alone is 836 KB for 772
nodes and 1540 edges.

**Most of the argument for changing this has already been half-answered, and the
half-answers are what make the case.**

**On merge conflicts, the repository has already conceded the point.**
`knowledge-meta.yml` now reports a stale index as a warning instead of failing,
`knowledge-meta-nightly.yml` reconciles the default branch in one pull request
nobody has to rebase, and `build/Update-KnowledgeIndex.ps1` refreshes a branch on
demand. That was the right move, and it removed the worst of the churn behind
sixty-seven commits, roughly 82,000 lines, and merge commits named for the problem
— `0d55364 Merge origin/main, regenerating the indexes rather than merging them`.

But it did not remove the files, and every branch that does refresh still collides
with every other that did, with the nightly reconciliation landing on top of
long-lived branches. More to the point, look at what is now being committed: an
artifact that is *expected* to be stale, that CI declines to enforce, and that
every consumer must re-verify against its sources before trusting a row. The
reasons the derived-artifacts convention gives for committing an artifact have
been argued away one at a time, and what remains in version control is a build
output nobody may rely on.

**On being read, the position has reversed — in this decision's favour.** When
this was first sketched, two things read the indexes. Now the derived layer is
load-bearing: `KnowledgeIndexReader` lists a folder without opening a markdown
file, `LazyKnowledgeList` defers the parse to the one chapter a reader opens,
`KnowledgeAtlas` draws every folder from `_meta/graph.json`, and the domain,
arc42, design and technology panels all build from the index.
`RoadmapItemRollupService` still parses the whole repository graph to total the
effort behind a roadmap item.

That work also established the reading discipline this decision depends on, which
is therefore not something it has to invent: an entry whose file is newer than the
index is re-read from its markdown, an unrecognised `schemaVersion` falls back to
scanning, and a folder with no index behaves exactly as it did before one existed.
The degradation ladder below is a restatement of what `KnowledgeIndexReader`
already does, applied to a different container.

**What JSON still costs, now that it is load-bearing.** A consumer parses a whole
document to answer a narrow question — 836 KB read to total a few hundred effort
values. The corpus is serialized twice, once into the repository rollup and once
per folder, which is how 772 nodes become 1.8 MB. The authored reading order is
interleaved in the same file as re-read titles and statuses. Archify state is
another JSON read per folder. And there is nowhere to put anything that is not a
graph or an outline.

**Retrieval is the part nothing has answered.** There is no full-text index and no
semantic one. An AI-facing surface — an MCP server, an in-app assistant, a
retrieval step in a Copilot session — would have to parse the corpus itself, and
so would the mobile app and the VS Code extension, each in its own language. Four
markdown parsers for one body of markdown. This is now the largest gap between
what the knowledge folders hold and what anything can ask of them.

There is one constraint that shapes any answer: **`index.json` is not purely
derived.** As `outline.mjs` states in its own header, a directory's reading order
and its `root: true` entry are *authored in that file* and carried forward across
regeneration; only titles and statuses are re-read from the markdown.
Regeneration is a fixed point, not a projection. So "stop committing the derived
files" is not available as written — the authored half has to be separated from
the derived half first.

## Decision

**One SQLite database per knowledge repository holds the derived layer. It is
generated and not committed. Markdown stays canonical, and the authored reading
order stays a committed text file.**

### What is authored and what is derived

| Authored — stays committed text | Derived — lives in the database |
|---|---|
| the markdown chapters and their `meta` blocks | the reference graph: nodes, edges, node attributes |
| each directory's reading order and root document | the resolved outline: titles and statuses with that order applied |
| the hand-written Archify specifications | chapter text, source hashes, and the diagram artifact index |
| | the lexical (FTS) and semantic (embedding) retrieval indexes |

Separating the reading order out of `_meta/index.json` into a small committed file
of its own is what makes the rest possible, and it is worth doing for its own
sake. Today one file interleaves an authored directory listing with re-read titles
and statuses, so moving a chapter from `draft` to `active` rewrites that file, and
the graph, and both of their scoped copies. Once the order is stated alone, the
same edit rewrites nothing that is in git.

This is the same reasoning that already moved the order out of a `meta` fence and
into the index: a directory listing is a fact about a directory, stated once, in
the artifact that describes it. It just should not be stated in the same file as
things nobody authored.

### One database, not one per scope

`_meta/knowledge.db` at the repository root. A scope becomes `WHERE folder = …`,
not another file — which is what removes the double serialization the context
describes, without a consumer having to choose between a scoped file and a
repository-wide one.

The location rule from the derived-artifacts convention is unchanged: `_meta/`,
one level below the thing it describes. Being repository-wide, the root `_meta/`
is exactly where that convention already puts a cross-cutting artifact.

### SQLite

Not reasoned from scratch — matched to ADR 0003, whose sentence transfers intact:
the database is the store, markdown is the content. `Microsoft.Data.Sqlite` is
already a dependency, FTS5 puts lexical search in the same file, and one file is
one thing to build, copy, delete and ignore.

Raw ADO.NET on the reading side, as in ADR 0003, and for a sharper reason here:
the schema is created and written by the Node generator and only ever read from
C#. An ORM's migration machinery on the reading side would be versioning a schema
it does not own.

### Two tiers, and the semantic tier is optional

**Tier 1 — structural.** Graph, outline, chapter text, hashes, diagram state.
Deterministic, offline, no network, seconds to build. This tier keeps the
convention's determinism rule and stays checkable.

**Tier 2 — semantic.** One embedding per chapter, keyed by the chapter's content
hash so an unchanged chapter is never re-embedded. This tier needs a model, is
versioned by it, and is not reproducible byte for byte, so it must sit outside any
determinism or staleness check.

A reader must work correctly against tier 1 alone. Absent embeddings, retrieval
falls back to FTS5, which is the right default anyway: on a documentation corpus
full of exact identifiers, hybrid lexical-plus-semantic beats semantic alone.

### No vector index yet

The corpus is 639 chapters. Embeddings are stored as a `BLOB` and cosine
similarity is computed in the reader; a brute-force scan over a few thousand
vectors costs less than the query that fetched them. `sqlite-vec` and its
equivalents are native loadable extensions — a real deployment problem inside an
MSIX package and on Android — bought for a corpus three orders of magnitude below
where an approximate index starts paying. Vector search sits behind a port so an
index can arrive later without moving its callers.

### Generated, never committed

The database, and its `-wal` and `-shm` sidecars, are ignored by git. It is a
build output.

That is the whole of the merge-conflict answer, and it is worth being precise
about why: the win comes from *not committing derived output*, not from the output
being a database. A committed `.db` would be worse than the JSON in one respect
and better in another — unmergeable, but failing loudly instead of merging
line by line into something that parses and is wrong. Ignored, the question does
not arise.

### The generator is the only writer

The Node generator creates the schema and is the only thing that writes to the
database. The app reads and never writes — not even to repair a row it can see is
stale.

That rule keeps the parse in one place. The alternative — letting the app
re-index a file it has just written through `KnowledgeMarkdownStatusWriter` or
`KnowledgeChapterWriter` — puts a second implementation of `metadata.mjs` in C#
and makes it load-bearing for correctness rather than for speed. That is the drift
hazard the Archify hash rule already demonstrates, and there is no reason to take
it twice. A file the app has just edited is simply a *drifted* file, and a drifted
file already has a defined answer: read the markdown. The user's own edit is
served correctly and immediately by the fallback path rather than by new
machinery.

### How the database is kept current

Refresh is an optimisation, never a precondition. Every path below can fail, be
switched off, or never run at all, and the app still shows correct knowledge — it
just reads more markdown. Cheapest first:

- **On the app's own write** — nothing happens. The edited file is drifted, and
  the reader serves it from markdown until a generator run reconciles it.
- **On open** — when an area is opened, the reader stats the files that area is
  about to present and takes the markdown for any whose recorded size,
  modification time or hash no longer matches. One `stat` per file, never a
  re-parse of the corpus. This is the check that makes an unattended database safe
  to read at all.
- **While a folder is in view** — a debounced file-system watcher over the open
  area, so an edit made in another editor, or a `git pull` landing underneath,
  appears without reopening the pane. Debounced and coalesced into a single pass,
  because a branch switch changes hundreds of files at once.
- **In the background** — a low-priority pass runs the generator over repositories
  and areas nobody has opened, and computes the embeddings the structural tier does
  not need. Idle-triggered, cancellable, and never on the path of a panel waiting
  to draw.
- **On demand** — the generator command a contributor already runs in a terminal,
  and the scheduled job that reconciles the default branch. Unchanged by this
  decision.

**Not on startup.** Startup does one `stat` per registered repository to learn
whether a database exists, and nothing more. Several repositories may be
registered, most will not be opened in a given session, and indexing them all up
front spends a certain cost on a speculative benefit — in a client whose startup
time the user is watching. Startup schedules work; it does not perform it.

Because the generator is a separate process and SQLite runs in WAL mode, a
background refresh and a reading panel do not block each other: a reader sees the
last committed state while a rebuild is in flight, and a reader that does meet a
busy database falls back rather than waits.

### How a reader degrades

Not a boolean. Each rung is a defined state with defined behaviour, and only the
last is visible to the user:

| State | What the reader does |
|---|---|
| Database current for this file | Serves from the database. |
| Database present, this file drifted | Reads this file's markdown and serves that — correct content, one file's parse. |
| Schema version unrecognised | Ignores the database entirely and reads markdown, so a database written by a newer generator can never break an older app. |
| Database absent, locked or unreadable | Reads markdown. This is the path the knowledge panels take today. |
| Retrieval, no embeddings | Full-text search answers; search by meaning is absent, not broken. |
| Retrieval, no database at all | **Search is unavailable, and says so.** |

That last row is the one honest exception. Browsing degrades to markdown because
browsing touches the handful of files on screen; search cannot, because scanning
the corpus per query is not a fallback but a hang. Everything else about an
unindexed repository keeps working — its areas open, its chapters render, its
diagrams draw — and only search is missing until an index exists.

None of this is new, which is most of why the decision is worth taking now rather
than earlier. `KnowledgeIndexReader` already returns nothing for a folder with no
index, already refuses a `schemaVersion` it does not recognise, and already
re-reads an entry whose file is newer than the index it came from; the panels
already fall back to scanning the directory, because that is what they did before
an index existed. This changes what the reader opens, not how it decides whether
to trust what it finds.

### Where it lives

In the repository that owns the knowledge folders, not in the workspace root.
`IKnowledgeFolderSource` resolves an area per registered repository, so the app
routinely reads knowledge in repositories it did not build and does not own. A
database beside the folders travels with them; one in the workspace root would be
a second place that can disagree about a repository the workspace does not
control.

### Archify moves its index and nothing else

Each folder's `_archify/index.json` becomes rows: chapter, ordinal, fence hash,
type, kind, specification path, artifact path, state, checks passed. The
specifications are hand-authored and stay on disk and in git. The rendered HTML
stays on disk because it is what the WebView loads, with the database holding the
pointer.

The match rule is untouched: a diagram still resolves by the SHA-256 of its
normalised fence, and `normalizeDiagramSource` in `archify-artifacts.mjs` and
`DiagramSourceHash` in `DiagramArtifacts.cs` must still agree exactly, still
pinned by tests on both sides. A database does not fix a duplicated rule; it just
stops the lookup reading a file per folder.

### Where this departs from the derived-artifacts convention

`knowledge-derived-artifacts.instructions.md` requires a derived artifact to be
JSON, to carry the standard envelope, and to be committed so it can be read
without a build step and reviewed in diffs. This decision keeps the location rule
and the determinism rule for tier 1, and departs on format and on committing.

The convention's reasons for committing are answered differently rather than
ignored: what gets reviewed in a diff is the markdown, which is the only thing
anybody authored; a reader with no database falls back to the markdown; and a
consumer that needs the database without Node available takes it from CI as a
published artifact rather than from the working tree. Where the two disagree, this
ADR is this repository's answer and the divergence is deliberate.

## Consequences

Positive:

- Two branches editing different chapters conflict on nothing, and the 1.8 MB of
  derived output in git goes to zero. The warning-only check and the nightly
  refresh already stopped most of this churn; removing the files finishes it and
  makes `Update-KnowledgeIndex.ps1` safe to run in a branch at any time.
- A chapter moving from `draft` to `active` rewrites one line of markdown, where
  today it rewrites four committed files whenever anything refreshes them.
- A narrow question becomes a narrow read. The panels already avoid re-parsing the
  corpus, so the remaining win is per-query rather than per-load: the roadmap
  rollup stops parsing an 836 KB document to total a few hundred effort values,
  and the Archify lookup stops reading a JSON file per folder.
- Desktop, mobile, the IDE extension and a future MCP server read one artifact
  with one schema instead of each carrying its own markdown parser. This is the
  benefit that compounds, and it is the reason to do this rather than simply
  delete the JSON.
- Retrieval by words and by meaning becomes possible, which it currently is not.
- Correctness never depends on the refresh running. Every refresh path can be
  switched off and the app still shows what the folders say, because the floor of
  the ladder is the reader it already has. That is already true of the JSON
  indexes, so the property is inherited rather than promised, and it is what makes
  this safe to adopt incrementally and safe to leave half-built.

Negative:

- The schema is touched from two languages — written by the Node generator, read
  from C#. That is a contract that can drift silently, exactly as the Archify hash
  rule already can, and it needs the same treatment: pinned from both sides by
  tests.
- The fallback path is neither optional nor rare, so the code carries two readers
  — database and markdown — rather than one, and the slower one has to stay
  correct and stay tested.
- Refresh still has to be orchestrated — a watcher, a debounce, an idle trigger, a
  cancellation story — even though none of it is required for correctness.
  Machinery that only buys speed still has to be right; the consolation is that
  getting it wrong redraws a panel too often rather than showing the wrong thing.
- Search is the one capability that an unindexed repository does not have, so
  refresh is optional for browsing and load-bearing for retrieval. The UI has to
  say which state it is in rather than returning nothing and looking broken.
- Embeddings pin the database to whichever model produced them. Changing the model
  re-embeds the corpus.
- The derived layer stops being diffable. A question about what it says is answered
  with a query rather than by reading a file in a pull request. Accepted, on the
  same grounds as ADR 0003: nobody authored it, and its only correct content is
  whatever the generator emits.

Neutral:

- Azure is neither addressed nor blocked. Tier 1 is ordinary relational data and
  tier 2 is a vector column; both port to PostgreSQL with `pgvector` or to a
  hosted search service without changing what the callers ask for, provided vector
  search stays behind its port.
- The Desktop App diagram in `.arc42/05-building-block-view.md` still shows
  `Local Storage (Markdown files)` and `JSON Indexes`. It was already inaccurate
  after ADR 0003, this ADR does not correct it, and correcting it means
  re-authoring the Archify specification behind it — a diagram change rather than
  a decision.

Open, and deliberately not decided here:

- **How the app invokes the generator for the background and on-open refresh.**
  Since the generator is the only writer, every refresh path the app drives has to
  start it. Spawning the vendored Node generator as a process keeps one
  implementation of the parse and makes Node a runtime dependency of the desktop
  app; reimplementing it in C# removes the dependency and creates a second
  implementation to keep in step — the thing "the generator is the only writer"
  exists to prevent. Neither is chosen, and the ladder is what makes deferring it
  safe: with no answer at all the app simply runs on markdown, which is exactly
  what it does today.
- **Which embedding model**, and therefore what the database is pinned to.
- **Whether chapter body text is stored** in the database. FTS and chunking both
  need it, and it duplicates the markdown either way.
