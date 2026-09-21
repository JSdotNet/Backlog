# ADR 0011: Devbook annotations are the person's data, in a Backlog-owned store, replicated through a third container

```meta
status: active
date: 2026-09-16
related: [".arc42/08-crosscutting-concepts.md#storage-and-sync", ".arc42/08-crosscutting-concepts.md#task-sync", ".arc42/08-crosscutting-concepts.md#devbook-database", ".arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".arc42/adr/0004-knowledge-index-is-a-generated-local-database.md", ".arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md", ".arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md", ".domain/devbook/features.md"]
issue: null
```

## Status

Accepted, and built: the store, the port, the third replica container, its
endpoints and the desktop exchange landed together on 2026-09-16.

> **Amended, 2026-09-18 — a push may never move a document backwards.** The
> replica this record copied from local ADR 0005 kept whichever copy of an
> annotation arrived last, and that let a device's *echo* win — the defect the
> task replica was amended for on the same day, and the same defect here,
> because the annotation exchange is the task exchange's copy. Every device
> pushes everything above its own watermark, and a document it received on a
> pull sits above that watermark exactly as an edit does — so each device
> re-sends what it pulled, once, on its next push. When the other desktop had
> resolved, deleted or reworded the remark in between, the echo replaced the
> tombstone or the edit at the replica, and the first desktop then took the
> older copy back over its own pushed one, because the merge treats the replica
> as authoritative for anything a device has already sent. A deleted remark
> came back on both desktops; an edit reverted to the version the other had
> pulled. Both annotation adapters now refuse a pushed copy that is not a later
> version than the one held — later by `updated_at`, a tombstone beating the
> live copy it replaced on a tie, an identical pair changing nothing
> (`AnnotationChangePrecedence` in the Sync module, a copy of the task rule
> rather than a shared one, on this record's terms; the Cosmos adapter reads,
> compares, and writes against the etag, re-reading on a race rather than
> letting timing decide). The push response's `accepted` count is honest about
> it, and the client already treats a short count as nothing to act on. What
> this costs is the one race arrival order was chosen for: two desktops editing
> one remark in the same interval now resolve to the later-*stamped* edit rather
> than the later-*uploaded* one, so a skewed clock can pick the winner. Both
> orderings lose one edit in that race; only arrival order also lost every
> deletion. `_ts` still orders the feed and the merge's tiebreaks are unchanged.
>
> **Amended, 2026-09-21 — nor may a pull.** The merge no longer takes an older
> copy over a local remark at or below the push watermark; it applies the
> replica's own rule on the pull side and the watermark no longer enters it.
> The same change, for the same reason, as ADR 0005's amendment of the same
> date, which carries the account.

A **local** decision, numbered in the local sequence. It **extends** local ADR
0005 — a third replica container beside `tasks` and `sessions`, on that
record's terms — and **leaves local ADR 0004 intact**: the generated
`_meta/devbook.db` stays a build output the app never writes, and this record
is partly about why a remark cannot live there.

## Context

**A remark had no home.** The arc42 and domain panels let a reader leave a
remark against a block of a chapter — a `MarkdownComment` in the read view's
margin — and kept those remarks in a dictionary on the panel component,
deliberately, because choosing a home was "a decision about a governed
knowledge folder rather than a detail of this panel". The cost was that the
Router remounts the pane on every route change: Settings and back, and every
remark was gone. Before anything else — an MCP tool that serves remarks, a
review flow that reads them — they had to exist in a store behind a port, and
that store had to outlive the panel and the process, and reach the person's
other desktops.

**Three homes were on the table, and two are wrong for reasons already
recorded.**

- *`_meta/devbook.db` in the repository.* Local ADR 0004 makes it a generated,
  git-ignored build output that the Node generator recreates from scratch and
  the app reads and never writes; `08-crosscutting-concepts.md#storage-and-sync`
  lists the derived knowledge layer among the state that deliberately stays on
  the machine. A remark kept there is lost on the next generator run and can
  never leave the machine — the opposite of both requirements.
- *A table in `backlog.db`.* Local ADR 0003's file already has three owners
  (Tasks, Roadmap, Inbox) and local ADR 0006 warns that every added shape moves
  the additive-bootstrapping mechanism towards its limit. A remark on a
  repository's chapter is Devbook's, not Tasks'.
- *The devbook `annotation` fence.* The devbook convention has its own note —
  a fenced block written into the chapter beside the passage it is about,
  shared through git, read by review mode and skipped by everything else. That
  is a repository artefact with a schema and a lifecycle the plugin governs. It
  is not what the panel's remark is: a private reading note, which must also
  work on a chapter read from a branch snapshot nobody can write to (local ADR
  0008).

**Cross-device is the ask.** "Store the annotations, including sync" meant the
person's other desktops, through the sync service — not a file a sync product
carries, which local ADR 0005 exists to rule out.

**The wire had a shape question.** Local ADR 0009 rode captures in the `tasks`
container because a capture *is* task-shaped and the desktop merge only needs
one kind token to route it. An annotation is not task-shaped: the fixed
25-field `TaskPayload` has no honest place for a repository alias, a chapter
path and a block index, and unknown JSON members are dropped by both the model
binder and the Cosmos serializer. `08-crosscutting-concepts.md#storage-and-sync`
already says why `sessions` is a separate container — its own change feed,
indexing policy and retention, at no per-container cost under serverless — and
the same reasoning applies.

## Decision

**A remark on a Devbook chapter is the person's data, kept by Backlog in a
store of Devbook's own, and replicated between the person's desktops through a
third replica container, `annotations`, on the task container's terms.**

### The record and the port

`DevbookAnnotation` (in `Backlog.Modules.Devbook.Abstractions`) names a remark
by **repository alias and repository-relative chapter path**, never by a folder
on disk, so the same remark names the same chapter on every device — the alias
is the cross-device name a repository already has (local ADR 0005 §Session
records). It is anchored by **block index**, for the reason `MarkdownComment`
gives, and re-anchoring after the chapter changes is not solved: an index that
has gone out of range shows at the end of the chapter rather than vanishing.
It carries `UpdatedAt` and `DeletedAt`, which is what lets it replicate on a
task's terms: whole-document last-write-wins, and a deletion that is a
tombstone.

`IDevbookAnnotationStore` is the port the panels see. Its four panel verbs
(`Add`, `Edit`, `SetResolved`, `Delete`) stamp `UpdatedAt` from the store's
clock; `Apply` writes a replicated document stamps and all; `ListChangedSince`
is what a push selects. A remark with an empty body is a **draft** — kept so
closing the pane does not lose it, opened straight into its textarea when the
chapter is shown again, never pushed, and removed outright rather than
tombstoned when cancelled. The panels resolve the port optionally and fall back
to a session-scoped store when a host composed none, so the storybook and every
existing panel test render unchanged.

### The store

`DevbookAnnotationStore` (in `Backlog.Infrastructure.FileSystem`) keeps **one
JSON file per repository** in `devbook-annotations/` under the storage folder —
beside `backlog.db`, following the workspace root the way the inbox repository
does, and listed in `WorkspaceSettingsStore.OwnedRootFolders` so a move takes
the remarks along. Under the storage folder rather than beside the per-user
settings because a remark is the person's data on the same terms as their
backlog, not this machine's bookkeeping about it. The file is meant to be read:
camelCase, indented, named after the alias with a digest beside it. Tombstones
stay in it — the store cannot know when every device has seen one, and the
file is small.

The author is the writing machine's name, shown as "You" on the machine that
wrote it and as that machine's name elsewhere.

### The wire

`AnnotationPayload`, `AnnotationChange`, `AnnotationChangeRecord` and the
push/pull request and response records are the task contracts over the
annotation shape; `POST` and `GET /api/sync/annotations` are the task routes
over the third container; `IAnnotationReplica` has exactly the two operations
`ITaskReplica` relays with and nothing beside them — no `Find`, no listing, no
query a screen could be answered from. The endpoint validates at the edge the
way the session endpoint does (a remark with no alias or no chapter path could
not be placed on any device; bounded fields keep a caller out of durable
per-request-billed storage) and refuses the whole batch, so a device's
watermark never moves past a remark nobody stored.

The `annotations` Cosmos container is partitioned on `/ownerId`, default
indexing, `defaultTtl` -1 with the 180-day retention stamped per tombstone from
`Sync__Cosmos__AnnotationTombstoneTtlSeconds` — the task container's
arrangement, because an annotation is edited and deleted like a task. It is
declared in the AppHost and in `infra/sync/main.bicep` beside the other two,
and the in-memory replica the endpoint tests and a bare `dotnet run` get is a
copy of the task one.

### The desktop exchange

`AnnotationSyncClient`, `AnnotationReplicaMerge`, `AnnotationSyncSession` and
`AnnotationSyncWorker` (in `Backlog.Infrastructure.Sync/Annotations`) are the
task pipeline's client, merge, exchange and loop over the third feed, with
their own progress file (`annotation-sync-state.json`, per-user, never the
workspace root). The merge's rules are the task merge's exactly — replica
stamp, then device stamp, then device id within a page; against the local copy,
a later version wins and nothing else does — because two devices have to agree
on them. **Amended 2026-09-18 and 2026-09-21:** the replica accepts a pushed
copy only when it is a later version than the one it holds, by `updated_at` and
then by tombstone, and the merge takes a pulled copy on the same terms; the
rule "an older copy may overwrite one this device has already sent" is gone.
See the amendment notes under **Status**. The worker is a third sibling of the other two loops rather than a
third exchange inside one, for the whole of the reasoning `SessionSyncWorker`
records, staggered eleven seconds behind the app's start so three exchanges do
not fire into the same moment. It answers to the one `sync` feature switch and
to this device being paired.

`AddAnnotationSyncStore` and `AddAnnotationSyncClient` are opt-in, on the terms
`AddSessionSyncClient` sets: a head that composes no annotation store composes
unchanged, and the mobile heads compose nothing of this.

### What does not change

The mobile and IDE heads: neither reads the annotation feed, and nothing they
do read changed. The `tasks` and `sessions` containers, their contracts and
their endpoints. Local ADR 0004: the generator is still the only writer of
`_meta/devbook.db`. The devbook `annotation` fence: a repository artefact with
its own lifecycle, which this store does not write and nothing yet promotes a
remark into.

## Consequences

Positive:

- **A remark outlives the panel, the process and the machine.** The failure
  this exists to remove is gone, and the store sits behind a port an MCP tool
  or a review flow can serve from.
- **No shape abuse.** Annotations have their own contracts and container
  rather than riding in `TaskPayload` fields that mean something else.
- **The two note conventions stay distinct.** A private reading note and the
  repository's shared review fence are different things with different owners,
  and neither pretends to be the other.
- **Same rules, same guards.** The exchange copies the task pipeline's
  watermark, cursor and merge rules, and every host guard
  (`*SyncClientRegistrationTests`, the provider validation) has an annotation
  counterpart.

Negative, and accepted:

- **Three copies of the loop.** `TaskSyncWorker`, `SessionSyncWorker` and
  `AnnotationSyncWorker` share a mechanism by copy. A third copy is where an
  extraction normally pays; it is deferred because the two existing loops were
  out of scope to refactor and the copy keeps their "fail separately" property
  by construction.
- **No re-anchoring.** A chapter edited above a remark moves the block the
  remark points at; the read view shows an orphan at the end rather than
  dropping it. The devbook fence solves this by position and `quote`; this
  store does not, yet.
- **No settings surface for the third loop.** The Devices tab shows the task
  exchange's summary and offers its two resets; the annotation loop has neither
  a button nor a reset. Whoever adds them must copy the pending-flag mechanism
  `TaskSyncWorker` uses, not only the method.
- **Not in the backup.** Local ADR 0010 backs up the database and only the
  database; the remark files under the storage folder are carried by a move
  and by replication, not by the backup.
- **Emulator-only TTL blindness**, as for every tombstone here: expiry is
  deployed-only behaviour no test observes.

## Alternatives considered

- **`_meta/devbook.db`** — rejected: regenerated, ignored, never written by the
  app (local ADR 0004), and cannot sync.
- **A table in `backlog.db`, synced as a task document kind** — rejected: a
  fourth owner of the task file, and a task-shaped wire for a thing that is not
  task-shaped.
- **The devbook `annotation` fence** — rejected as the home for this remark: it
  is a repository review artefact, writes into governed folders, and cannot
  exist on a branch snapshot. Promoting a remark into one is a possible later
  flow, not a substitute.
- **Riding in the `tasks` container with `type: "annotation"`** (the ADR 0009
  pattern) — rejected: no honest place in `TaskPayload` for the annotation's
  fields, and the container's feed would carry two kinds with different
  retention needs.
