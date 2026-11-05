# Sync

```meta
index: root
type: context
deployment: service
related: [.devbook/arc42/05-building-block-view.md#cloud-service, .devbook/arc42/08-crosscutting-concepts.md#task-sync, .devbook/arc42/08-crosscutting-concepts.md#session-record-sync, .devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md, .devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md, .devbook/arc42/adr/0011-devbook-annotations-are-a-third-replica-container.md, .devbook/arc42/adr/0044-devbook-subject-state-lives-in-backlogs-own-store.md, .devbook/arc42/adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md, .devbook/arc42/adr/0039-notifications-are-an-owned-document-kind-and-push-only-nudges.md]
```

Sync keeps one person's data the same on each of their devices. It knows which
devices belong together, and it carries the documents other contexts write from
one device to the next. It never becomes the authority for what those documents
say.

Inside the boundary:

- the [Owner](domain.md#owner) a set of devices shares, and the partition every
  replicated document is stored under;
- device registration: each [Device](domain.md#device), its
  [Registration credential](domain.md#registration-credential), and the
  [Device token](domain.md#device-token) it trades that credential for;
- pairing: the [Pairing Code](domain.md#pairing-code) that joins a second device
  to an owner;
- the owner-scoped replicas:
  - the task feed, the session-record feed, the annotation feed and the devbook
    subject feed (local
    [ADR 0044](../../arc42/adr/0044-devbook-subject-state-lives-in-backlogs-own-store.md)),
    each read through a signed [SyncCursor](domain.md#synccursor);
  - the `notifications` feed with its `notificationReplies` mailbox (local
    [ADR 0039](../../arc42/adr/0039-notifications-are-an-owned-document-kind-and-push-only-nudges.md));
- telling a device to pull: the Web PubSub nudge to an owner's desktops, and the
  Android push to an owner's phones;
- the push installation registry: one Notification Hubs installation per phone,
  tagged with its owner and itself, and deleted when the phone is revoked;
- which pushed copy of a document wins, by
  [Change Precedence](domain.md#change-precedence);
- attachment blobs, stored under the owner and served only through the sync
  service (local [ADR 0014](../../arc42/adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md));
- the service's version, and whether an app may sync with it at all
  ([Syncing between devices](requirements.md#syncing-between-devices); local
  [ADR 0005 §Version compatibility](../../arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md)).

Outside it: what each document means. A task document is
[Tasks](../tasks/domain.md#task)'s, a session record is
[Sessions](../sessions/domain.md#session-log)', an annotation and a
[devbook subject's state](../devbook/domain.md#subject-state) are
[Devbook](../devbook/context.md)'s, and a `capture`-kind document becomes an inbox
item in the [Inbox](../inbox/domain.md#inbox-item) (local
[ADR 0009](../../arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md)).

Sync stores a document whole, orders it by its stamp and tombstone, and hands it
back. It reads no field of the payload, and the merge into a device's local store
stays with the context that owns the document. A notification's push preview is
not the payload: the owner writes it on the envelope, so Sync can send the
Android push without opening the document.

The machine itself is the shared
kernel's [Device](../tasks/domain.md#device). Sync's own
[DeviceId](domain.md#deviceid) names that machine's registration with the sync
service, and is not the same identifier.

Sync runs in two places. The sync service is `Backlog.Modules.Sync.Api`, which hosts
`Backlog.Modules.Sync`. It holds the owners, devices, pairing codes and replicas in
the cloud. The sync client (`Backlog.Infrastructure.Sync`), on the desktop and the
phone, is the adapter each downstream context reaches the replica through, and the
place the version check sits. The Devices settings
page is `Backlog.Modules.Sync.UI`.

## Taking part in sync

```meta
type: feature-flag
key: sync
default: off
related: [".devbook/domain/sync/features.md#pairing-a-device", ".devbook/domain/sync/features.md#syncing-between-devices", ".devbook/domain/sync/features.md#carrying-captures-to-the-inbox", ".devbook/arc42/08-crosscutting-concepts.md#feature-enablement"]
```

The flag switches whether this machine talks to the sync service at all. Turning
it on lets the device:

- register;
- pair a second device;
- push its task changes, session records, remarks and devbook subject states;
- pull back what the other devices changed.

A session record travels as metadata only: it never carries a prompt, a
transcript, a working folder or a title (local ADR 0005 §Session records).

It replaces three flags, `device-pairing`, `task-sync` and `session-sync`. The
catalog entry names them as former keys, so a settings file written while any of
them was on comes up with this one on. The three were split so a person could pair
devices without their tasks leaving the machine. In practice the question is
whether this machine takes part in sync, and three switches made that one answer
look like three.

It ships off and marked `BETA`
([Feature Enablement](../../arc42/08-crosscutting-concepts.md#feature-enablement)).
The key is `SyncFeatures.Sync` in `Backlog.Modules.Sync.Abstractions`. The settings
screen lists it from the feature catalog in `AppFeatures.cs`, and a person's choice
is kept in `features.json` beside the app's other per-device choices. The flag is
retired when sync leaves `BETA`.

## Dependencies

Sync is upstream of the contexts whose documents it carries. It publishes the
replica protocol, the DTOs and routes in `Backlog.Modules.Sync.Abstractions`, as an
open host service. Each downstream context reaches it through an adapter in
`Backlog.Infrastructure.Sync` and never sees inside another context's documents.

### Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| Shared kernel device identity | Conformist (Sync = conformist) | `IDeviceIdentitySource` in `Backlog.SharedKernel`, read by the desktop sync client (`OwnEnvironment`) | `.devbook/domain/tasks/domain.md#device` | The service stamps session records with its own [DeviceId](domain.md#deviceid), not the installation identity. |

- **Shared kernel device identity:** The service issues its DeviceId at registration, and the local readers stamp the installation identity. The client maps this machine's own records back onto the shared-kernel identity, so one machine never reads as two.

### Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Tasks](../tasks/domain.md#task) | OHS + Published Language (Sync = supplier) | `TaskSyncWorker` pushes and pulls through `POST` and `GET /api/sync/tasks`; `TaskReplicaMerge` applies each pulled document | `TaskChange`, `TaskPayload` and `PushTasksRequest` in `Backlog.Modules.Sync.Abstractions`; local ADR 0005 | A task document comes back unchanged, and a push never moves it backwards ([Change Precedence](domain.md#change-precedence)). |
| [Sessions](../sessions/domain.md#session-log) | OHS + Published Language (Sync = supplier) | `SessionSyncSession` pushes this machine's session records through `POST /api/sync/sessions` and pulls the other machines' through `GET` | `SessionRecord` and `SessionRecordEntry` in `Backlog.Modules.Sync.Abstractions`; local ADR 0005 §Session records | A record is keyed by its machine ([Session Record Key](domain.md#session-record-key)), so no machine can overwrite another's. |
| [Devbook](../devbook/context.md) | OHS + Published Language (Sync = supplier) | `AnnotationSyncWorker` pushes and pulls remarks through `POST` and `GET /api/sync/annotations` | `AnnotationChange` and `AnnotationPayload` in `Backlog.Modules.Sync.Abstractions`; local ADR 0011 | A remark replicates on a task's terms, in its own container, under Change Precedence. |
| [Devbook](../devbook/domain.md#subject-state) | OHS + Published Language (Sync = supplier) | `DevbookSubjectSyncWorker` pushes and pulls through `POST` and `GET /api/sync/devbook-subjects`; `DevbookSubjectReplicaMerge` applies each pulled one | `DevbookSubjectChange` and `DevbookSubjectPayload` in `Backlog.Modules.Sync.Abstractions`; local ADR 0044 | A subject's state is stored whole in `devbookSubjects`, and Change Precedence holds with no tombstone. |
| [Notifications](../notifications/context.md) | OHS + Published Language (Sync = supplier) | The owner desktop pushes through `POST /api/sync/notifications` and every device pulls through `GET` | `NotificationChange` with its `NotificationPushPreview`, and `RegisterPushInstallationRequest`, in `Backlog.Modules.Sync.Abstractions`; local ADR 0039 | Each notification has one writer, and a reply reaches only its own owner. |
| [Inbox](../inbox/domain.md#inbox-item) | Customer/Supplier (Inbox = customer of the intake channel) | The desktop sync client hands `capture`-kind documents to `IInboxIntake` and drains `IInboxCaptureOutbox` | local ADR 0009; attachments, local ADR 0014 | A capture reaches the desktop once, keyed by its own id. |

- **Tasks:** The desktop runs `TaskSyncWorker` over the task feed, and the merge writes to the local store. A task document is stored whole. Other kinds ride the task feed under their owners' kind tokens:
  - roadmap plans (local ADR 0018);
  - GitHub settings (local ADR 0021);
  - notes (local ADR 0025);
  - Progress's `goal` and `impediment` documents, with the phone's `stand-up-context` and `impediment-edit` (local ADR 0035 §8, §9).

  A `sprint-series` document an older build pushed is read and dropped, never taken as a task.
- **Sessions:** Only the whitelisted metadata travels.
- **Devbook, subject state:** The merge applies each pulled state to the subject state store. The `devbookSubjects` container keeps a state under the one id its subject has on every device. A service that has no such route answers 404, and the desktop reads that as "not yet".
- **Notifications:** A viewer posts a reply to `…/replies`, and the owner lists and deletes its replies there. A desktop asks `GET …/negotiate` for its nudge socket, and a phone hands its FCM token to `PUT …/push`. A phone is pushed only when a notification is published or changes state.
- **Inbox:** The client hands a capture over before the task merge, and drains the outbox into the tasks push as tombstones. The capture is one made on the phone or in the IDE, and its attachments are on the blob store before it names them.
