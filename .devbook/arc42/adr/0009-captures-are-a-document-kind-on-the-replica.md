# ADR 0009: Captures are a document kind on the replica; the desktop acknowledges by tombstone

```meta
status: active
date: 2026-09-15
related: [".devbook/arc42/08-crosscutting-concepts.md#storage-and-sync", ".devbook/arc42/08-crosscutting-concepts.md#task-sync", ".devbook/arc42/05-building-block-view.md#desktop-app", ".devbook/arc42/adr/0002-backlog-module-owns-the-entry-text-language.md", ".devbook/arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md", ".devbook/arc42/adr/0006-additive-schema-bootstrapping-is-the-local-migration-mechanism.md", ".devbook/arc42/adr/guidelines/0005-modular-monolith-structure.md", ".devbook/arc42/adr/guidelines/0014-persistence-and-repository-boundaries.md", ".devbook/domain/inbox/domain.md#inbox-item", ".devbook/domain/inbox/flow.md", ".devbook/domain/inbox/dependencies.md", ".devbook/domain/capture/domain.md#capture"]
issue: null
```

## Status

Accepted, and built: the Inbox module, its store, the sync intake and the
outbox landed together on 2026-09-15.

A **local** decision, numbered in the local sequence — not to be confused with
inherited ADR 0009 under `.devbook/arc42/adr/guidelines/`, which is about feature
slices. Every reference below to a local ADR by bare number means the local one.

It **narrows** local ADR 0005 on one point. That record says a capture "is a
task in the making, so it becomes a task document in the `tasks` container
rather than a third shape with a store of its own", and its status note adds
that an acknowledgement "clears the capture's source rather than writing a
tombstone". The first half stands — a capture is still a task-shaped document
in the `tasks` container, and there is still no third store — but the document
now carries its own kind token, and the acknowledgement is a tombstone. The
reasoning that forbade the tombstone is retired below, not overruled.

## Context

**The Inbox stopped being a projection.** Until this change the desktop's Inbox
pane was scaffolding: it listed the draft rows of the task table that carried a
`sourceInboxId`, and "route to backlog" meant editing that same row. The Inbox
now has its own module (`Backlog.Modules.Inbox`, with an Abstractions project)
and its own tables in `backlog.db` — `inbox_items`, `inbox_lists`,
`inbox_groups` — and routing an item **creates new tasks**, one per assigned
repository, each stamped with the inbox item's id as its create-only
`SourceInboxId`.

**That broke the discriminator.** On the replica, "this document is a capture"
meant `sourceInboxId != null`: the phone wrote its name there, the service's
`ListInbox` filtered on it, and the desktop's acknowledgement cleared it. Once
routed tasks carry a `SourceInboxId` of their own and are pushed like any other
task, the phone's `GET /api/sync/inbox` would list every routed task as a
capture still waiting. There is no shape-preserving discriminator left: every
field a capture document has, a routed task now also has, with a meaning of
its own.

**The tombstone prohibition rested on a fact that stopped being true.**
`AcknowledgeInboxItemCommand` refused to tombstone because the desktop turned a
capture into work *under the same document id* — under whole-document
last-write-wins a tombstone would have deleted that work on every device. The
desktop no longer creates a task with the capture's id; the capture id names
nothing but the capture.

**Two constraints bound the options.** The Sync module may not reference the
task domain (local ADR 0005, and the remarks in `CaptureInboxItemCommand`): the
replica is a relay, and a service that imported the Tasks vocabulary to name a
status would need redeploying whenever that vocabulary grew. And the mobile and
IDE clients post `{ title, source }` and read `{ id, title, source, capturedAt }`
— a change to those shapes is a change to three heads for a fact only the
desktop and the service need to agree on.

## Decision

**A capture document on the replica carries `type: "capture"`. That token is
the one thing that says "this is a capture" — on the service, on both replica
adapters, and on the desktop. The desktop acknowledges a capture by pushing a
tombstone of that document, from an outbox, through the ordinary tasks push.**

### The document

The service's `CaptureInboxItemCommand` writes the capture as it always did — a
task-shaped document in the `tasks` container, `status: draft`,
`priority: medium`, the client's source in `sourceInboxId` — with one literal
changed: the type token is `capture`, not `task`. The in-memory and Cosmos
replicas filter the inbox listing on that token instead of on `sourceInboxId`.
The literal is duplicated, on purpose, in the four places that read or write it
(service handler, two replica adapters, desktop merge), for the reason local
ADR 0005 gives for the other three task literals: what the two sides share is a
spelling, and neither imports the other's vocabulary to get it.

`capture` is deliberately **not** a member of the desktop's task-type
vocabulary. A desktop build that predates the Inbox module has no member for it
either, so its merge skips the document as unreadable and leaves it on the
replica — which is exactly what keeps a capture out of that build's task table.

### The desktop intake

`TaskReplicaMerge` hands every `capture`-kind document to the Inbox's
`IInboxIntake` port **before** the local task is read and before the
apply-against-local rule is consulted. The intake decides by id and status and
parses no token: an unknown live capture becomes an inbox item that **reuses
the capture's id**; a known one arriving again is a replay; a tombstone for an
item still open here archives it, owing nothing; a tombstone for an item this
desktop already decided on, or for an id it never saw, is nothing to act on.
Reusing the id is what makes intake idempotent by primary key — a replayed page
and the desktop's own echo both cost nothing.

A head composed without an intake — the phone, which has no inbox store —
holds captures on the replica rather than writing them anywhere.

### The acknowledgement

When the desktop routes or archives a replica-backed item, the item is flagged
as owing the replica an acknowledgement. `TaskSyncSession.PushAsync` drains
that outbox (`IInboxCaptureOutbox`) after the task batches: it rebuilds each
capture document, sets `deletedAt`, and pushes the tombstones through
`POST /api/sync/tasks` in the same batches tasks go in, clearing each flag only
once the replica has accepted its batch. No new endpoint, and no call on the
spot: routing happens offline as readily as online, and a decision taken with
no network reaches the phone on the first push that succeeds.

The service's own `AcknowledgeInboxItem` — the phone's "Triage" button — writes
the same tombstone, so "dealt with" has one meaning whichever end says it.

### What does not change

Nothing in any request or response record, route, or client. `CaptureRequest`,
`InboxItem`, the three `/api/sync/inbox` routes, the mobile head and the IDE
extension are byte-for-byte what they were. `TaskSyncSession` and
`TaskReplicaMerge` take the intake and the outbox as optional constructor
parameters defaulted to null, so every existing host composes unchanged.

## Consequences

Positive:

- **No wire change for mobile or IDE.** One literal the service writes and
  filters on changed value; the shapes the other heads speak are untouched.
- **Captures never touch the task table.** The Inbox owns its own rows and its
  own lifecycle, and Tasks learns of an inbox item only through the entries the
  Inbox asks it to make — which is the boundary `.devbook/domain/context-map.md` always
  drew and the projection never honoured.
- **Acknowledgement is durable and batched.** An offline route or archive is
  not lost, and an outbox that grew past the push limit cannot wedge the sync
  loop on a request that can never be accepted.
- **One meaning for "dealt with".** Phone and desktop both tombstone; the
  contradiction between `AcknowledgeInboxItemCommand`'s remarks and the desktop's
  behaviour is gone.

Negative:

- **Legacy capture documents arrive once as draft tasks.** A capture written
  before this change is a `task`-typed document with the phone's name in
  `sourceInboxId`; it no longer matches the capture predicate, so it goes down
  the task path on the new desktop and lands as the draft task it claims to be,
  and it drops off the phone's list. One-time, single-user, both ends upgraded
  together; accepted rather than special-cased, because a rule to tell those
  documents apart would be carried by every merge for ever.
- **An older desktop build holds captures on the replica.** It skips them as
  unreadable on every pull and logs each one, until it is upgraded. By design,
  and the same posture local ADR 0005 takes for any token a build does not know.
- **A second desktop that still holds an item open sees the first desktop's
  tombstone and archives its copy** rather than routing it twice. Correct, and
  new behaviour worth knowing about.
- **The phone's "Triage" button now means dismiss.** Relabelling it is a
  follow-up in the mobile head.

Neutral:

- The replica still holds exactly two containers and the capture is still a
  task-shaped document in `tasks`; local ADR 0005's Scope and Compute tables
  need no edit. What this record changes is one token's value and who writes a
  tombstone.
- `inbox_items`, `inbox_lists` and `inbox_groups` fall under local ADR 0003's
  idempotent `IF NOT EXISTS` DDL for their creation and under local ADR 0006's
  three shapes for any column added later, exactly as `roadmap_plan` does.
  Lists and groups are hard-deleted, not tombstoned: nothing replicates them.
