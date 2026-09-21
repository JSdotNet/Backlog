# ADR 0008: The Devbook reads from a cached branch snapshot when there is no clone; only a clone is editable

```meta
status: proposed
related: [".arc42/08-crosscutting-concepts.md#storage-and-sync", ".arc42/08-crosscutting-concepts.md#devbook-database", ".arc42/adr/0004-knowledge-index-is-a-generated-local-database.md", ".arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md", ".arc42/adr/guidelines/0015-resilience-for-outbound-dependencies.md", ".domain/devbook/features.md#repository-devbook-areas"]
issue: null
```

## Status

Proposed.

> **Amended 2026-09-15: Knowledge → Devbook.** The context and module this record
> is about were renamed, and with them the code it names (`KnowledgeSnapshotCache`
> → `DevbookSnapshotCache`, `knowledgeBranch` → `devbookBranch`,
> `useLocalKnowledgeFolder` → `useLocalDevbookFolder`, each with a read fallback
> for the old persisted value; the default cache folder `knowledge-cache` →
> `devbook-cache` with no fallback — snapshots are disposable and are fetched
> again into the new folder). The decision is unchanged, and the file name is
> kept so that `ADR 0008` citations in code stay true.

> **Amended 2026-09-16: the cache defaults under the storage folder.** The
> decision below said the cache location "defaults beside the per-user settings
> rather than inside the backlog", on the reasoning that a snapshot has no
> business in a folder people back up or sync. Both halves of that reasoning
> moved: the Storage tab now tells people to keep the backlog off synced disks,
> and local ADR 0010 backs up the database file alone, never the folder. What
> is left is the machine with a small system drive and a large one for work —
> and that machine puts its *backlog* on the large drive, so the cache belongs
> beside it. The default is now `<storage folder>\devbook-cache`, recomputed
> when the root moves; the override is unchanged. The storage folder's own
> devbook — the section rows the Storage tab used to carry — was removed in the
> same change: a devbook belongs to a repository.

> **Amended 2026-09-15: resolution fetches on its own.** The decision said
> *"Resolution never fetches"* and left *"should a snapshot ever refresh on its
> own?"* open. In practice that put every section of a branch-sourced repository
> in an error state — *"has not been fetched from main yet"* — until somebody went
> to the Devbook pane and pressed the update control, and the Settings screen
> showed five errors for a repository nothing was wrong with. The rule is now
> **resolution never *waits* on a fetch**: the first resolve of an unfetched branch
> starts the download in the background and answers "fetching" as news rather
> than as an error; a snapshot on disk is served at once and re-checked against
> the branch head at most once per ten minutes; a failure is the adapter's words,
> remembered for the same interval and forgotten when the repository settings
> change. `DevbookSnapshotAutoFetch` owns the cadence, and every subscriber to
> the folder source's `Changed` event hears a landing the way it already heard a
> pull. The open question below is closed by this; the rest of the decision is
> unchanged.

> **Amended 2026-09-15, the same day: the snapshot is lazy.** The original
> decision took the branch as one archive, extracted whole. That was reversed on
> the owner's call: a snapshot is now the branch's *index* — one listing of every
> path in the commit — plus the files fetched into it on demand, area by area, as
> a panel opens them. The menu is drawn from the index before a chapter exists
> on disk, and the update control refreshes the index rather than the tree. The
> two amendments compose: what the auto-fetch above starts, re-checks and
> rations is the *index*, which is why it can afford to run on every resolve;
> what a reader then needs comes through two preparation calls on the devbook
> folder port, *listing* — which joins the auto-fetch's download rather than
> starting its own — and *content*. "Why a snapshot rather than reading files
> over the API on demand" below is left as written, because its first reason —
> the readers walk a real directory — still holds and shaped how the lazy
> version was built; its other reasons are answered in the new section that
> follows it. The archive client is gone; `IGitHubTreeClient` and the
> `Prepare*Async` calls are what replaced it.

A **local** decision, numbered in the local sequence — not to be confused with
inherited ADR 0008 under `.arc42/adr/guidelines/`, which this repository did not
import. Every reference below to a local ADR by bare number means the local one.

It **narrows** local ADR 0004 on one point — where a derived artifact may live —
and states the limit of the "desktop works fully standalone" concept in
`08-crosscutting-concepts.md`. It changes nothing about what canonical means: the
knowledge folders are still markdown, still hand-edited, still diffed in pull
requests.

## Context

Every knowledge area resolved against a repository's local clone. A repository
with no `CloneDirectory` answered `Unavailable` for all five areas, so registering
a repository in Settings bought nothing until somebody also cloned it and typed
the path in. `InstructionSourceDiscovery` had a second, independent gate saying
the same thing in its own words.

That is the right shape for a repository you work in and the wrong one for a
repository you only read. A workspace commonly registers several: the one being
built, and the ones whose domain model, architecture chapters or design tokens
have to be consulted. Cloning the second kind is a cost with no return — nobody
is going to edit them — and it is a cost paid per machine.

Two things were also true and pulled against changing this:

- **ADR 0004** places the derived knowledge layer *"in the repository that owns
  the knowledge folders, not in the workspace root"*, reasoning that a copy in
  the workspace root *"would be a second place that can disagree about a
  repository the workspace does not control."*
- **`08-crosscutting-concepts.md`** states *"Desktop works fully standalone; the
  cloud connection is purely additive"*, and lists the four kinds of state that
  deliberately stay on the machine.

## Decision

A repository's knowledge is read from **one of two sources**, decided per
repository:

- its **local clone**, exactly as before; or
- a **cached snapshot of a branch** — the repository's default branch unless
  another is chosen.

**Only the clone is editable.** Reading from a branch, the panels do not offer the
status selectors, the chapter editor, or the folder launchers, and the writers
refuse if reached anyway.

The snapshot is the branch's **index** — every path the commit contains, taken
in one call — and, under it, the **files fetched so far**, laid out at the paths
the index gives them inside an app-managed cache folder. A file is fetched the
first time a reader asks for the area or the file it is in, and stays until the
branch moves and changes it. The cache location is a setting, defaulting under
the storage folder since the 2026-09-16 amendment above (beside the per-user
settings before it).

**Resolution never waits on a fetch; preparation does.** As first decided,
resolution never fetched at all and the update control in the Devbook pane was
what went and got it; since the 2026-09-15 amendments above, resolution starts
the index download itself in the background, answers from the index once there
is one, and never blocks a panel load on GitHub. The two preparation calls on
the folder port are what a reader awaits: *listing* waits for the index the
resolve started (and fetches the reading-order files the menu is ordered by),
*content* fetches what a reader names — an area's folder, or a handful of
paths. The Devbook menu prepares the listing; the area stores prepare their
content; the auto-fetch re-checks the head on its cadence and the update
control does so on demand. Nothing waits on a pull.

## Why this, rather than the alternatives

**Why a snapshot rather than reading files over the API on demand.** The panels
already read a folder — `_meta/index.json`, then the one chapter somebody opened —
and a snapshot keeps every one of those readers working unchanged against a real
directory. Fetching per file would mean rewriting all of them onto an async
abstraction, needing the network on every chapter open, and spending rate limit
to do it. A snapshot also reads offline once taken, which is the property that
keeps the standalone concept intact.

**Why the whole tree rather than just the knowledge folders.** Knowledge folders
are configurable and can be pointed anywhere; the instructions area *is* the
repository root; the diagram tooling reads `tools/`. "Extract the folders we
need" is a guess that goes wrong the first time somebody re-points a folder.

**Why the index and the files separately, rather than either alone (amendment).**
The archive was the wrong unit. It carried the whole repository — source, tools,
and the rendered diagram artifacts, which in this one are megabytes per area —
to draw a menu of a few hundred kilobytes of markdown, and it carried nothing at
all until somebody pressed a button, so a repository configured to read a branch
looked empty until they did. The index is the cheapest thing that draws a menu:
one call, small, and it says which folders the commit has without a byte of
their content. The files are then fetched at the granularity the readers
actually read at — the area stores parse a whole folder, so an area is fetched
whole the first time its panel opens; the instructions area is the repository
root, so it names its agent folders and root files rather than fetching the
root; the rendered `_archify/` artifacts beside a chapter are left out of an
area, because nothing reads them until a rendered diagram is shown. The whole
tree is still the *index's* unit, for the reason above: nothing has to guess
which folders matter, because listing them all costs nothing.

The readers were not rewritten onto an async file abstraction, which is what the
first reason for a snapshot was protecting against. They still walk a real
directory. What changed is one call before each walk — `PrepareContentAsync`,
a no-op for a local folder — and one seam in the menu, which enumerates through
an `IDevbookFileTree` so that it can list from the index rather than the disk.
The rate-limit reason is answered by the shape: one listing per commit and one
blob per file somebody opened is far fewer calls than the API-per-read design it
argued against, and files already on disk at the commit's version cost none.
The offline reason is narrowed rather than lost: what has been opened reads
offline, which is what the standalone concept in `08-crosscutting-concepts.md`
now says.

**Why read-only, rather than editing the snapshot and pushing.** An edit to a
snapshot would be an edit to a copy of a commit — it would survive exactly until
the next fetch, and vanish without anyone being told. Committing from the app
instead would mean the app taking on branch, conflict and review concerns that a
clone and an editor already handle better. The honest answer is that this source
is for reading, and the app says so by not offering the controls.

**Why the branch is shared and the source choice is not.** "This repository's
knowledge is read from `main`" is true of the repository on every install of a
workspace, so the branch travels in the registry beside the alias and the hue.
"Read my clone instead" is only answerable on a machine that has the clone, so it
stays in the per-user file. A workspace shared with a colleague who never cloned
anything must not arrive telling their app to read a folder they do not have.

## What this narrows in ADR 0004

ADR 0004's placement rule is kept for what it was about — the *derived* layer,
`_meta/`, the generated index — and does not extend to a snapshot, for a reason
that dissolves its own argument. That rule guards against a second place that can
*disagree* about a repository. A snapshot cannot disagree with the folders beside
it, because there are none: it exists precisely where the repository was never
cloned. It is not a derivation of local content that could drift from it; it is a
verbatim copy of a named commit, and it records which commit, so "is this
current?" is one cheap call rather than a judgement.

It also sits in the workspace root by necessity rather than by preference. There
is no repository folder to sit beside — that is the whole case for the feature.

## What this qualifies in the standalone concept

"Desktop works fully standalone" remains true of everything it was written about:
the backlog, its tasks, its own knowledge, and any repository with a clone. What
is now also true is that **a repository configured to read a branch needs the
network to list the branch, and the first time each area is opened**. After
that it reads offline what it has, and a lost connection costs freshness — and
the areas not yet opened — and nothing else, which is the same trade ADR 0005
records for sync. A refresh that fails leaves the index and every fetched file as
they were; a fetch that fails part-way keeps what landed.

The default protects this. A repository with a clone and no stored preference
reads the clone, so an existing install's behaviour is unchanged and no
previously-offline workspace acquires a network dependency by upgrading.

## Consequences

- A repository can be registered and read without being cloned, which is the
  point.
- Devbook editing now depends on which source is selected, not only on whether
  a folder exists. `DevbookFolderLocation` carries that answer so no caller has
  to re-derive it, and the writers check it as a backstop.
- A fifth kind of machine-local state joins the four in
  `08-crosscutting-concepts.md`: the snapshot cache. Like the derived knowledge
  layer, it is regenerated rather than shipped, and is safe to delete. On disk it
  is, per branch, `snapshot.json` (which commit), `index.json` (every path),
  `fetched.json` (which files are here, at which blob id) and `tree/` (the
  files) — and the invariant every write keeps is that a file under `tree/` is
  the indexed commit's version of that path.
- The devbook folder port grew two calls beside `Resolve`: `PrepareListingAsync`
  and `PrepareContentAsync`, both defaulting to `Resolve` so a local folder — and
  every fake predating branch loading — needs nothing. A reader that opens files
  calls the second before it reads; the menu calls the first. Adding a reader
  means adding that call, and the read-only branch tests are where its absence
  shows up.
- Rendered diagram artifacts (`_archify/`) are not fetched with an area. On a
  branch, a diagram is drawn from its source; the pre-rendered picture is a
  clone-only nicety until somebody decides it is worth fetching on demand.
- The Devbook panels can now be looking at a commit rather than at a working
  tree. The scope label names the branch so this is visible rather than inferred.
- Somebody who edits a clone and expects to see it in a panel reading a branch
  will not, until they push and refetch. The read-only controls are what make
  that legible before it surprises anybody.

## Open questions

- ~~**Should a snapshot ever refresh on its own?**~~ Closed 2026-09-15: it does.
  The first fetch starts on the first resolve and the head is re-checked once per
  interval, in the background, which keeps ADR 0004's "refresh is an optimisation,
  never a precondition" — nothing waits on it — while dropping the manual step.
  What refreshes is the index; a fetched file the new commit changed is dropped
  and comes back the next time its area is opened. A stale *clone* still makes
  the old bargain: it is pulled only when somebody presses the control, because
  a pull touches a working tree somebody may be editing in.
- **Nothing prunes the cache.** A repository removed from Settings leaves its
  snapshots behind, and a branch selected once and abandoned keeps its tree. The
  folder is safe to delete by hand and the setting says where it is; whether the
  app should tidy it, and on what signal, is not decided here.
- **Private repositories need a credential** the transport can resolve. The
  listing and the blobs go through `IGitHubTransport` like every other call, so
  a repository bound to an account this machine cannot satisfy fails the way the
  branch catalog already failed — with the transport's "not configured" reason,
  which is at least a sentence about signing in. The anonymous public-repository
  read the archive download allowed is gone with it; whether it is worth a second,
  credential-less path for public repositories is not decided here.
