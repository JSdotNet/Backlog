# ADR 0005: An Azure-hosted task replica carries multi-device sync; the local store stays canonical

```meta
status: active
related: [".arc42/02-constraints.md#technical-constraints", ".arc42/07-deployment-view.md#cloud-deployment-azure", ".arc42/08-crosscutting-concepts.md#storage-and-sync", ".arc42/09-architecture-decisions.md", ".arc42/11-risks-and-technical-debt.md", ".arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".arc42/adr/0006-additive-schema-bootstrapping-is-the-local-migration-mechanism.md", ".arc42/adr/guidelines/0012-authentication-external-identity-providers.md", ".arc42/adr/guidelines/0013-authorization-zero-trust.md", ".arc42/adr/guidelines/0014-persistence-and-repository-boundaries.md", ".domain/capture/domain.md#capture", ".domain/sessions/domain.md#session-log", ".domain/tasks/domain.md#task", ".domain/tasks/naming.md#device"]
issue: null
```

## Status

Accepted. The cloud side is still unbuilt — this records the direction, the scope
it covers, and the questions it deliberately leaves open — but the two things that
had to be true before it could be accepted are true.

**The prerequisite it named is discharged.** `updated_at` and `deleted_at` are
columns on the `tasks` table now, added by the guarded additive `ALTER TABLE` local
ADR 0006 permits and seeded for pre-existing rows from `created_at`, which is the
only honest value a row that predates the column has
(`src/Infrastructure/Backlog.Infrastructure.Sqlite/SqliteTaskRepository.cs`).
Last-write-wins is expressible, so the design below has something to stand on.

**The Storage settings copy this record asked to be rewritten is rewritten.** The
screen no longer advises pointing the workspace root at a synced folder; it says
the backlog is one SQLite database and to keep it on a local disk. The old copy is
still quoted under **Context** because it is why the loss happened, not because it
is still on screen.

A **local** decision, numbered in the local sequence — not to be confused with
inherited ADR 0005 (modular monolith structure) under `.arc42/adr/guidelines/`.
Every reference below to ADR 0001–0005 without a qualifier means the local one.

It **amends** local ADR 0003 rather than superseding it. ADR 0003's decision —
that the canonical task store is one local SQLite database, and that a task's
content is markdown text inside it — survives this record intact. What changes is
the answer to a question ADR 0003 did not ask: what happens when the same person
runs the desktop on two machines.

## Scope

**Three kinds of state sync: the Task aggregate, session records, and the phone's
captures.** The first is what this record was originally written for, and its scope
said "the Task aggregate only". That is now too narrow. The other two are already
cross-device by their nature — a session record describes work done on a machine
the person is not sitting at, and a capture is made on a phone precisely so it can
be dealt with on a desktop — and a transport that carries tasks past them would
leave the two channels that most need it exactly where they are.

- **Tasks.** The aggregate local ADR 0003 made canonical in local SQLite,
  replicated and reconciled as the rest of this record describes.
- **Session records.** What the coding agents have been doing, as
  `.domain/sessions/domain.md#session-log` models it — machine-stamped,
  append-only, and cut back to the sanitization boundary stated under **Session
  records** below.
- **The phone's captures.** The one path the sync service already carries.
  `Backlog.Modules.Sync.Api.SyncStore` is an in-memory TTL dictionary standing in
  for a durable store; a capture is a task in the making, so it becomes a task
  document in the `tasks` container rather than a third shape with a store of its
  own, and the in-memory stand-in retires with it.

**Four things stay out, and stay out deliberately:**

- **Agent transcripts.** The prompts, the tool output and the file contents a
  session touched never leave the machine. Only the metadata listed under **Session
  records** does. This is not an omission to be filled in later — it is the
  sanitization boundary, and it is the reason session records can sync at all.
- **Workspace settings.** They describe one machine's disk. Replicating a path that
  means nothing on the other machine is worse than not replicating it, because the
  other machine would then act on it.
- **Feature flags.** Per-device on purpose. A flag records how one machine is being
  tried out; it is not a fact about the person, and a flag that travelled would
  turn an experiment on one machine into a change on both.
- **The derived knowledge layer.** Local ADR 0004 makes it generated and
  uncommitted. A derived artifact is regenerated on the second machine, not shipped
  to it, and shipping it would create exactly the second copy that record exists to
  remove.

The roadmap plan is on neither list. It has the same file-sync hazard as the
database and no answer yet; it stays an open question below rather than being
quietly filed under either heading.

## Context

**The failure this decision exists to prevent has already happened.** The
workspace root is configurable, and pointing it at a cloud-synced folder was
behaviour the Storage settings screen actively invited — its copy described the
pre-ADR-0003 world in which every entry was its own markdown file:

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

**The one prerequisite this record named has since been met.** When it was
written, the `tasks` table carried `created_at` and no modification timestamp, and
last-write-wins is not expressible without one — no sync design of any shape could
proceed until the aggregate could say when it last changed. It can now: `updated_at`
and `deleted_at` were added additively and the existing rows were seeded, exactly as
local ADR 0006 requires. That is the only part of this record that is built.

## Decision

**Azure hosts a replica of the person's tasks and session records, and the change
feed over each. Each device's local store remains canonical for what that device
owns.** Sync is reconciliation between equals, not a client talking to a system of
record.

### Storage

**Azure Cosmos DB for NoSQL, serverless. One account, one database, two
containers — `tasks` and `sessions` — both partitioned on `/ownerId`.**

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

Neither container holds anything but replicated documents. No invariant is
enforced there, no query serves the UI from them, and no domain logic runs against
them — they are replication substrate. That is what keeps inherited ADR 0014
satisfied: the cloud copies are replicas, and the canonical record stays on the
device that owns it.

#### Two containers, not one

Each container carries its own indexing policy, its own TTL and its own change
feed, and under serverless the split is free: serverless bills per request against
the account and levies no per-container charge, so a second container costs
nothing. On provisioned throughput it would be a second minimum, and the argument
would be a real trade; here there is no charge to trade against.

Free is only half of it. The other half is that merging them would be actively
worse in three ways, and each one holds on its own:

- **One container is one change feed.** Every device pulling tasks would drag
  session traffic through the same feed and discard it client-side. Session records
  are the higher-volume and faster-moving of the two, so they would set the pace and
  the cost of task sync — the one thing this record argues Cosmos serverless on.
- **One container is one indexing policy over two workloads that want different
  ones.** Tasks are read by id and by owner; session records are read by owner and
  recency. Indexing for both means indexing for neither.
- **One container is one TTL**, and the two retentions below are deliberately
  different. Merging would force the longer one on both, which means keeping task
  tombstones for a year to suit sessions, or expiring session history at six months
  to suit tombstones.

#### Retention is Cosmos TTL, not code

Both retentions are container TTL settings enforced by Cosmos. No reaper runs, no
scheduled job exists to fail silently, and nothing in the sync service has to be
trusted to delete anything — which matters most for the case where the service is
down or wrong, since that is exactly when a code-based reaper stops running and
nobody notices.

| Container | TTL | What expires |
|---|---|---|
| `tasks` | **180 days** | Task tombstones — a document with `deleted_at` set. A live task document carries no expiry. |
| `sessions` | **12 months** | The whole record. A session record is history, and history that old answers a question nobody is asking. |

180 days is chosen against how long a device may plausibly stay offline, and the
number matters in one specific direction: a tombstone that expires *before* a
device comes back lets that device push its still-live local copy and resurrect a
task the person deleted. Half a year is generous for a machine somebody actually
uses. A device gone longer than that is a re-pair, not a reconciliation, and this
record says so rather than letting the failure arrive unannounced.

### Compute

**Azure Container Apps, consumption plan, scale-to-zero**, hosting the existing
`Backlog.Modules.Sync.Api`. The deployment view already offered "App Service or
Container Apps"; this record settles it on Container Apps because scale-to-zero
means a personal tool costs nothing while nobody is syncing, and because the
Aspire AppHost already models `sync` as a container resource, so local topology
and deployed topology stay the same shape.

The service exposes four operations over the two containers, and no more:

| Operation | Meaning |
|---|---|
| `POST /sync/tasks` | Push the caller's task documents changed since its last push watermark. A capture pushed from the phone arrives here as a new task document. |
| `GET /sync/tasks?since={token}` | Pull the task change feed from a continuation token. |
| `POST /sync/sessions` | Append the caller's session records. A caller may only write records stamped with its own machine id. |
| `GET /sync/sessions?since={token}` | Pull the session change feed from a continuation token. |

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

Everything in this section is about tasks. Session records reconcile on different
terms, and the next section says why.

### Session records

Session records sync on different terms from tasks, and the difference is not a
special case bolted on — it falls out of who writes them.

**They are single-writer.** A session ran on one machine, and only that machine
holds the evidence for it. `.domain/sessions/domain.md#session-log` puts the
consistency boundary at one environment for exactly that reason: an environment is
the only thing that can read its own agents' records, so a log spanning several
would be asserting facts nobody gathered. No second device has anything to say
about a session it did not run.

**So last-write-wins does not apply to session records, and they have no
lost-edit failure mode.** The conflict policy this record accepts for tasks — and
the silent loss it accepts along with it — is not reachable here. Two devices
cannot both write one record, so there is never a second version to discard. This
is worth stating rather than leaving to be inferred, because the natural reading of
a record whose conflict policy is last-write-wins is that the policy covers
everything it syncs, and here it does not.

**They are machine-stamped and append-only.** Each record carries the id of the
machine that wrote it, and the service accepts a record only from the machine it
names. A session that moves gets a later record rather than an edit to an earlier
one, which is the same shape the domain already has — a session's state is derived
from the evidence available, never asserted, so evidence accumulates instead of
being revised. The `sessions` container therefore needs neither a tombstone nor an
`updated_at`: the TTL above is what removes a record, and nothing else does.

**The sanitization boundary is a whitelist, not a filter.** A session record
carries exactly this, and nothing else:

| Field | What it is |
|---|---|
| Session id | The identifier the agent gave the session. |
| Machine id | Which environment ran it. |
| Repository alias | The repository's alias — not its path, which would describe the machine's disk. |
| Branch | The branch the session worked on. |
| Started at | When the session began. |
| Last activity at | When it was last seen alive. |
| Turn count | How many turns it has taken. |
| Duration count | How long it has been running. |

**Never prompts, never tool output, never file contents.** That is the whole reason
a session record can leave the machine at all: the record is metadata *about* work,
not a copy *of* it, and everything the session actually said, read or wrote stays
where it happened. A whitelist rather than a filter because the two fail in
opposite directions — a filter that misses a field leaks it, a whitelist that
misses a field merely omits it. A field not in this table does not sync, and
adding one to the table is a decision to be taken here, not an implementation
detail to be settled in the pushing code.

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

**None of the rank work is built.** The constraint binds the first implementation
of the sync model rather than describing what the desktop does today. The trigger fix
needs nothing from this record, and should not wait for it.

### Identity

**Device pairing, not an account.** `.arc42/02-constraints.md` requires that
personal use need no login, and inherited ADR 0012 confirms device-session JWTs
rather than a user identity.

A first device generates an `ownerId` and a pairing secret. A second device is
paired by entering a short code, out of band, once. Each device then holds its
own long-lived registration credential in the OS credential store — DPAPI on
Windows — and exchanges it for a short-lived JWT (15–60 minutes, per inherited
ADR 0012) on each sync. The `ownerId` is the partition key of both containers.

**The sync service, not Cosmos, is what keeps a device inside its own partition.**
An earlier draft of this record said the partition key itself enforced it — that a
device "can only ever read the partition its token names" — and that was wrong.
Service-to-Azure access uses **managed identity** with a Cosmos data-plane role
assignment, and that role is scoped to the account: as far as Cosmos is concerned,
the service may read every partition in both containers. What actually constrains a
device is service code, which reads the `ownerId` out of the device's JWT and
refuses to issue a query outside the partition that JWT names.

The difference is recorded rather than smoothed over, because it moves where the
isolation lives. It is not a property of the storage layer that holds however the
service behaves; it is one check in one service, standing in front of a credential
that can see everything. It is also the only place the check could go, given the
identity model this record chose: Cosmos cannot authorize a principal it has never
heard of, and device-session JWTs are principals it has never heard of. Inherited
ADR 0012's device sessions and per-partition data-plane authorization are not
available at the same time, and this record takes the device sessions.

Managed identity still buys what it was chosen for: no connection strings, no
account keys, nothing in configuration to leak — the posture inherited ADR 0013
asks for. What it does not buy is per-partition authorization, and the query-scoping
check is the part of the service that carries that weight instead.

### The transport that was considered and deferred

**A per-device single-writer append-only log**, held in a shared folder or a git
repository. Each device writes only its own log and reads everybody else's, and
the current state of a task is a fold over all of them. It is deferred, not
dismissed, because it is a genuinely good answer to the problem as this record
first stated it.

What it buys is not small. Single-writer per file means file sync has nothing to
merge, so the R9 failure mode disappears without any service existing at all — no
Azure account, no managed identity, no monthly cost however small, and none of the
trust boundary the **Identity** section above has to argue about. Its conflict
story is also the stronger one: reconciling append-only logs is a fold over
recorded intent, where this record's whole-document last-write-wins discards one
side of a concurrent edit and cannot say that it did.

**What it cannot do is serve the phone or a second machine's IDE, and that is why
it lost.** The log lives in a folder, so a participant has to be something that
mounts that folder. The phone does not: it pushes a capture over HTTP and shares no
filesystem with any desktop. Neither does an IDE extension on a second machine with
no synced folder configured. Both are named channels in this architecture, both
already speak to the sync service, and both are in this record's scope. A transport
that reaches only the desktops would leave them exactly where they are now, which is
the problem this record exists to solve, solved halfway.

Adopting both was considered and is worse than either: two transports means two
reconciliation rules over one aggregate, and the question of which one wins when
they disagree has no good answer.

It stays on the table for the narrower case it answers well. If some corpus that
only desktops touch ever needs to travel and a cloud replica of it is unacceptable,
the append-only log is the shape that corpus should take — and this record is where
to find the reasoning rather than rediscovering it.

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
- **Session records become answerable from a device that did not run them.** The
  machine that ran a session stops having to be the machine the person is sitting
  at, which is the whole point of a fleet view and is not achievable while the
  evidence never leaves the environment that gathered it.
- **The phone's captures stop being transient.** `SyncStore`'s in-memory dictionary
  loses everything it holds when the container scales to zero, which is most of the
  time. A capture that lands in the `tasks` container is durable from the moment the
  phone pushes it, and it retires the stand-in rather than adding to it.

Negative:

- **A modification timestamp was a schema change to a store with live user data**,
  and this repository had no migration mechanism — the gap inherited ADR 0014
  records. This decision forced that story to be written, and it was: local ADR 0006
  names additive, idempotent bootstrapping as the mechanism, and `updated_at` and
  `deleted_at` were its first live-data change. That cost has been paid, and it was
  paid before this record was accepted rather than after.
- **Last-write-wins loses edits by design.** Two devices editing the same task
  while both offline will keep one version and discard the other. This is the
  policy the architecture already chose, and it is accepted here rather than
  re-litigated, but it is a real loss and the user is not told when it happens.
- **A replica of personal task content now lives in Azure.** The threat model
  changes: content that was purely local is now in a cloud account, and Key
  Vault, managed identity, and the service's query scoping are what stand in front
  of it.
- **The sync service is a trust boundary, and one check inside it carries the
  isolation.** Per **Identity** above, the managed identity is account-scoped and
  Cosmos does not enforce per-partition access; the service refusing to query
  outside the partition the device JWT names is what does. A bug there is not a
  degraded experience, it is a data-exposure defect, and it deserves tests that
  assert the negative case rather than only the positive one.
- **A second kind of personal state now leaves the machine.** Session metadata —
  which repository, which branch, when, for how long — describes a person's working
  patterns even with prompts and file contents withheld. The whitelist under
  **Session records** is what bounds that, and a whitelist is only as good as the
  code that honours it.
- **A task can sync while its attachment does not.** `.arc42/07-deployment-view.md`
  puts attachments on the desktop file system and provisions no blob storage, and
  this record does not change that. The second machine therefore receives a task
  referencing a file it does not have and has no way to fetch. This is deferred
  rather than solved, and it is stated here so that "the task synced" is not read as
  "everything about the task synced".
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

- The Task aggregate has gained `updated_at` and `deleted_at`. Both are
  infrastructure-shaped, and neither carries a lifecycle invariant, but they are
  domain-visible enough to belong in `.domain/tasks/domain.md`.
- Soft delete changes what "delete" means locally: the row persists as a
  tombstone until it is reaped. How long the *cloud* keeps one is settled above at
  180 days of container TTL; how long the *local* database keeps one is a separate
  question and is still open below.
- Retention stops being something anybody writes. Both numbers — 180 days for task
  tombstones, 12 months for session records — are container TTL settings, so the
  open question this record used to carry about tombstone retention is closed.
- `Backlog.Modules.Sync.Api.SyncStore` retires. It was always described in its own
  doc comment as an in-memory stand-in for a TTL-backed store; the `tasks` container
  is that store, and the capture path is the first thing to reach it.
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

- **How long the local database keeps a tombstone.** The cloud side is settled —
  180 days of Cosmos TTL on the `tasks` container, 12 months on `sessions`. What a
  device does with its own soft-deleted rows is not: nothing purges them today, and
  a local reaper has to expire no earlier than the cloud one or it will re-push a
  row the replica has already let go.
- **Attachments, still deferred.** `.arc42/07-deployment-view.md` states attachments
  live on the desktop file system and there is no blob storage. **The gap this
  leaves is that a task can sync while its attachment does not** — the second
  machine gets the task and a reference to a file it cannot reach. Widening scope to
  sessions and captures does not widen it to attachments, and nothing in this record
  makes the partial replica whole. Resolving it means either blob storage or an
  explicit statement, on the screen, that attachments are machine-local.
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
