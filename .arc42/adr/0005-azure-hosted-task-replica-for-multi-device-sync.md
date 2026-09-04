# ADR 0005: An Azure-hosted task replica carries multi-device sync; the local store stays canonical

```meta
status: proposed
related: [".arc42/02-constraints.md#technical-constraints", ".arc42/07-deployment-view.md#cloud-deployment-azure", ".arc42/08-crosscutting-concepts.md#storage-and-sync", ".arc42/09-architecture-decisions.md", ".arc42/11-risks-and-technical-debt.md", ".arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".arc42/adr/0006-additive-schema-bootstrapping-is-the-local-migration-mechanism.md", ".arc42/adr/guidelines/0012-authentication-external-identity-providers.md", ".arc42/adr/guidelines/0013-authorization-zero-trust.md", ".arc42/adr/guidelines/0014-persistence-and-repository-boundaries.md", ".domain/tasks/domain.md#task", ".domain/tasks/naming.md#device"]
issue: null
```

## Status

Proposed. Nothing is built yet; this records the direction and the questions it
deliberately leaves open.

A **local** decision, numbered in the local sequence — not to be confused with
inherited ADR 0005 (modular monolith structure) under `.arc42/adr/guidelines/`.
Every reference below to ADR 0001–0005 without a qualifier means the local one.

It **amends** local ADR 0003 rather than superseding it. ADR 0003's decision —
that the canonical task store is one local SQLite database, and that a task's
content is markdown text inside it — survives this record intact. What changes is
the answer to a question ADR 0003 did not ask: what happens when the same person
runs the desktop on two machines.

Scope is **the Task aggregate only**. The roadmap plan, workspace settings,
feature flags, and the knowledge layer stay local and unsynced in this pass.

## Context

**The failure this decision exists to prevent has already happened.** The
workspace root is configurable, and pointing it at a cloud-synced folder is
behaviour the Storage settings screen actively invites — its copy still describes
the pre-ADR-0003 world in which every entry was its own markdown file:

> Every entry is a markdown file with YAML frontmatter, readable and editable
> without this app. Point this at a synced or version-controlled folder and the
> backlog travels with it.

Since ADR 0003 that is one binary file, and OneDrive cannot merge one. Two
machines writing `backlog.db` through a file-sync product produced six conflicted
copies — `backlog-JS-DESKTOP.db` through `backlog-JS-DESKTOP-6.db` — and status
edits made on one machine were silently reverted on the other. WAL mode makes it
worse: the `-wal` and `-shm` sidecars sync independently of the database, so a
sidecar arriving without its matching file rolls back transactions that were
already committed. ADR 0003 predicted this in its Negative consequences and
accepted the trade; the accepted trade turned out to be the primary workflow.

**File sync is the wrong mechanism, not the wrong idea.** The requirement
underneath is multi-device use, which the architecture has always intended to
serve through the cloud tier rather than through the file system.
`.arc42/08-crosscutting-concepts.md#storage-and-sync` already commits to it, down
to the conflict policy — *"Optional cloud sync for multi-device. Conflict
resolution: new items always create; edits are last-write-wins"* — and
`.arc42/07-deployment-view.md#cloud-deployment-azure` already names Cosmos DB as
the cloud data store. None of it was ever built for tasks. The only sync code
that exists, `Backlog.Modules.Sync.Api.SyncStore`, is an in-memory TTL dictionary
carrying mobile inbox captures.

**Three standing constraints bound the answer**, and a naive reading of "host the
task database on Azure" breaks all three. `.arc42/02-constraints.md` requires
local-first canonical storage, requires every core workflow to run offline with
the cloud additive only, and confines the cloud to a thin sync layer with *"no
inbox fetching, domain CRUD, or full-text search"*. Inherited ADR 0014 puts the
same boundary in one sentence: *"The cloud tier persists only sync-oriented
state, never canonical domain data."*

Moving the canonical task store into Azure would overturn all four positions,
make the product useless on a plane, and buy nothing the replica model does not
already buy for the problem at hand.

**One prerequisite is missing outright.** The `tasks` table carries `created_at`
and no modification timestamp. Last-write-wins is not expressible without one, so
no sync design of any shape can proceed until the aggregate can say when it last
changed.

## Decision

**Azure hosts a replica of the person's tasks and the change feed over it. Each
device's local SQLite database remains canonical for that device.** Sync is
reconciliation between equals, not a client talking to a system of record.

### Storage

**Azure Cosmos DB for NoSQL, serverless**, one container `tasks`, partition key
`/ownerId`.

Cosmos rather than Azure SQL or PostgreSQL Flexible Server for three reasons that
each hold on their own:

- **Serverless bills per request with no floor.** A single user's sync traffic
  costs cents per month. The cheapest always-on relational tier is a fixed
  monthly charge for a database that is idle almost all of the time.
- **The aggregate is already a document.** The SQLite adapter writes the whole
  task in one upsert, and `sub_items`, `projections`, `usage_events`, `tags`, and
  `repo_ids` are already JSON columns. A document store stores what the
  application already has, and adds no second relational schema to version.
- **The change feed is the sync protocol.** A device asks "what changed since my
  continuation token" and Cosmos answers it natively, ordered and durable. Built
  on a relational store, that is a polling query plus a watermark table plus the
  correctness argument that goes with them.

The container holds task documents and nothing else. No invariant is enforced
there, no query serves the UI from it, and no domain logic runs against it — it
is replication substrate. That is what keeps inherited ADR 0014 satisfied: the
cloud copy is a replica, and the canonical record stays on the device.

### Compute

**Azure Container Apps, consumption plan, scale-to-zero**, hosting the existing
`Backlog.Modules.Sync.Api`. The deployment view already offered "App Service or
Container Apps"; this record settles it on Container Apps because scale-to-zero
means a personal tool costs nothing while nobody is syncing, and because the
Aspire AppHost already models `sync` as a container resource, so local topology
and deployed topology stay the same shape.

The service exposes two operations over the task container, and no more:

| Operation | Meaning |
|---|---|
| `POST /sync/tasks` | Push the caller's documents changed since its last push watermark. |
| `GET /sync/tasks?since={token}` | Pull the change feed from a continuation token. |

### The sync model

- Every task carries **`updated_at`** — UTC, stamped by the device on every
  mutation — and **`deleted_at`**, a tombstone. Deletion has to replicate, and a
  row that is simply gone cannot.
- **New items always create; edits are last-write-wins on the whole document.**
  This is the policy `.arc42/08-crosscutting-concepts.md` already states, applied
  unchanged. Whole-document rather than per-field because the aggregate is
  already written whole everywhere else, and a per-field merge would invent a
  reconciliation the domain has no rule for.
- **Ordering authority is the server, not the device clock.** Two machines'
  clocks disagree, and last-write-wins decided by a skewed clock loses real
  edits. The Cosmos `_ts` assigned on write orders the change feed; the device's
  `updated_at` is carried for display and used only to break ties, with the
  device id as the final deterministic tiebreak so two devices never flap.
- **Offline is unchanged.** The device reads and writes its local database and
  never blocks on the network. Sync is a background reconciliation; losing
  connectivity costs cross-device freshness and nothing else.

### Manual rank

**A task's manual rank is synced aggregate state, and this record now says so.**
`.domain/tasks/domain.md` names `order` and `area` together as the attributes that
"place it in the person's own working set rather than in any external system",
both "freely re-settable" and carrying "no lifecycle invariant of their own". It
withholds from `order` the disclaimer it gives `view`, the one attribute it does
call "a display preference rather than domain state". `area` travels; nothing in
the domain separates rank from it. So `TaskItem.SetOrder` restamping `updated_at`
is correct and stays — a rank set by hand is an edit, and an edit that did not
travel would be the sequence the person arranged quietly reverting on their other
machine.

**But the gesture that sets rank is the gesture that floods the feed.** The
desktop writes every row's list position back as its rank after a drag and after
a delete — `NormalizeOrderAsync` at
`src/Modules/Tasks/Backlog.Modules.Tasks.UI/TasksDesktopState.cs:2261`, called
from the two delete paths at `:994` and `:1001` and from `MoveEntryAsync` at
`:1041`. Every row it renumbers is a genuine mutation, so every one restamps and
every one becomes due to push. Ordinary work does not do this: both new-row paths
append (`:736`, `:810`) and touch a single row, and an ordinary flush save
normalizes nothing. It is drag and delete specifically, and the worst case is the
first such gesture on a backlog nobody has ranked. Those rows all share the
default `0`, so the handler's skip test `order == index`
(`src/Modules/Tasks/Backlog.Modules.Tasks/Features/ReorderTasks/ReorderTasksCommand.cs:25`)
matches only index 0 and a single deletion rewrites every other row in the
backlog. Under whole-document last-write-wins each of those rewrites overwrites
the other machine's copy of that task — including a real, unrelated edit made
there. One gesture, O(backlog) push volume and O(backlog) blast radius.

**The second cost is this record's own cost argument.** *"Serverless bills per
request with no floor"* is the first reason given for Cosmos above, and a write
amplification that scales with the size of the backlog on every drag and every
delete is levied straight against it.

**Two corrections follow, and their order matters.** The first is the trigger.
Renumbering the rows below a deletion is pure waste: removing a row does not
change the relative order of any row that survives it, so rewriting their ranks
reproduces the identical sequence at the cost of a write and a stamp apiece.
Those calls go. That costs no schema change, no new semantics and no new
concurrency question, and it stands on its own — it is worth doing whether or not
anything here is ever built.

**The second is the encoding, and only a drag needs it.** Dense ranks are
positions, so moving a row past *k* others necessarily moves *k* of them; no
amount of care makes a drag touch one row while rank means "index in the list".
The rank key therefore becomes sparse — a nullable column **added alongside**
`sort_order` and seeded where it is null, leaving the existing column as it
stands. That is two of the three shapes local ADR 0006 permits, *"Add a column"*
and *"Seed a new column for existing rows"*, and it is the same pattern
`updated_at` itself uses here.

**Re-keying `sort_order` in place is forbidden, and not as a matter of taste.**
ADR 0006 rules out *"any `UPDATE` that overwrites a value the previous version
wrote deliberately"*, which a person's hand-made ordering is precisely. Nor could
such a statement be written safely: multiplying every rank to open gaps is not
idempotent — on the next open it multiplies again — where that record requires
every statement be *"idempotent by construction rather than by bookkeeping: true
after it runs, and matching nothing the next time"*. Done that way the change
would trip ADR 0006's own boundary and supersede it, buying the versioned
migration mechanism it exists to defer.

Three alternatives were weighed and rejected:

- **Accept the cascade and document it.** Free, and wrong. It reproduces R9's
  failure through a new mechanism — a real edit on one machine silently reverted
  on the other, with nothing shown to the person — and it contradicts the premise
  this record argues Cosmos on.
- **Make rank device-local and unsynced.** Structurally worse than it sounds.
  Excluding one field from a whole-document push obliges the pull side to merge
  that field out of every inbound document, which *is* per-field merge — the
  machinery rejected above, reintroduced for one field and without saying so. The
  domain objects independently: it pairs `order` with `area`, and it reasons about
  per-device storage explicitly when it explains why even `view` lives on the
  task, because *"a preference kept in a sidecar or in a per-device setting would
  not survive the file being shared"*. A rank is the stronger case, not the
  weaker one.
- **Per-field merge for rank alone.** Out of scope by the position already taken
  here. In fairness the general argument is weaker for rank than elsewhere: two
  statuses have no reconciliation the domain can name, but two sparse rank keys
  are independently comparable and do have a defensible one. That is a reason to
  revisit per-field merge deliberately, not a reason to make rank the exception
  that arrives without the decision.

**None of this is built.** The constraint binds the first implementation of the
sync model rather than describing what the desktop does today. The trigger fix
needs nothing from this record, and should not wait for it.

### Identity

**Device pairing, not an account.** `.arc42/02-constraints.md` requires that
personal use need no login, and inherited ADR 0012 confirms device-session JWTs
rather than a user identity.

A first device generates an `ownerId` and a pairing secret. A second device is
paired by entering a short code, out of band, once. Each device then holds its
own long-lived registration credential in the OS credential store — DPAPI on
Windows — and exchanges it for a short-lived JWT (15–60 minutes, per inherited
ADR 0012) on each sync. The `ownerId` is the Cosmos partition key, so a device
can only ever read the partition its token names.

Service-to-Azure access uses **managed identity** with a Cosmos data-plane role
assignment. No connection strings, no account keys, nothing in configuration to
leak — the posture inherited ADR 0013 asks for.

### The database filename

**The local database stays `backlog.db` on every device.** A per-device filename
is rejected as a pre-sync mitigation, and left available to the sync slice, where
the device identity above already exists to supply the name.

The question is whether `SqliteTaskRepository.DatabaseFileName` — today a constant
`"backlog.db"`, combined with whatever root the workspace points at — should
become something like `backlog-JS-DESKTOP.db`, so that two machines sharing a
synced root never contend for one file.

It would work as far as it goes. Two devices would write two files; the `-wal` and
`-shm` sidecars derive from the database name and would separate with it, so the
worst hazard — a sidecar arriving without its matching file and rolling back
committed transactions — goes with them; and OneDrive would have nothing to
conflict. **It would also convert a loud failure into a silent one.** Each device
reads only its own file, so the two backlogs diverge and nothing says so. The loss
recorded as R9 was noticed at all because edits visibly reverted; two quietly
divergent backlogs would be discovered weeks later, and file sync would go on
replicating both files to both machines for no benefit. That is not a smaller
failure than the one it prevents. It is a less visible one.

Three further things make it the wrong change to make first:

- **It would disable the only cross-device freshness the product has.**
  `TasksDesktopState` polls the newest timestamp across `backlog.db` and its two
  sidecars for exactly one reason, which its own summary states: two machines can
  share one `backlog.db` through a synced folder, and the second has no way to be
  told about the first one's writes. A per-device name leaves that watcher
  watching a file no other machine ever writes. Until the sync service ships, the
  shared file is what makes the second machine see anything at all.
- **It needs a device identity, and the product should have exactly one.** The
  pairing registration credential above is it. `Environment.MachineName` exists
  today and is the obvious shortcut, but a machine can be renamed and two machines
  can share a name, so a file named from it is neither stable nor unique — and
  adopting it would leave two device identities free to disagree. Naming the file
  from the pairing identity means the rename cannot precede pairing.
- **Renaming an existing database is not additive.** Local ADR 0006 permits three
  shapes and this is none of them; it is precisely the non-additive change that
  supersedes 0006 and forces a versioned migration mechanism to be built. Spending
  that on a file name, against a hazard the sync service removes anyway, inverts
  the order of the work.

**What the residual hazard needs is detection, not a rename.** R9's mitigation now
rests on the user keeping the workspace root off a synced disk, and nothing in the
app checks: a root already on OneDrive stays there. A startup check that
recognises a known sync provider's folder — OneDrive, Dropbox, Google Drive,
iCloud — and says so on the Storage screen closes that gap without touching the
schema. It also covers the whole root rather than one file in it, which matters
here: `_roadmap/plan.json` sits under the same root with the same hazard, and one
root-level check answers for it too, where a per-device filename would have to be
invented separately for every file the root holds.

Once the sync service ships, the workspace root goes back to being a local folder
and the hazard is structural rather than advisory. Per-device filenames stay
available then as belt and braces, on terms this record states in advance: named
from the pairing identity, and adopted for **new roots only**, leaving an existing
`backlog.db` where it is so the change stays additive and ADR 0006 stands. That is
a judgement for the slice that builds sync, not a prerequisite for it.

### Deployment

**Bicep under `infra/sync/`, provisioned and deployed with `azd`**, alongside the
existing `infra/foundry/`. One environment (`prod`) is enough for a personal
tool. GitHub Actions deploys on push to `main` using **OIDC federated
credentials**, so no publish profile or service principal secret is stored in the
repository.

Provisioned resources: Container Apps environment and app, Cosmos DB serverless
account with the `tasks` container, Key Vault, Log Analytics, and Application
Insights. OpenTelemetry already flows through `AddServiceDefaults()`, so
observability needs wiring, not designing.

Local development runs against the **Cosmos DB emulator** as an Aspire resource,
so no cloud account is required to build or test the sync path.

Indicative cost at single-user volume: Cosmos serverless a few cents per month,
Container Apps zero while scaled to zero, Key Vault and Log Analytics within
free grants. Well under €5/month.

## Consequences

Positive:

- The reported data loss becomes structurally impossible. Two devices reconcile
  through a store built for concurrent writers instead of racing to overwrite one
  file.
- Local-first survives fully. Every core workflow still runs offline, and the
  cloud stays additive exactly as `.arc42/02-constraints.md` requires.
- The workspace root goes back to being a local folder. The Storage screen's
  advice to point it at a synced folder becomes both correct and harmless again
  once it is rewritten, because the database no longer travels by file sync.
- The cloud tier finally does the job it was documented to do. The Azure
  deployment view, the sync module, and the conflict policy stop being aspirational.
- The mobile and IDE channels get multi-device task access as a by-product; they
  already speak to this service.
- Manual ordering survives multi-device use. The sequence a person arranged by
  hand is one of the things they would notice losing, and it travels as ordinary
  aggregate state rather than being dropped at the device boundary.

Negative:

- **A modification timestamp is a schema change to a store with live user data**,
  and this repository has no migration mechanism — the gap inherited ADR 0014
  already records. This decision forces that story to be written, which is the
  first real cost of it.
- **Last-write-wins loses edits by design.** Two devices editing the same task
  while both offline will keep one version and discard the other. This is the
  policy the architecture already chose, and it is accepted here rather than
  re-litigated, but it is a real loss and the user is not told when it happens.
- **A replica of personal task content now lives in Azure.** The threat model
  changes: content that was purely local is now in a cloud account, and Key
  Vault, managed identity, and partition-scoped tokens are what stand in front of
  it.
- **A second store to keep consistent.** ADR 0003's own argument against
  many-files was that no two of them can be allowed to disagree; a replica is by
  definition a second copy that can. The change feed and the tombstones exist to
  bound that, not to eliminate it.
- **The sparse rank key is a second schema change riding the same mechanism.**
  ADR 0006 notes that its bootstrap *"runs on every open, so its cost grows with
  the number of statements"*; this adds a column probe and a seeding `UPDATE` to
  that list, and moves the mechanism nearer the boundary at which it stops being
  enough.
- **Two callers derive rank from a list index and have to stop.** The importer
  starts new entries at `existing.Count`
  (`src/Modules/Tasks/Backlog.Modules.Tasks/Features/ImportPlan/ImportPlanCommand.cs:101`)
  and the desktop saves a new entry with its row index as its rank
  (`TasksDesktopState.cs:2195`). Both assume rank is a position, and neither
  survives a sparse key unchanged.
- Cosmos serverless has a 20 GB logical partition ceiling. Irrelevant at personal
  scale, and a hard wall if the product ever stops being single-user.
- **The file-sync hazard survives until this decision is built.** The filename
  decision above deliberately declines the one change that would blunt it early,
  on the grounds that it trades a visible failure for an invisible one. What
  stands in its place until then is a warning the user can ignore, plus detection
  that is not written yet.

Neutral:

- The Task aggregate gains `updated_at` and `deleted_at`. Both are
  infrastructure-shaped, and neither carries a lifecycle invariant, but they are
  domain-visible enough to belong in `.domain/tasks/domain.md`.
- Soft delete changes what "delete" means locally: the row persists as a
  tombstone until it is reaped. A retention policy for tombstones is left open
  below.
- `0` stops meaning "never ranked". The current read leans on it — unranked rows
  share the default, sort ahead of ranked ones and fall back to recency
  (`SqliteTaskRepository.cs:179`) — so the sparse key needs its own answer for a
  row nobody has ordered.
- Nothing here makes Azure canonical for anything, and nothing here changes what
  canonical means for a task. ADR 0003 stands.
- The database file keeps the name `backlog.db` on every device, so nothing that
  reads a workspace root by that name changes: `WorkspaceSettingsStore.DatabasePath`,
  the external-change poller and its sidecar watch, and the tests that pin the
  path all stand as written.

## Open questions

- **Tombstone retention.** The existing cloud TTLs (sync payloads 7 days, webhook
  events 24h) assume transient state; a replica is not transient. How long a
  tombstone must survive is bounded by how long a device may stay offline, and
  that number is not chosen yet.
- **Attachments.** `.arc42/07-deployment-view.md` states attachments live on the
  desktop file system and there is no blob storage. A task that syncs but whose
  attachment does not is a partial replica, and this pass does not resolve it.
- **Roadmap plan.** `_roadmap/plan.json` is a single JSON file under the same
  workspace root and has the same file-sync hazard, unexercised so far. Whether it
  syncs, and how, is out of scope here and will need the same treatment. Its share
  of the *hazard* is already answered: the detection described under **The database
  filename** is a check on the root, so it covers the plan file as it covers the
  database.
- **Whether the sync slice adopts per-device filenames after all.** This record
  declines them now and states the terms on which they could land later — named
  from the pairing identity, new roots only. Whether that belt and braces earns its
  cost once the hazard is already structural is a judgement for the slice that
  builds sync.
- **Whether `sort_order` is frozen or dual-written** while the sparse key is
  introduced. Freezing it is simpler and leaves an older build reading a stale
  order; dual-writing keeps that build correct and reintroduces on every drag the
  cascade the sparse key was added to remove.
- **What an unranked row sorts as** under the sparse key, now that `0` no longer
  carries that meaning.
- **The reorder path has no tests at all.** Neither `ReorderTasksCommandHandler`
  nor `NormalizeOrderAsync` is covered anywhere, so the skip-if-unchanged test
  that bounds every number above is unverified. Not a precondition for this
  record, which chooses a direction; a precondition for the trigger fix, because
  that change is judged entirely by which rows it stops writing.
- **Recovering the six conflicted copies** is a data-recovery task, not an
  architecture one, and is tracked separately.
