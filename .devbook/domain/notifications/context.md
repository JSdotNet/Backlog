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
`Backlog.Infrastructure.Notifications`. The module, its abstractions and its
screens exist, with the owner's store as the `notifications` table in
`backlog.db`, and the MCP server publishes through it with its `notify`, `ask`,
`get_answer` and `close` tools. So does the sync exchange: the owner desktop
reads its notifications through `INotificationReplication` and hands each reply
to `INotificationReplies`. The infrastructure project does not yet. Every
context that owns a [Subject](domain.md#subject) closes its notifications when
the subject ends and answers the startup probe.

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
| Sync | `Backlog.Infrastructure.Sync` | the answer to the pairing question, and the code's expiry |

Three contexts publish through the same adapters, and two of them register an
answer handler for their source:

| Context | Publishes | About | Answer handler |
| --- | --- | --- | --- |
| Sessions | "Approve the validation of *run*?": Approve, Reject, or an open answer, when a run's Personal Validation stage goes in progress | the run | `SessionValidationAnswers` records the decision on the run file; an open answer is a rejection in the person's words |
| Sync | "Pair *device name*?": Allow or Refuse, `answerOn: owner`, when a device redeems this desktop's code | the pairing | `PairingAnswers` revokes the device on Refuse, then ends the pairing |
| Devbook | An info alert "Remark left on *chapter*", with a `backlog://devbook/<alias>/<chapter>` deep link, when a remark left on another device syncs in | the remark | none |

Sessions asks when the gate moves into progress, or is reported in progress again
after a rejection set the approval back to pending, and only once its earlier
question is settled. It closes an unanswered question when the gate moves on at
the desktop, with the reason "Validation decided at the desktop", and a decision
the desktop recorded after the question went out wins over a late answer.

Sync learns of a redemption by reading the owner's device list every five seconds
while its code waits, and for thirty seconds past its expiry, because the service
does not tell the issuing desktop. A redeemed pairing stops expiring and lasts
until the question is answered, across a restart too: the device is paired from
the moment it redeemed the code, so its question stays open rather than letting
the device stay unasked. Only the device whose code paired another may revoke it.

A remark typed on this desktop raises no alert, nor does one made more than a day
before it arrives, so a first pull of every remark raises none.

## Owner screens

On the owner desktop the person reads and answers through `INotificationCentre`,
in two places:

- **The notification centre**, a takeover the shell's **Notifications** option
  opens. The option wears the count of open questions. The centre lists the open
  notifications, and those settled in the last seven days, grouped by
  [Subject](domain.md#subject). The one picked is drawn whole beside the list as
  the library's `NotificationCard`. Its Markdown body renders through
  `MarkdownView`, Mermaid through `DiagramView`. A single-choice question shows
  buttons, a multiple-choice one checkboxes, an open one a plain-text box, and an
  alert Acknowledge. An answered card names the winning answer and the device it
  came from. A closed card names its reason.
- **Windows app notifications**, raised through the Windows App SDK for every
  notification the desktop publishes, and taken down once it settles. Set answers
  become action buttons, at most three. An open answer gets an inline text box
  and Send. A question whose answers do not fit offers **Open in Backlog**, which
  opens the centre on it.

The person tries the system out on **Settings › Notifications**
(`settings/notifications`, offered behind the `notifications` switch). **Send test
alert** publishes an info alert whose Markdown body carries a small Mermaid
diagram. **Send test question** publishes a question with the choices "It works"
and "Not yet" and an open answer, about the subject `notifications` / `test` / a
fresh id. Both are raised by the source `notifications` / `settings-test`, whose
answer handler only takes the answer. Under the buttons the page follows the last
test through `INotificationCentre.Changed`: published, pushed to the phone,
answered with the answer and the device it came from (or acknowledged, for the
alert), and closed. **Clear test** closes the test, and sending a new one closes
the one still open. The push row reads *not reported* until the phone push is
built.

Both hand an answer to `INotificationCentre.AnswerAsync`, which passes it to
`INotificationReplies.AcceptReplyAsync` as a [Reply](domain.md#reply) from this
device. That is the path a viewer's reply takes, so the owner checks every answer
the same way, whichever channel it came from.

## Dependencies

Notifications is downstream of Sync and upstream of every context that publishes.
It conforms to Sync's replica protocol, and it offers one port and one handler
contract to the contexts that publish, as an open host service.

### Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Sync](../sync/context.md) | Conformist (Notifications = conformist) | A notification exchange in `Backlog.Infrastructure.Sync` pushes to the `notifications` container and takes replies from `notificationReplies`; the phone posts replies through its device outbox | The replica protocol in `Backlog.Modules.Sync.Abstractions`; local ADR 0038 | Notifications and replies reach every paired device, and the sync service checks the owner before it stores a reply. |
| Shared kernel device identity | Conformist | `DeviceIdentity` in `Backlog.SharedKernel` | `.devbook/domain/tasks/domain.md#device` | The owner instance is named by the device that published the notification. |

### Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Sessions](../sessions/context.md) | OHS + Published Language (Notifications = supplier) | `INotifications.PublishAsync` when a run reaches Personal Validation, and `SessionValidationAnswers` for the `sessions` source; closes by subject when a session is archived or deleted, a run finishes, or a pull request merges or closes; answers the startup probe for all three | `Backlog.Modules.Notifications.Abstractions` | A question at Personal Validation is answered once and handed back to its handler; closing by subject also closes the MCP tools' notifications about a session. |
| MCP server (`Backlog.Infrastructure.Mcp`) | OHS + Published Language (Notifications = supplier) | `notify` and `ask` publish as source `mcp`, keyed by the call, about the calling session (`sessions` / `session` / its id) when the caller names one, otherwise a question is about itself (`mcp` / `ask` / the key); `get_answer` and `close` read and withdraw through `INotificationStatuses` and `INotifications.CloseAsync`, only for notifications source `mcp` published; its answer handler only takes the answer | `Backlog.Modules.Notifications.Abstractions` | `ask` answers the id at once and never waits; the session polls `get_answer` for pending, the answer, or closed with its reason. |
| [Sync](../sync/context.md) | OHS + Published Language (Notifications = supplier) | Publishes "Pair *device name*?" on the desktop whose code a device redeemed, `answerOn: owner`, and `PairingAnswers` for the `sync` source; closes by subject when the question is answered or the code expires unredeemed | `Backlog.Modules.Notifications.Abstractions` | Allow keeps the device; Refuse revokes it. |
| [Devbook](../devbook/context.md) | OHS + Published Language (Notifications = supplier) | Publishes an info alert when a remark left on another device syncs in; closes it when the remark is resolved | `Backlog.Modules.Notifications.Abstractions` | The alert links to the chapter. |
| [Tasks](../tasks/domain.md#task) | OHS + Published Language (Notifications = supplier) | Closes by subject when a task is marked Done, archived or deleted, here or synced in; answers the startup probe | `Backlog.Modules.Notifications.Abstractions` | A question about a finished task is closed on every device. |
