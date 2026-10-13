# Inbox

```meta
type: flow
status: draft
related: [.devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md, .devbook/arc42/adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md, .devbook/domain/inbox/domain.md#attachment]
```

> Lifecycle and process flows for this bounded context. Flows describe how an
> inbox item moves through its states over time. They complement `model.md`
> (structure), `domain.md` (responsibilities) and `domain.invariants.md` (the
> rules).

## Inbox item lifecycle

```mermaid
stateDiagram-v2
    [*] --> Unprocessed : Capture delivers item / typed by hand
    Unprocessed --> Routed : Route to Tasks / Create plan / To Brain
    Unprocessed --> Deferred : Defer for later review
    Unprocessed --> Archived : Dismiss / not actionable
    Deferred --> Deferred : Change the review date
    Deferred --> Unprocessed : Review date reached / Return to inbox
    Deferred --> Routed : Route to Tasks / Create plan / To Brain
    Deferred --> Archived : Dismissed after deferral
    Routed --> Unprocessed : Undo (return to the inbox)
    Archived --> Unprocessed : Undo (restore)
    Routed --> [*]
    Archived --> [*]
```

- `Routed` is the stored status `triaged` with a `RoutingTarget`, and
  `ItemTriaged` is emitted on the way in. [Inbox Status](domain.md#inbox-status)
  says why deciding and routing are one step.
- A `triaged` item with no routing target is not routed, so it may still be
  deferred or archived.
- Filing in a list (`list_id`) is not a transition.
- The rules each transition is held to are the `Inbox Item` chapters of
  [`domain.invariants.md`](domain.invariants.md#inbox-item).
- `Deferred → Unprocessed` happens two ways. A person returns the item by hand,
  whatever its date, or the [resurface sweep](domain.md#resurface-sweep) finds
  its review date reached.
- Undo reopens a decision only within the app session that took it
  ([Undo a decision](features.md#undo-a-decision)).

## Capture intake and acknowledgement

The replica document and its `capture` kind token are
decided in `.devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md`.

```mermaid
sequenceDiagram
    participant P as Phone / IDE
    participant S as Sync Service (replica)
    participant M as Desktop sync client
    participant I as Inbox intake
    participant O as Inbox outbox
    participant A as Attachment store (via sync)

    P->>A: PUT /api/sync/attachments/{id} (each file first)
    P->>S: POST /api/sync/inbox (title, source, attachments: metadata)
    S->>S: write document, type = capture
    M->>S: GET /api/sync/tasks?since=cursor
    S-->>M: documents, captures among them
    M->>I: ReceiveAsync(capture)
    alt unknown id, live
        I->>I: create item with the capture's id, record its attachments, save (Received)
        loop each attachment still waiting
            I->>A: GET /api/sync/attachments/{id}
            A-->>I: bytes
            I->>I: check sha256, write into the item's folder — Downloaded, or Failed with the reason
        end
    else known id, live
        I->>I: record attachments it lacks, fetch waiting ones (AlreadyKnown)
    else known id, tombstoned, still open
        I->>I: archive the item, owing nothing (Withdrawn)
    else otherwise
        I-->>M: AlreadyKnown / Ignored
    end
    Note over I,O: later — the reader routes or archives the item
    I->>O: acknowledgement pending
    M->>O: ListPendingAsync()
    M->>S: POST /api/sync/tasks (tombstones of the capture documents, batched)
    S-->>M: accepted
    M->>O: MarkSentAsync(ids)
    P->>S: GET /api/sync/inbox
    S-->>P: the capture is no longer listed
```

- The intake decides by id and status and parses no token. Every case has an
  answer and none is an error, because the caller is the sync client, and an
  error there would replay the page for ever. For the same reason, a file entry
  no fetch could honour is left off the item rather than thrown. A file that
  fails to download leaves the item Received.
- Only a route, archive or delete of a
  [replica-backed item](domain.md#replica-backed-item) puts an acknowledgement
  in the outbox. An item archived *because* the replica already carried a
  tombstone owes nothing.
- The acknowledgement is durable on the item, so a decision taken offline
  reaches the phone on the first push that succeeds.
- A deleted item's acknowledgement is kept beside the item store, and it is all
  that is left of the item. Until it is sent, a replay of the capture is
  `AlreadyKnown` rather than a new item.
- The desktop's own tombstone, its [sync echo](domain.md#sync-echo), comes back
  on the next pull as a known id it has already decided on: `AlreadyKnown`,
  nothing written. A second desktop still holding the item open archives it
  instead.
- The phone's own "Triage" acknowledges the same way: the service tombstones the
  capture document.

## Attachment lifecycle

One file on an item, as this machine holds it. The bytes come from the attachment
store (local ADR 0014), never from the replica.

```mermaid
stateDiagram-v2
    [*] --> Recorded : the capture names the file, item saved
    Recorded --> Downloaded : bytes fetched, sha256 matches, written to the item's folder
    Recorded --> Failed : fetch or write fails, or sha256 differs
    Failed --> Downloaded : Retry succeeds
    Failed --> Failed : Retry fails again (new reason)
    Downloaded --> [*]
```

- A replayed sync page never retries a Failed file; only the person's Retry does.
- Recording the same file again changes nothing, so a replay cannot move a
  Downloaded file back to Recorded.

## Undo and a returning tombstone

An undo cannot call back a tombstone the desktop already sent, so the intake
reads every later tombstone against the item's `restored_at`.

```mermaid
sequenceDiagram
    participant D as Desktop Inbox
    participant R as Replica
    participant P as Phone or another desktop

    D->>R: tombstone for the capture, stamped t1 (archive or route)
    Note over D: Undo sets restored_at to t2, after t1
    R-->>D: a later pull delivers the t1 tombstone again
    D->>D: t1 is at or before restored_at, so the item stays open
    P->>R: a dismissal stamped t3, after t2
    R-->>D: the next pull delivers the t3 tombstone
    D->>D: t3 is after restored_at, so the item is archived (Withdrawn)
```

- The echo may arrive on the next pull or days later, on a full replay.
- The stamps come from different clocks when the tombstone is not this
  desktop's own. A dismissal made after the undo but stamped at or before it by
  a skewed clock is ignored. That is accepted rather than an undo that does not
  hold.

## Merge and To Brain

Both decisions write into another context first and change the item only once
that write succeeded.

```mermaid
sequenceDiagram
    participant I as Inbox (Triage)
    participant T as Tasks
    participant B as Devbook (Brain)

    Note over I,T: Merge into a task
    I->>I: refuse a decided item, or a task the backlog no longer has
    I->>T: write the item as one comment on the task
    alt Tasks takes the comment
        I->>I: archive the item, DuplicateOf names the task
    else Tasks refuses it (a heading, checklist item or fence) or the write fails
        I->>I: the item stays open
    end
    Note over I,B: To Brain
    I->>I: refuse a decided item
    I->>B: write a Knowledge Note into the Brain's inbox area
    alt the note is written
        I->>I: route the item, target devbook
    else no Brain folder is set up, or the write fails
        I->>I: the item stays open and unrouted
    end
```

- Writing first means a failure never leaves an item archived against a task
  that never heard of it, or routed to a note that was never written.

## Batch route

Two or more items routed to Tasks together, as one plan import
([Route a batch to Tasks](features.md#route-a-batch-to-tasks)). A single item
routes on the press, without the panel.

```mermaid
flowchart TD
    A["Reader picks items, or opens a list"] --> B["Panel shows order, plan tag, tasks and dependencies"]
    B --> C{"Ask the AI to order?"}
    C -->|yes| D["Drafter adds inferred dependencies"]
    C -->|no| E["Confirm"]
    D --> E
    E --> F["Items that would spoil the document are left out by name"]
    F --> G{"Tasks imports the rest whole?"}
    G -->|yes| H["Each item is marked routed to its own tasks"]
    G -->|no| I["Nothing is routed, every item stays"]
```

- A loop among the dependencies holds Confirm until a switch breaks it, and
  Confirm waits while an ask is out.
- An item whose entries were made but which could not then be marked routed is
  named along with those entries.

## AI triage pass Apply

How **Apply** takes the accepted proposals of the
[AI triage pass](features.md#ai-triage-pass).

```mermaid
flowchart TD
    A["Apply"] --> B["Take the next accepted proposal, in list order"]
    B --> C{"Refused?"}
    C -->|no| D["The decision lands as one undo step"]
    D --> E{"More accepted proposals?"}
    E -->|yes| B
    E -->|no| F["Screen closes, a toast counts decisions and items left"]
    C -->|yes| G["Stop. What already landed stays"]
    G --> H["Screen keeps the proposals not taken, a note names the refused item and why"]
```

- A plan that routed only some of its members leaves the screen, and its
  members still waiting are left for the reader.

## A note on the phone

How a note saves in the phone's Notes tab
([Notes on the phone](features.md#notes-on-the-phone)).

```mermaid
stateDiagram-v2
    state "Saving..." as Saving
    state "Waiting to sync" as Waiting
    [*] --> Empty : New note opens an empty editor
    Empty --> [*] : left empty, nothing is made
    Empty --> Saving : the first save with something in it creates the note
    Saving --> Waiting : the outbox still holds the note
    Saving --> Saved : the outbox has sent it
    Waiting --> Saved : the outbox sends it
    Saved --> Saving : another edit
    Waiting --> Saving : another edit
    Saved --> Removed : the desktop archived or deleted it, seen on the next pull
    Removed --> [*]
```

- A note is kept on the phone first, so it saves with no signal.
- An editor still open on a removed note says its changes are no longer kept.
