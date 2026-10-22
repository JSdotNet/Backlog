# Notifications

```meta
status: proposed
index: root
type: context
deployment: module
related: [.devbook/arc42/adr/0038-notifications-are-an-owned-document-kind-and-push-only-nudges.md, .devbook/arc42/08-crosscutting-concepts.md#notifications, .devbook/arc42/05-building-block-view.md#cloud-service, .devbook/arc42/06-runtime-view.md#notification-reply]
```

Notifications lets a desktop instance tell the person something, or ask them
something, and hear the answer from whichever paired device they are holding.
Every other context publishes through it, so no context builds its own channel
between devices.

Inside the boundary:

- the [Notification](domain.md#notification): its kind, severity, content and
  lifecycle;
- which [Owner instance](domain.md#owner-instance) writes it, and how a
  [Viewer instance](domain.md#viewer-instance) proposes a [Reply](domain.md#reply);
- which reply becomes the [Answer](domain.md#answer), and handing that answer back
  to the [Source](domain.md#source) that asked;
- closing a notification when the publisher closes it or its
  [Subject](domain.md#subject) ends.

Outside it:

- what a question is about. The context that owns the subject decides when the
  subject ends and tells Notifications. Notifications never reads that context's
  data.
- what the answer does. The source's own handler acts on it.
- carrying the documents between devices. [Sync](../sync/context.md) stores and
  hands back the notification and reply documents and reads none of their fields.
- the device identity. The owner instance is the shared kernel's
  [Device](../tasks/domain.md#device).

Android is the only phone platform. The local decision behind the storage and
the realtime nudge is
`.devbook/arc42/adr/0038-notifications-are-an-owned-document-kind-and-push-only-nudges.md`.

The module is `Backlog.Modules.Notifications`, published to other contexts through
`Backlog.Modules.Notifications.Abstractions`, with its screens in
`Backlog.Modules.Notifications.UI`. The delivery technology sits behind ports in
`Backlog.Infrastructure.Notifications`. The module, its abstractions and an empty
UI project exist, with the owner's store as the `notifications` table in
`backlog.db`; the screens, the sync exchange and the infrastructure project do not
yet. Every context that owns a [Subject](domain.md#subject) closes its
notifications when the subject ends and answers the startup probe.

A module never references another module, so a context reaches
`Backlog.Modules.Notifications.Abstractions` through its adapter: the subject's
names and reason live as plain strings in the context's own abstractions, and the
adapter that notices the end calls `CloseForSubjectAsync`:

| Context | Adapter | Notices the end in |
| --- | --- | --- |
| Sessions | `Backlog.Infrastructure.Sessions` | the session reading, and `finish_run` |
| Sessions (pull requests) | `Backlog.Desktop.UI` | the session card's pull request reading |
| Tasks | `Backlog.Infrastructure.Sqlite` | the task store, which the sync merge also writes through |
| Devbook | `Backlog.Infrastructure.Devbook` | a decorator over the remark store |
| Sync | `Backlog.Infrastructure.Sync` | the pairing client, and the code's expiry |

## Dependencies

Notifications is downstream of Sync and upstream of every context that publishes.
It conforms to Sync's replica protocol, and it offers one port and one handler
contract to the contexts that publish, as an open host service.

### Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Sync](../sync/context.md) | Conformist (Notifications = conformist) | A notification exchange in `Backlog.Infrastructure.Sync` pushes and pulls the `notifications` container; the phone posts replies through its device outbox | The replica protocol in `Backlog.Modules.Sync.Abstractions`; local ADR 0038 | Notifications and replies reach every paired device, and the sync service checks the owner before it stores a reply. |
| Shared kernel device identity | Conformist | `DeviceIdentity` in `Backlog.SharedKernel` | `.devbook/domain/tasks/domain.md#device` | The owner instance is named by the device that published the notification. |

### Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Sessions](../sessions/context.md) | OHS + Published Language (Notifications = supplier) | `INotifications.PublishAsync` and an `INotificationAnswerHandler` for its source name; closes by subject when a session is archived or deleted, a run finishes, or a pull request merges or closes; answers the startup probe for all three | `Backlog.Modules.Notifications.Abstractions` | A question at Personal Validation, and the MCP `ask` tool's questions, are answered once and handed back to its handler. |
| [Sync](../sync/context.md) | OHS + Published Language (Notifications = supplier) | Publishes a "Pair this new device?" question on the owner desktop; closes by subject when the pairing code is redeemed or expires | `Backlog.Modules.Notifications.Abstractions` | The answer confirms or refuses the pairing code. |
| [Devbook](../devbook/context.md) | OHS + Published Language (Notifications = supplier) | Publishes an alert when a remark is left on a chapter the person owns; closes it when the remark is resolved | `Backlog.Modules.Notifications.Abstractions` | The alert links to the chapter. |
| [Tasks](../tasks/domain.md#task) | OHS + Published Language (Notifications = supplier) | Closes by subject when a task is marked Done, archived or deleted, here or synced in; answers the startup probe | `Backlog.Modules.Notifications.Abstractions` | A question about a finished task is closed on every device. |
