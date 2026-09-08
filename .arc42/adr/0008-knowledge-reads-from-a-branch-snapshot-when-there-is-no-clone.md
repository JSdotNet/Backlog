# ADR 0008: Knowledge reads from a cached branch snapshot when there is no clone; only a clone is editable

```meta
status: proposed
related: [".arc42/08-crosscutting-concepts.md#storage-and-sync", ".arc42/08-crosscutting-concepts.md#knowledge-index", ".arc42/adr/0004-knowledge-index-is-a-generated-local-database.md", ".arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md", ".arc42/adr/guidelines/0015-resilience-for-outbound-dependencies.md", ".domain/second-brain/features.md#repository-knowledge-areas"]
issue: null
```

## Status

Proposed.

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

The snapshot is the repository tree as that branch has it, downloaded as an
archive and extracted into an app-managed cache folder. The cache location is a
setting, defaulting beside the per-user settings rather than inside the backlog.

**Resolution never fetches.** A branch nobody has fetched resolves to "not fetched
yet"; the existing update control in the knowledge pane is what goes and gets it.

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
network once**, to take the first snapshot. After that it reads offline, and a
lost connection costs freshness and nothing else — which is the same trade ADR
0005 records for sync.

The default protects this. A repository with a clone and no stored preference
reads the clone, so an existing install's behaviour is unchanged and no
previously-offline workspace acquires a network dependency by upgrading.

## Consequences

- A repository can be registered and read without being cloned, which is the
  point.
- Knowledge editing now depends on which source is selected, not only on whether
  a folder exists. `KnowledgeFolderLocation` carries that answer so no caller has
  to re-derive it, and the writers check it as a backstop.
- A fifth kind of machine-local state joins the four in
  `08-crosscutting-concepts.md`: the snapshot cache. Like the derived knowledge
  layer, it is regenerated rather than shipped, and is safe to delete.
- The knowledge panels can now be looking at a commit rather than at a working
  tree. The scope label names the branch so this is visible rather than inferred.
- Somebody who edits a clone and expects to see it in a panel reading a branch
  will not, until they push and refetch. The read-only controls are what make
  that legible before it surprises anybody.

## Open questions

- **Should a snapshot ever refresh on its own?** It does not today, following
  ADR 0004's "refresh is an optimisation, never a precondition". A stale snapshot
  is silent until somebody presses the control, which is the same bargain a stale
  clone already makes.
- **Nothing prunes the cache.** A repository removed from Settings leaves its
  snapshots behind, and a branch selected once and abandoned keeps its tree. The
  folder is safe to delete by hand and the setting says where it is; whether the
  app should tidy it, and on what signal, is not decided here.
- **Private repositories need a credential** the archive download can resolve.
  Anonymous download works for a public repository, which is deliberate, but the
  failure for a private one is a 404 from GitHub rather than a message saying
  "sign in" — the credential design cannot distinguish the two.
