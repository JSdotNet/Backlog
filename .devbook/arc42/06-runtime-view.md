# 06. Runtime View

```meta
related: [".devbook/arc42/05-building-block-view.md"]
```

## Task to GitHub Issue

```meta
related: [".devbook/arc42/05-building-block-view.md#desktop-app"]
```

Creating a task writes its SQLite row locally first (local ADR 0003), then asynchronously creates
one GitHub issue per targeted repo.

```mermaid
sequenceDiagram
    actor ME
    participant UI as Desktop UI
    participant Backlog as Backlog Service
    participant Store as Local SQLite Store
    participant GitHub as GitHub API

    ME->>UI: Create task
    UI->>Backlog: createTask(title, body, repo_ids, tags)
    Backlog->>Store: Upsert task row
    Backlog-->>UI: Task created (id)

    Note over Backlog,GitHub: Async GitHub sync - one issue per repo_id
    loop For each repo_id
        Backlog->>GitHub: POST /repos/{repo}/issues
        GitHub-->>Backlog: 201 Created (issue_number, html_url)
        Backlog->>Store: Update entry with github_issue_ids
        Backlog->>DB: Update index with issue links
    end

    Backlog-->>UI: github_issue_ids available
    UI-->>ME: Entry visible with GitHub issue links
```

## State Sync and Webhook Forwarding

```meta
related: [".devbook/arc42/05-building-block-view.md#cloud-service"]
```

In connected mode the desktop pushes state snapshots to the cloud, the phone pulls
deltas, and GitHub webhooks are validated and forwarded to the desktop.

```mermaid
sequenceDiagram
    participant Desktop as Desktop App
    participant Cloud as Cloud Service
    participant DB as Cloud Database
    participant Phone as Phone App
    participant GH as GitHub

    Desktop->>+Cloud: POST /sync/push (state snapshot)
    Cloud->>DB: Store SyncPayload (TTL 7 days)
    Cloud-->>-Desktop: 200 OK

    Note over Phone,Cloud: Phone comes online
    Phone->>+Cloud: GET /sync/pull?since=token
    Cloud->>DB: Query delta for user
    DB-->>Cloud: Changed items since token
    Cloud-->>-Phone: 200 OK (items, next_sync_token)

    Note over GH,Cloud: GitHub webhook received
    GH->>+Cloud: POST /webhooks/github
    Cloud->>Cloud: Validate HMAC signature
    Cloud->>DB: Store WebhookEvent (TTL 24h)
    Cloud-->>-GH: 202 Accepted
    Cloud-)Desktop: SSE - github.event forwarded
```

## Mobile Capture and Sync

```meta
related: [".devbook/arc42/05-building-block-view.md#mobile-app"]
```

A capture is written to the phone's own SQLite outbox before anything touches
the network, then sent in the order it was made. The phone never loses a capture
to a missing signal: it shows the item at once, marked waiting, and delivers it
when the service answers.

- **One outbox, many kinds.** Each row is `id, kind, payload, attempts,
  last_error, created_at`. The `kind` picks the sender; `capture` is the first,
  and later kinds join the same queue and the same order: `task`, pushed to its
  own endpoint, and `talk-note`, one entry that is several requests — see
  **Talk Note Upload**. Kind `note` joins them for a note the phone creates or
  edits — see **Mobile Note Sync**.
- **Oldest first, head of line.** A transient failure (no network, a timeout, a
  5xx, or a 401 from a service that restarted with a new signing key) stops the
  flush, so nothing overtakes it. It is retried after 2s, 4s, 8s, 16s. The fifth
  failure parks the entry as *waiting — tap to retry*, and it stays first in
  line. A refusal (any other 4xx) is set aside at once and holds nothing up.
- **Same id on every attempt.** The id is minted (`Guid.CreateVersion7()`) when
  the capture is queued. The service answers a repeat of it with 200 and the
  capture it already holds, so a retry after a lost 201 delivers it once.
- **Flush triggers.** Queuing an entry, the backoff timer, the app returning to
  the foreground, the network coming back, and a tap on a parked entry. The
  last three skip the pending wait and give a parked entry its attempts back,
  since each is a sign that the reason for the wait has gone.
- **The list outlives the service.** Every successful pull is kept in the same
  file with its time. A failed pull, including 503 `sync.replica_unavailable`
  while the Cosmos emulator warms up, shows that cached list with one status line
  saying why it is not newer.
- **Only the phone's own captures.** The Inbox tab lists the captures this phone
  made, until the desktop takes each in. The phone sends no acknowledgement and
  no other decision about an item: triage stays on the desktop
  (`.devbook/domain/inbox/features.md#triage-stays-on-the-desktop`). The phone's
  Dismiss, which posts `POST /inbox/{id}/ack`, is built today and is retired by
  plan `phone-app-redesign`.
- **Notes come back.** Notes are the one kind of Inbox item the phone pulls as
  well as sends. They travel on the task feed, not on the inbox listing, and
  **Mobile Note Sync** below describes them.

```mermaid
sequenceDiagram
    actor ME
    participant App as Phone App
    participant Outbox as SQLite Outbox
    participant Sync as Sync Service

    ME->>App: Capture (title)
    App->>Outbox: INSERT outbox (id v7, kind=capture, attempts=0)
    App-->>ME: Row shown at once, marked waiting

    loop Oldest entry first
        Outbox->>+Sync: POST /api/sync/inbox (same id every attempt)
        alt 201 Created, or 200 for an id it already holds
            Sync-->>-Outbox: Capture
            Outbox->>Outbox: DELETE entry
            App->>Sync: GET /api/sync/inbox (refresh, cache the list)
        else No network, timeout, 5xx or 401
            Outbox->>Outbox: attempts+1, last_error; wait 2s·2^(n-1); stop the flush
        else Any other 4xx
            Outbox->>Outbox: set aside as refused; continue with the next entry
        end
    end

    Note over App,Outbox: Five failures park the head entry and hold the queue behind it; resume, network back or a tap tries again
```

## Capture Attachments

```meta
related: [".devbook/arc42/adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md", ".devbook/arc42/05-building-block-view.md#cloud-service", ".devbook/domain/capture/domain.md#capture"]
```

A capture's files travel beside it rather than inside it (local ADR 0014). The
phone uploads each file first, under an id it mints itself, and posts the
capture that names them after; the capture document carries only their
metadata. All three sides are built — the phone's half is the talk note, below.

- **Upload, then capture.** `PUT /api/sync/attachments/{id}` carries the bytes,
  their declared `Content-Type` and their SHA-256 in `X-Attachment-Sha256`. The
  service refuses a type off the allowlist (415) or a body over the cap (413)
  before storing anything, stages the bytes while hashing them, and commits them
  only when they match the declared digest. The same bytes again under the same
  id are a 200 that writes nothing; different bytes are a 409.
- **A capture names only what is there.** `POST /api/sync/inbox` with
  `attachments` is refused (400 `inbox.capture_attachment_missing`, naming the
  ids) unless every file is stored under this owner with the size and digest
  the capture claims.
- **The desktop fetches** each file with `GET /api/sync/attachments/{id}`,
  served as a download with `nosniff`. Another owner's id is a 404. The Inbox
  intake records the files the capture names in `inbox_item_attachments` and
  saves the item first, then downloads each one, checks its SHA-256, and writes
  it into the item's folder under the workspace. A file that fails keeps its
  reason and waits for Retry; the item is received either way. See
  `.devbook/domain/inbox/requirements.md#capture-attachments`.
- **Acknowledgement releases.** When the capture's tombstone is stored — the
  phone's `POST /inbox/{id}/ack` or the desktop's pushed tombstone — the
  service deletes the files the held capture named. A failed delete is logged
  and left to the container's 30-day lifecycle rule; the acknowledgement never
  fails on it.

```mermaid
sequenceDiagram
    participant Phone as Phone App
    participant Sync as Sync Service
    participant Blob as Attachment Store
    participant Cosmos as Task Replica
    participant Desktop as Desktop App

    Phone->>+Sync: PUT /api/sync/attachments/{id} (Content-Type, X-Attachment-Sha256)
    Sync->>Sync: Allowlist, cap, digest shape
    Sync->>Blob: Stage blocks {ownerId}/{id} while hashing
    alt Digest matches
        Sync->>Blob: Commit block list (content type, sha256)
        Sync-->>Phone: 201 StoredAttachment
    else Mismatch, over the cap
        Sync-->>-Phone: 400 / 413 — nothing committed
    end

    Phone->>+Sync: POST /api/sync/inbox (attachments: metadata)
    Sync->>Blob: Find each {ownerId}/{id}: size + sha256
    alt All stored as claimed
        Sync->>Cosmos: Upsert capture document (metadata only)
        Sync-->>Phone: 201 InboxItem
    else Any missing or mismatched
        Sync-->>-Phone: 400 inbox.capture_attachment_missing (ids)
    end

    Desktop->>+Sync: GET /api/sync/attachments/{id}
    Sync->>Blob: Download {ownerId}/{id}
    Sync-->>-Desktop: Bytes (attachment, nosniff)

    Desktop->>+Sync: POST /api/sync/tasks (capture tombstone)
    Sync->>Cosmos: Find held capture, then upsert tombstone
    Sync->>Blob: Delete the held capture's files (best effort)
    Sync-->>-Desktop: 200
```

## Talk Note Upload

```meta
related: [".devbook/domain/capture/features.md#talk-note", ".devbook/arc42/06-runtime-view.md#capture-attachments", ".devbook/arc42/06-runtime-view.md#mobile-capture-and-sync"]
```

A talk note is the phone's half of **Capture Attachments**: one outbox entry of
kind `talk-note` whose payload is the capture — attachment metadata included —
and the ids of the files already uploaded. The bytes wait beside the database
in `talk-notes/outbox/`, since an outbox row holds JSON.

- **Prepared once, at Send.** Each picture is downscaled (1600 px longest edge,
  JPEG, EXIF reduced to the orientation) unless the note keeps originals; the
  size and SHA-256 are taken from the bytes that will actually go.
- **Uploads, checkpointed.** An attempt uploads each file not yet listed as
  uploaded, then posts the capture. Every upload that lands is written back
  into the entry before the next starts (`IStagedOutboxKind`), and hands the
  entry its attempts back — so a retry resumes at the first file still to go,
  and each file has the whole backoff budget of its own.
- **One refusal refuses the note.** A 409, 413 or 415 on a file sets the entry
  aside naming the file; the capture is never posted without it.
- **Released on delivery.** The files are deleted from the phone once the
  capture is taken (201, or 200 for an id the service already holds).
- **Status from the outbox.** *synced* once the entry is gone, *uploading i of
  n* while an attempt is sending its files, *waiting* otherwise, and *waiting —
  tap to retry* once parked.

```mermaid
sequenceDiagram
    actor ME
    participant App as Phone App
    participant Outbox as SQLite Outbox
    participant Files as talk-notes/outbox
    participant Sync as Sync Service

    ME->>App: Send to Inbox (body, speaker, tags, files)
    App->>Files: Downscale pictures, move files; size + sha256
    App->>Outbox: INSERT (id v7, kind=talk-note, capture + uploaded=[])
    App-->>ME: Inbox row marked waiting; note says "waiting"

    loop Each file not yet uploaded
        Outbox->>+Sync: PUT /api/sync/attachments/{id} (X-Attachment-Sha256)
        alt 201, or 200 for the same bytes
            Sync-->>Outbox: StoredAttachment
            Outbox->>Outbox: Checkpoint: uploaded += id, attempts = 0
        else No network, timeout, 5xx or 401
            Sync-->>Outbox: Transient — attempts+1, back off, stop the flush
        else 409, 413 or 415
            Sync-->>-Outbox: Refused — set aside, naming the file
        end
    end

    Outbox->>+Sync: POST /api/sync/inbox (same id, attachments: metadata)
    Sync-->>-Outbox: 201 InboxItem
    Outbox->>Outbox: DELETE entry
    Outbox->>Files: Delete the note's files
    App-->>ME: "synced"
```

## Mobile My Day and Task Push

```meta
related: [".devbook/arc42/05-building-block-view.md#mobile-app", ".devbook/arc42/06-runtime-view.md#mobile-capture-and-sync", ".devbook/domain/tasks/features.md#my-day"]
```

The phone's Tasks tab is My Day and nothing else. It reads the owner's existing
task feed rather than a My Day endpoint, and it writes exactly one thing: a task
added for today. It keeps no task store of its own — a projection of the feed,
and a push through the outbox.

- **Fold the feed.** `GET /api/sync/tasks?since=` is pulled from a cursor kept
  on the phone, page after page until `hasMore` is false, and each page is kept
  with the cursor after it in one transaction. Records fold into one row per
  task in a local `task_view` table: the later `UpdatedAt` wins, an equal stamp
  goes to the higher server stamp and then to the tombstone, a `DeletedAt` hides
  the row, and a `capture`-type document is skipped (it is the Inbox's,
  ADR 0009). A deleted task keeps its row, hidden, so the order pages arrive in
  never changes the result.
- **My Day is arithmetic**, as `.devbook/domain/tasks/domain.md#my-day` defines
  it. The list is the rows whose `in_my_day_on` is the phone's current local date
  and whose status is neither `done` nor `archived`.
- **A rejected cursor starts over.** `sync.cursor_malformed` or
  `sync.cursor_expired` drops the cursor and pulls once from the beginning; the
  rows already kept fold to the same result. Any other failure keeps the list on
  screen with one line saying why.
- **Adding is a push.** The task is built on the phone as a `TaskChange` with a
  `Guid.CreateVersion7()` id, the typed title, type `task`, status `draft`,
  priority `medium`, `CreatedAt` now and `InMyDayOn` today — added here means
  picked for today. It is queued as outbox kind `task` and posted to
  `POST /api/sync/tasks` with the same order, backoff and waiting marker as a
  capture. A retry sends the same id, and the replica's whole-document upsert
  makes that idempotent (ADR 0005).
- **At once, then replaced.** The row is written into `task_view` when the task
  is queued, with server stamp 0, so it is in today's list immediately and marked
  waiting until the outbox delivers it. The pull that delivery triggers brings
  back the replica's copy of the same write, which replaces it.

```mermaid
sequenceDiagram
    actor ME
    participant Tasks as Phone Tasks tab
    participant View as task_view (SQLite)
    participant Outbox as SQLite Outbox
    participant Sync as Sync Service

    ME->>Tasks: Add "Buy a charger"
    Tasks->>Outbox: INSERT outbox (id v7, kind=task, TaskChange with InMyDayOn=today)
    Tasks->>View: UPSERT row (server stamp 0)
    Tasks-->>ME: In My Day at once, marked waiting

    Outbox->>+Sync: POST /api/sync/tasks (same id every attempt)
    Sync-->>-Outbox: 200 Accepted
    Outbox->>Outbox: DELETE entry

    loop Until hasMore is false
        Tasks->>+Sync: GET /api/sync/tasks?since=cursor
        alt Page
            Sync-->>-Tasks: TaskChangeRecords, next cursor
            Tasks->>View: Fold page (LWW, tombstones kept, captures skipped) + cursor
        else 400 sync.cursor_malformed / sync.cursor_expired
            Tasks->>View: Forget the cursor; pull again from the beginning
        end
    end
```

## Sync Item Lifecycle

```meta
related: [".devbook/arc42/05-building-block-view.md#mobile-app"]
```

```mermaid
stateDiagram-v2
    [*] --> Queued: Capture written to the outbox
    Queued --> Sending: Flush reaches it (oldest first)
    Sending --> Delivered: 201, or 200 for a known id
    Sending --> Backoff: No network, timeout, 5xx or 401
    Backoff --> Sending: 2s, 4s, 8s, 16s, or resume / network back
    Backoff --> Parked: Fifth failed attempt
    Parked --> Queued: Tap, resume or network back (attempts reset)
    Sending --> Refused: Any other 4xx
    Refused --> Queued: Tap to retry
    Delivered --> [*]

    note right of Parked
        "Waiting — tap to retry";
        still first in line, holds the rest
    end note
    note right of Refused
        Set aside; the queue moves past it
    end note
```

## IDE Context-aware Capture

```meta
related: [".devbook/arc42/05-building-block-view.md#ide-extensions"]
```

Selecting code in the IDE captures it with repo context (file, line, branch) into the
local markdown cache and posts it to the inbox.

```mermaid
sequenceDiagram
    actor ME
    participant IDE as IDE Extension
    participant Ext as Extension API
    participant Capture as Capture Service
    participant API as Backend API
    participant Store as Local Markdown

    ME->>IDE: Select code -> right-click Capture
    IDE->>Ext: Get repo context
    Ext-->>IDE: file_path, line, branch, selection
    IDE->>ME: Show capture dialog (pre-filled context)
    ME->>IDE: Add title and tags -> confirm
    IDE->>Capture: createItem(title, body, metadata)
    Capture->>Store: Write to local markdown cache
    Capture->>API: POST /inbox/items (source=ide-vs-code)
    API-->>Capture: 201 Created (id)
    Capture-->>IDE: Item created (id, deep_link)
    IDE-->>ME: Confirmation + deep-link to item
```

## Copilot App Session Capture

```meta
related: [".devbook/arc42/05-building-block-view.md#ide-extensions", ".devbook/domain/capture/domain.md#source-adapter", ".devbook/domain/capture/features.md#copilot-app-session-capture"]
```

A GitHub Copilot App session can capture a backlog follow-up or knowledge note using
local session metadata (`session_id`, `worktree_path`, `branch`) via
`.devbook/domain/capture/domain.md#source-adapter`, reusing the same local
capture pipeline as IDE extensions and desktop workers.

```mermaid
sequenceDiagram
    actor ME
    participant Copilot as GitHub Copilot App Session
    participant Adapter as Source Adapter
    participant Capture as Capture Service
    participant Store as Local Markdown
    participant API as Backend API

    ME->>Copilot: /capture "follow-up"
    Copilot->>Adapter: Build source metadata (session_id, worktree_path, branch)
    Adapter->>Capture: createItem(title, body, source=ide, metadata)
    Capture->>Store: Write to local markdown cache
    Capture->>API: POST /inbox/items
    API-->>Capture: 201 Created (id)
    Capture-->>Copilot: Item created (id, deep_link)
    Copilot-->>ME: Confirmation + deep-link

    Note over Copilot,API: Local-first path only; no credential forwarding through Cloud Service
```

## Repository Baseline Scan and Health Signal

```meta
related: [".devbook/arc42/05-building-block-view.md#desktop-app", ".devbook/arc42/08-crosscutting-concepts.md#shared-data-types"]
```

Repository Management scans registered repositories, compares discovered package
versions with Technology Stack baselines, and emits monitoring signals when drift is
found.

```mermaid
sequenceDiagram
    participant Scheduler as Desktop Scheduler
    participant RepoMgmt as Repository Management
    participant RepoRegistry as Repository Registry
    participant Pkg as Package Registries
    participant TechStack as Technology Stack
    participant Index as Local JSON Index
    participant Monitoring as Monitoring Service

    Scheduler->>RepoMgmt: Start dependency scan
    RepoMgmt->>RepoRegistry: Load registered repos and manifests
    loop For each registered manifest
        RepoMgmt->>Pkg: Query latest package metadata
        Pkg-->>RepoMgmt: Current versions / advisories
    end
    RepoMgmt->>TechStack: Compare discovered versions to baselines
    TechStack-->>RepoMgmt: Drift and policy evaluation
    RepoMgmt->>Index: Persist repository health snapshot
    RepoMgmt->>Monitoring: Emit ProgressSignal(s)
    Monitoring-->>Scheduler: Dashboard/alert state updated
```

## Remote PC Wake and Status Update

```meta
related: [".devbook/arc42/05-building-block-view.md#desktop-app", ".devbook/arc42/05-building-block-view.md#cloud-service"]
```

Dev PC Management uses the optional cloud registry to wake a registered machine and
reconcile its status when the target desktop comes back online.

```mermaid
sequenceDiagram
    actor ME
    participant Desktop as Desktop App
    participant DevPC as Dev PC Management
    participant Cloud as Cloud Service
    participant Registry as Machine Registry
    participant Target as Remote Desktop Agent
    participant Monitoring as Monitoring Service

    ME->>Desktop: Wake remote machine
    Desktop->>DevPC: wake(machine_id)
    DevPC->>Cloud: POST /pc/wake/{machine_id}
    Cloud->>Registry: Load machine registration
    Registry-->>Cloud: MAC address, last status
    Cloud->>Target: Relay WoL packet
    Cloud-->>DevPC: 202 Accepted

    Note over Target,Cloud: Machine resumes and heartbeat starts
    Target->>Cloud: POST /pc/heartbeat
    Cloud->>Registry: Update status=online
    Cloud-->>DevPC: pc.wake-result / status update
    DevPC->>Monitoring: Emit machine-online signal
    Monitoring-->>ME: Machine visible as online
```

## Mobile Note Sync

```meta
related: [".devbook/domain/inbox/domain.md#note", ".devbook/domain/inbox/features.md#notes", ".devbook/arc42/06-runtime-view.md#mobile-capture-and-sync", ".devbook/arc42/06-runtime-view.md#mobile-my-day-and-task-push", ".devbook/arc42/06-runtime-view.md#capture-attachments", ".devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md", ".devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md"]
```

A note (`.devbook/domain/inbox/domain.md#note`) is the one Inbox item that
syncs both ways. The phone pulls every note, whichever device made it, and pushes the notes
it creates and edits. The desktop pushes its own note edits back, and a
tombstone when it archives or deletes one. This section is specified and not
built: plan `phone-app-redesign` builds it.

```mermaid
sequenceDiagram
    actor ME
    participant Notes as Phone Notes tab
    participant View as note_view (SQLite)
    participant Outbox as SQLite Outbox
    participant Sync as Sync Service
    participant Desktop as Desktop App

    ME->>Notes: Create or edit a note
    Notes->>View: UPSERT row (UpdatedAt now, server stamp 0)
    Notes->>Outbox: INSERT outbox (kind=note, whole note document)
    Outbox->>+Sync: POST /api/sync/tasks (type note, same id every attempt)
    Sync-->>-Outbox: 200 Accepted

    Desktop->>+Sync: GET /api/sync/tasks?since=cursor
    Sync-->>-Desktop: note document
    Desktop->>Desktop: Inbox intake: create or update the item of kind note (later UpdatedAt wins)

    ME->>Desktop: Edit, archive or delete the note
    Desktop->>Sync: POST /api/sync/tasks (note document, or its tombstone)

    Notes->>+Sync: GET /api/sync/tasks?since=cursor
    Sync-->>-Notes: Changed documents
    Notes->>View: Fold notes (later UpdatedAt wins, tombstone hides the row)
```

- **A third document kind.** A note travels on the replica as a task-shaped
  document in the `tasks` container with its own kind token, `type: "note"`,
  under the Inbox item's id. It follows the pattern
  `.devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md` set
  for `capture`. The difference is that nobody acknowledges a note away: the
  desktop takes it in and keeps pushing it.
- **Pulled with the task feed.** The phone already pages through
  `GET /api/sync/tasks?since=` for My Day. It folds every `note`-type document
  into a local `note_view` table the way it folds tasks into `task_view`. The
  later `UpdatedAt` wins, and a `DeletedAt` hides the row. The Notes tab reads
  `note_view` and searches it on the phone.
- **Pushed through the outbox.** A note the phone creates or edits is written
  into `note_view` at once, then queued as outbox kind `note` and posted whole to
  `POST /api/sync/tasks`. It keeps the order, backoff and waiting marker of every
  other outbox entry. A retry sends the same id, so the replica's whole-document
  upsert makes it idempotent.
- **The desktop owns triage.** The desktop's merge hands each `note` document to
  the Inbox intake, which creates the item or applies the later edit. The
  desktop pushes its own edits as note documents. When it archives or deletes a
  note it pushes the note's tombstone, and the phone drops the row on its next
  pull. The phone never pushes a tombstone, because it never archives an item.
- **Files keep their own path.** A note's photos and files travel as
  **Capture Attachments** describes: each is uploaded before the note document
  that names it, and the desktop fetches it from the attachment store.
