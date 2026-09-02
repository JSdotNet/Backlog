# ADR 0005: An Azure-hosted task replica carries multi-device sync; the local store stays canonical

```meta
status: proposed
related: [".arc42/02-constraints.md#technical-constraints", ".arc42/07-deployment-view.md#cloud-deployment-azure", ".arc42/08-crosscutting-concepts.md#storage-and-sync", ".arc42/09-architecture-decisions.md", ".arc42/11-risks-and-technical-debt.md", ".arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".arc42/adr/guidelines/0012-authentication-external-identity-providers.md", ".arc42/adr/guidelines/0013-authorization-zero-trust.md", ".arc42/adr/guidelines/0014-persistence-and-repository-boundaries.md", ".domain/tasks/domain.md#task"]
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
- Cosmos serverless has a 20 GB logical partition ceiling. Irrelevant at personal
  scale, and a hard wall if the product ever stops being single-user.

Neutral:

- The Task aggregate gains `updated_at` and `deleted_at`. Both are
  infrastructure-shaped, and neither carries a lifecycle invariant, but they are
  domain-visible enough to belong in `.domain/tasks/domain.md`.
- Soft delete changes what "delete" means locally: the row persists as a
  tombstone until it is reaped. A retention policy for tombstones is left open
  below.
- Nothing here makes Azure canonical for anything, and nothing here changes what
  canonical means for a task. ADR 0003 stands.

## Open questions

- **Tombstone retention.** The existing cloud TTLs (sync payloads 7 days, webhook
  events 24h) assume transient state; a replica is not transient. How long a
  tombstone must survive is bounded by how long a device may stay offline, and
  that number is not chosen yet.
- **Attachments.** `.arc42/07-deployment-view.md` states attachments live on the
  desktop file system and there is no blob storage. A task that syncs but whose
  attachment does not is a partial replica, and this pass does not resolve it.
- **Roadmap plan.** `_roadmap/plan.json` is a single JSON file under the same
  workspace root and has the same file-sync hazard, unexercised so far. Out of
  scope here, and it will need the same treatment.
- **Recovering the six conflicted copies** is a data-recovery task, not an
  architecture one, and is tracked separately.
