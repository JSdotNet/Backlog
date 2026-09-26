# ADR 0014: Attachments travel through a blob store beside the replica; the sync service is the only door

```meta
date: 2026-09-25
related: [".devbook/arc42/07-deployment-view.md#cloud-deployment-azure", ".devbook/arc42/08-crosscutting-concepts.md#storage-and-sync", ".devbook/arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md", ".devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md", ".devbook/arc42/adr/guidelines/0003-aspire-for-web-services.md", ".devbook/arc42/adr/guidelines/0012-authentication-external-identity-providers.md", ".devbook/arc42/adr/guidelines/0013-authorization-zero-trust.md", ".devbook/arc42/adr/guidelines/0014-persistence-and-repository-boundaries.md", ".devbook/domain/capture/domain.md#capture", ".devbook/domain/inbox/domain.md#inbox-item"]
```

## Status

Proposed, 2026-09-25, for the `mobile-inbox-slice` plan. Nothing here is built:
no storage account is provisioned, no route exists, and no capture carries an
attachment today.

A **local** decision, numbered in the local sequence — not to be confused with
inherited ADR 0014 under `.devbook/arc42/adr/guidelines/`, which is about persistence
boundaries. Every reference below to a local ADR by bare number means the local
one. The plan item that asked for this record named it 0012; that number was
already taken by the MCP server decision, so it is 0014.

It **closes** the attachments open question local ADR 0005 leaves, for
captures, and says in its Consequences what it deliberately does not close for
tasks. It **extends** local ADR 0009: the acknowledgement tombstone that record
defines gains one more effect.

## Context

**The phone is where a capture happens, and the owner wants files on it.** The
mobile Inbox slice turns the phone into the capture device — a slide photographed
at a conference, a handout PDF, a document someone sent. The owner's requirement
is pictures **and any other attachment**: PDFs, office documents, any file. That
rules out the one cheap option on the table, a bounded inline image carried in
the capture document itself: it covers only small pictures, and it puts bytes
into Cosmos, where every one of them is billed as request units on write, on the
change feed, and on every pull a desktop makes.

**There is nowhere for the bytes to go.** `.devbook/arc42/07-deployment-view.md` states
there is no blob storage, and local ADR 0005 records the gap it leaves: "a task
can sync while its attachment does not". The replica holds documents, and a
Cosmos item is capped at 2 MB — smaller than a single phone photo at full
resolution.

**The sync service already owns the trust boundary.** Local ADR 0005's
**Identity** section is explicit that isolation between owners is one check in
one service — `OwnerScopeFilter`, reading `ownerId` out of the device JWT —
standing in front of a managed identity that can see every partition. Whatever
stores the bytes inherits that posture or has to build a second one.

**Two constraints bound the options.** Devices hold no Azure credential of any
kind — only their own registration secret and the short-lived device tokens it
mints (local ADR 0005). And the local topology has to be the deployed topology's
shape (inherited ADR 0003): a path that works only against real Azure is one no
test or QA run here can reach.

## Decision

**One Azure Storage account beside the Cosmos replica holds attachment bytes, in
one container, `attachments`, keyed `{ownerId}/{attachmentId}`. Every byte goes
in and comes out through the sync service. The capture document carries each
attachment's metadata and never its bytes.**

### The store

- **One general-purpose v2 storage account, Standard LRS, Hot tier**, in the
  resource group the replica lives in, provisioned by `infra/sync/main.bicep`.
- **One blob container, `attachments`, private.** No public access, no
  anonymous read, and `allowSharedKeyAccess: false` on the account, so the
  account keys cannot be used even by someone who obtained them — the posture
  the Cosmos account already takes with `disableLocalAuth`.
- **Blob name `{ownerId}/{attachmentId}`.** The owner prefix is what makes a
  whole owner's attachments one listable, deletable range; the attachment id is a
  GUID the **client** generates, so a phone with no network can name an
  attachment before it has uploaded it.
- **`ownerId` comes from the token, never from the request.** The route carries
  only the attachment id; the service prefixes it with the owner out of the
  device JWT, as every other sync operation does. A device asking for another
  owner's attachment id asks for a blob under its own prefix, which does not
  exist.
- **Locally, Azurite** as an Aspire resource beside the Cosmos emulator
  (`AddAzureStorage(...).RunAsEmulator()`, with the `attachments` container
  declared in the AppHost), so the upload and download path runs end to end on a
  developer machine with no cloud account.

### The transport

**Upload and download go through the sync service**, as two routes under the
existing `/api/sync` group, behind the same bearer authentication and
fallback-deny policy as the rest:

| Route | Meaning |
|---|---|
| `PUT /api/sync/attachments/{id}` | Store the request body as the caller's attachment `{id}`. The body is streamed to the blob, never buffered whole. The client sends the content type and its sha256 of the bytes; the service hashes while it streams and refuses a mismatch. A repeat `PUT` with the same id and the same hash is a success that writes nothing, so a retried upload is safe; the same id with a different hash is a conflict. |
| `GET /api/sync/attachments/{id}` | Stream the caller's attachment `{id}` back, with its stored content type, `Content-Disposition: attachment` and `X-Content-Type-Options: nosniff`, so nothing the service returns is rendered as active content in a browser. |

Two limits are enforced at the `PUT`, in the service, in one place:

- **A per-file size cap** — 25 MB by default, a setting. A request whose
  `Content-Length` exceeds it is refused before a byte is read; one that lies
  about it is cut off when the stream passes the cap.
- **A content-type allowlist** — images (`jpeg`, `png`, `heic`/`heif`, `webp`,
  `gif`), `application/pdf`, plain text, Markdown and CSV, and the Office Open
  XML document, spreadsheet and presentation types. Also a setting. It exists
  to refuse executables, scripts and HTML, not to enumerate every useful format;
  "any file" in the owner's sense is served by widening the setting, and a
  refused type answers `415` naming what was sent.

**Why through the service rather than direct to storage:**

- **One place for owner scope and limits.** The owner prefix, the size cap and
  the type allowlist live in the same service, next to `OwnerScopeFilter`, and a
  test that asserts the negative case asserts it against one component. Split
  across a token issuer and the storage service, each limit would have two
  halves that have to agree.
- **No key material on devices.** A device holds exactly what it holds today.
  Nothing it carries can address storage, so a lost phone is a device to revoke
  in the Devices tab, not a storage credential to rotate.
- **Identical locally.** The service talks to Azurite exactly as it talks to
  Azure, through the managed-identity-or-emulator connection Aspire hands it. No
  client ever needs to reach the storage endpoint, so there is no question of
  whether a phone on the LAN can reach an emulator bound to the developer's
  loopback.

### The rejected alternative: SAS URLs handed to clients

The service would mint a short-lived, single-blob, user-delegation SAS — write
for an upload, read for a download — and the client would talk to Blob Storage
directly.

What it buys is real: the bytes never pass through the container app, so a large
upload costs no compute time, cannot be cut short by a scale-in, and the service
never holds a stream open. At a personal tool's volumes none of that is a cost
worth paying for — the service scales to zero again minutes after the last
upload, and a 25 MB stream is seconds of a consumption replica.

What it costs is the list above, inverted: the size cap and the type allowlist
become SAS and blob-policy terms that Storage enforces loosely or not at all (a
SAS cannot cap a blob's size), owner scope is a second check in the SAS minting
path, a SAS URL in a log or a crash report is a bearer credential to a person's
file for its lifetime, and locally the phone has to reach Azurite directly. If
upload volume ever makes the service the bottleneck, this is the change to
revisit — and the capture document's shape below does not change when it is.

### The capture document

**The capture carries attachment metadata only**, as a list on the document —
per attachment its `id`, file `name`, `contentType`, `size` in bytes and
`sha256`. Bytes never enter Cosmos. `CaptureRequest` gains the same list as an
optional member, and `InboxItem` returns it, so a client that sends none is
exactly the client it was.

**A client uploads every attachment before it posts the capture that names
them.** On `POST /api/sync/inbox` the service checks that each named attachment
exists under the caller's owner prefix with the size and hash the capture claims,
and refuses the capture, naming the missing or mismatched ids, if one does not.
A capture on the replica therefore never names a blob that is not there. The
phone's offline queue holds a capture and its files together and flushes them in
that order; an upload that succeeds and a capture post that then fails leaves
only an orphan, which the backstop below removes.

The desktop's intake (local ADR 0009) fetches each attachment with the `GET`
when it takes a capture into the Inbox, verifies the hash, and keeps the file
beside the inbox item in local storage. The inbox item owns its local copy from
then on; the blob is a courier, not a home.

### Lifecycle

- **The acknowledgement tombstone releases the blobs.** When the service stores
  a tombstone for a capture — pushed by the desktop's outbox through
  `POST /api/sync/tasks`, or written by the phone's own acknowledgement — it
  reads the attachment list from the capture document it **already holds**, not
  from the tombstone payload, and deletes those blobs. Reading its own copy means
  a tombstone cannot name, and so cannot delete, a blob its capture never did.
  The delete is best effort: the tombstone is written whether or not the delete
  succeeds, and a failure is logged and left to the backstop.
- **A storage lifecycle rule is the backstop.** One management-policy rule on
  the `attachments` container deletes any blob not modified for 30 days. It
  catches the uploads whose capture never arrived and the deletes that failed,
  with no reaper to run and nothing in the service to trust — the argument local
  ADR 0005 makes for Cosmos TTL, made again for Storage.

## Consequences

Positive:

- **A capture from the phone can carry any file the allowlist admits**, up to
  the cap, and arrives on the desktop whole. The 0005 open question is closed
  for captures.
- **The replica stays a document store.** Cosmos pays only for metadata; the
  2 MB item limit and the change-feed cost of bytes never come into it.
- **The trust boundary does not move.** Isolation between owners is still one
  check in one service; attachments are one more thing behind it, not a second
  door beside it.
- **Retention is not code.** Release on acknowledgement plus a 30-day lifecycle
  rule, with nothing scheduled to fail silently.

Negative:

- **Attachment bytes transit the container app.** Every upload and download is
  compute time and a held connection. Bounded by the cap and a personal tool's
  volume, and the reason SAS stays recorded as the alternative.
- **A desktop away for more than 30 days loses a waiting capture's files.** The
  backstop cannot tell an orphan from a blob whose capture is still waiting for a
  desktop that has not pulled. The capture itself survives; its files do not,
  and the intake has to say so on the item rather than fail. Thirty days is
  chosen as far longer than a conference trip and far shorter than local ADR
  0005's 180-day tombstone horizon.
- **The lifecycle rule cannot be verified locally.** Azurite does not run
  management policies — the same gap the Cosmos emulator has with TTL — so the
  30-day number is deployed-only behaviour.
- **A wire change for the mobile head.** Unlike local ADR 0009, this adds to
  `CaptureRequest` and `InboxItem`. Optional and additive, so an older phone or
  the IDE extension keeps working unchanged; but a deployed service that
  predates the field strips it, so the service has to be deployed before a phone
  sends attachments.
- **A new Azure resource and a new role assignment** to provision, monitor and
  keep inside the budget alert.

Neutral:

- **Task attachments are unchanged.** A task's `Attachment` is still one path to
  a folder or an archive on the machine that set it, deliberately a pointer and
  not a copy, and it still syncs as a path the other machine may not be able to
  resolve. This record gives captures a store; it does not copy desktop folders
  or zips into it. Whether a routed capture's files become the new task's
  attachment, and whether task attachments ever travel, is a later decision — the
  0005 open question stays open for tasks, now narrowed to them.
- **Cost at conference volumes.** Indicatively, a three-day conference at 150
  captures and 300 files averaging 3 MB is about 1 GB, held for days rather
  than the full 30: a few cents of Hot LRS capacity, operations in the fractions
  of a cent, egress inside the free monthly allowance, and a few minutes of
  consumption compute inside the free grant. Well under €1 a month on top of
  local ADR 0005's budget.
- **The new resource in `infra/sync/main.bicep`**: a storage account
  (`allowSharedKeyAccess: false`, minimum TLS 1.2, no public blob access), the
  `attachments` container, the 30-day management-policy rule, and a
  **Storage Blob Data Contributor** role assignment for the existing
  user-assigned sync identity, scoped to the container rather than the account.
  The container app gains the blob endpoint and container name as settings. Like
  the Cosmos role, the grant spans every owner's prefix; the service's owner
  prefixing is what keeps a device inside its own.
