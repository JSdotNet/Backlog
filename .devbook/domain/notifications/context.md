# Notifications

```meta
index: root
type: context
deployment: module
related: [.devbook/arc42/adr/0039-notifications-are-an-owned-document-kind-and-push-only-nudges.md, .devbook/arc42/08-crosscutting-concepts.md#notifications, .devbook/arc42/05-building-block-view.md#cloud-service, .devbook/arc42/06-runtime-view.md#notification-reply, .devbook/arc42/adr/0038-backlog-queues-session-requests-and-a-manager-session-dispatches-them.md, .devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md, .devbook/design/interaction-guidelines.md#focus-and-selection]
```

Notifications lets a desktop instance tell the person something, or ask them
something, and hear the answer from whichever paired device they are holding.
Every other context publishes through it, so none builds its own channel.

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

- what a question is about. The context that owns the subject decides when it
  ends and tells Notifications, which never reads that context's data.
- what the answer does. The source's own handler acts on it.
- carrying the documents between devices. [Sync](../sync/context.md) stores and
  hands back the notification and reply documents and reads none of their fields.
  For the Android push, it reads the preview the owner writes on the
  envelope: kind, severity, title, summary, and whether the
  notification is private.
- the device identity. The owner instance is the shared kernel's
  [Device](../tasks/domain.md#device).

Android is the only phone platform. The local decision behind the storage and
the realtime nudge is `.devbook/arc42/adr/0039-notifications-are-an-owned-document-kind-and-push-only-nudges.md`.

The module is `Backlog.Modules.Notifications`. It is published to other contexts through
`Backlog.Modules.Notifications.Abstractions`, with its screens in
`Backlog.Modules.Notifications.UI`. The sync service's delivery adapters are
`Backlog.Infrastructure.WebPubSub`, for the desktop nudge, and
`Backlog.Infrastructure.NotificationHubs`, for the Android push. They sit behind
`INotificationNudgeChannel` and `INotificationPushChannel` in
`Backlog.Modules.Sync`.

The module, its abstractions and its screens exist, with the
owner's store as the `notifications` table in `backlog.db`. The MCP server
publishes through it with its `notify`, `ask`, `get_answer` and `close` tools. So does
the sync exchange: the owner desktop reads its notifications through
`INotificationReplication` and hands each reply to `INotificationReplies`. Every
context that owns a [Subject](domain.md#subject) closes its notifications when
the subject ends and answers the startup probe.

A module never references another module, so a context reaches
`Backlog.Modules.Notifications.Abstractions` through its adapter. The subject's
names and reason live as plain strings in the context's own abstractions. The
adapter that notices the end calls `CloseForSubjectAsync`:

| Context | Adapter | Notices the end in |
| --- | --- | --- |
| Sessions | `Backlog.Infrastructure.Sessions` | the session reading, and `finish_run` |
| Sessions (pull requests) | `Backlog.Desktop.UI` | the session card's pull request reading |
| Tasks | `Backlog.Infrastructure.Sqlite` | the task store, which the sync merge also writes through |
| Devbook | `Backlog.Infrastructure.Devbook` | a decorator over the remark store |
| Sync | `Backlog.Infrastructure.Sync` | the answer to the pairing question, and the code's expiry |

Three contexts publish through the same adapters; two register an
answer handler for their source:

| Context | Publishes | About | Answer handler |
| --- | --- | --- | --- |
| Sessions | "Approve the validation of *run*?", when a run's Personal Validation stage first reports a handoff | the run | `SessionValidationAnswers` |
| Sync | "Pair *device name*?": Allow or Refuse, `answerOn: owner`, when a device redeems this desktop's code | the pairing | `PairingAnswers` revokes the device on Refuse, then ends the pairing |
| Devbook | An info alert "Remark left on *chapter*", when a remark left on another device syncs in | the remark | none |

- **Sessions.** The question carries the gate's handoff as its body and offers
  Approve, Approve + follow-up (with text), Revise (with text) or Reject.
  `SessionValidationAnswers` records the decision on the run file. Approve and
  Approve + follow-up count as an approval, Revise as a rejection in the person's
  words, and Reject as a rejection with none. For Approve + follow-up it asks Tasks
  to file the follow-up entry first.
- **Devbook.** The alert carries a `backlog://devbook/<alias>/<chapter>` deep link.

Sessions asks on the first report of the gate that carries a handoff: its review
links, or the handoff text. A gate that only moved into progress has nothing to
show. After a rejection set the approval back to
pending, Sessions asks again on the next report that carries a handoff, once
its earlier question is settled. A handoff longer than the 32 KB body is cut
to fit and ends with a line saying so. Approve + follow-up is offered only when the
run file names its entry, by id or by plan and item.

Sessions closes an unanswered question when the gate moves on at the desktop, with
the reason "Validation decided at the desktop". A decision the desktop recorded
after the question went out wins over a late answer. A late Approve + follow-up
that loses this way files no entry.

The service does not tell the issuing desktop of a redemption, so Sync reads the owner's device list every five seconds
while the code waits, and for thirty seconds past its expiry. A redeemed pairing stops expiring and lasts
until the question is answered, across a restart too. The device is paired from
the moment it redeemed the code, so its question stays open. Only the device whose code paired another may revoke it.

A remark typed on this desktop raises no alert, nor does one made more than a day
before it arrives, so a first pull raises none.

## Owner screens

On the owner desktop the person reads and answers through `INotificationCentre`,
in two places:

- **The notification centre**, a takeover the shell's **Notifications** option
  opens, wearing the count of open questions. The centre lists the open
  notifications and those settled in the last seven days. The one picked is
  drawn whole beside the list as the library's `NotificationCard`, its Markdown body
  through `MarkdownView` and Mermaid through `DiagramView`.

  A single-choice question shows buttons, a multiple-choice one checkboxes, an open one
  a plain-text box, and an alert Acknowledge. A choice that takes text asks for its text in a plain-text box
  before it sends. An answered card names the winning answer and the device it
  came from. A closed card names its reason.
- **Windows app notifications**, raised through the Windows App SDK for every
  notification the desktop publishes, and taken down once it settles. Set answers
  become action buttons, at most three, and an open answer an inline text box
  and Send. A question whose answers do not fit, such as one with more than three
  choices or a choice that takes text, offers **Open in Backlog**, which opens the
  centre on it.

The centre's list follows the header's repository scope, by the rule the
Sessions list keeps (local ADR 0027):

- nothing pressed lists every notification;
- one repository, or several pressed with Ctrl, lists those about them;
- the Brain lists those about no repository.

A notification carries no repository of its own. The host works it out at read time
from what its producer left on it (`NotificationRepositories`):

- a run's worktree or place;
- a session's repository;
- a pull request's or a linked task's;
- a spec-manager product's connected target;
- a remark's chapter.

Pairing, the settings test and an MCP call from no session name none and are the
Brain's. One the host could not work out, because its source
could not be read, is listed under every scope, so a failing source never
hides a question. Opening the centre on a notification the scope hides shows
every repository until the scope is pressed again.

The owner desktop publishes that answer with each notification it pushes, worked
out by the same rule when it pushes: the repository's alias, the name its header
scope shows, or the Brain for a notification about no repository. One it could
not work out goes out with neither and reads as not known, as does every
notification an owner pushed before it published a repository. The phone keeps
no aliases of its own, so this is what its Repository filter reads
([Answering from another device](features.md#answering-from-another-device);
local ADR 0039).

Three filters sit over the list, each option counting what it would keep under
the other two:

- **From:** the system, per [Origin](domain.md#origin).
- **Asked by:** *AI agent* or *Person*, the asker kind of
  [Origin](domain.md#origin).
- **Kind:** *All*, *Questions*, *Alerts*, or *Open*, whatever still waits.

**Group by** offers five groupings:

- *Repository*;
- *System*;
- *Asked by*;
- *Subject* ([Subject](domain.md#subject));
- *Not grouped*.

Unchosen, it groups by repository, or by system while one repository or the Brain
alone is pressed. A group holding an open question comes first, then by its newest notification;
inside a group an open question leads, then newest first. Group by and the
filters are remembered on this machine; **Clear filters** resets every filter and keeps Group by. The **Notifications** option's count stays global, so
the subtitle says how many waiting questions lie outside the scope and how many
the filters hide, and reads *Loading…* until the first load returns.

The person tries the system out on **Settings › Notifications**
(`settings/notifications`, offered behind the `notifications` switch). **Send test
alert** publishes an info alert whose Markdown body carries a small Mermaid
diagram. **Send test question** publishes a question with the choices "It works"
and "Not yet" and an open answer, about the subject `notifications` / `test` / a
fresh id. Both are raised by the source `notifications` / `settings-test`, whose
answer handler only takes the answer.

Under the buttons the page follows the last test through `INotificationCentre.Changed`:
published, pushed to the phone, answered with the answer and its device
(or acknowledged, for the alert), and closed. **Clear test** closes the test, as does sending a new one. The push row reads *not reported*, because the phone push
carries no delivery receipt.

Both hand an answer to `INotificationCentre.AnswerAsync`, which passes it to
`INotificationReplies.AcceptReplyAsync` as a [Reply](domain.md#reply) from this
device. A viewer's reply takes the same path, so the owner checks every answer
alike.

## Dependencies

Notifications is downstream of Sync and upstream of every context that publishes.
It conforms to Sync's replica protocol and offers publishers one port and one handler
contract, as an open host service.

### Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Sync](../sync/context.md) | Conformist (Notifications = conformist) | A notification exchange in `Backlog.Infrastructure.Sync`, and the phone's device outbox | The replica protocol in `Backlog.Modules.Sync.Abstractions`; local ADR 0039 | Notifications and replies reach every paired device. |
| Shared kernel device identity | Conformist | `DeviceIdentity` in `Backlog.SharedKernel` | `.devbook/domain/tasks/domain.md#device` | The owner instance is named by the device that published the notification. |

- **Sync, the exchange.** The exchange pushes to the `notifications` container and
  takes replies from `notificationReplies`. The push preview travels on each
  `NotificationChange`. The sync service checks the owner before storing a reply.
- **Sync, the phone.** The phone posts replies through its device outbox, and
  registers for the Android push through `PUT /api/sync/notifications/push`.

### Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Sessions](../sessions/context.md) | OHS + Published Language (Notifications = supplier) | `INotifications.PublishAsync`, and `SessionValidationAnswers` for the `sessions` source; closes by subject; answers the startup probe | `Backlog.Modules.Notifications.Abstractions`; a choice that takes text on the [Answer Spec](domain.md#answer-spec) | A question at Personal Validation is answered once and handed back to its handler. |
| MCP server (`Backlog.Infrastructure.Mcp`) | OHS + Published Language (Notifications = supplier) | `notify` and `ask` publish as source `mcp`; `get_answer` and `close` read and withdraw | `Backlog.Modules.Notifications.Abstractions` | `ask` answers the id at once and never waits. |
| [Sync](../sync/context.md) | OHS + Published Language (Notifications = supplier) | Publishes "Pair *device name*?", and `PairingAnswers` for the `sync` source; closes by subject | `Backlog.Modules.Notifications.Abstractions` | Allow keeps the device; Refuse revokes it. |
| [Devbook](../devbook/context.md) | OHS + Published Language (Notifications = supplier) | Publishes an info alert when a remark left on another device syncs in | `Backlog.Modules.Notifications.Abstractions` | The alert links to the chapter. |
| [Tasks](../tasks/domain.md#task) | OHS + Published Language (Notifications = supplier) | Closes by subject when a task ends, here or synced in; answers the startup probe | `Backlog.Modules.Notifications.Abstractions` | A question about a finished task is closed on every device. |

- **Sessions.** It closes by subject when a
  session is archived or deleted, a run finishes, or a pull request merges or
  closes, and it answers the startup probe for all three. A choice's text reaches
  the handler with the answer. Closing by subject also closes the MCP tools'
  notifications about a session.
- **MCP server, publishing.** `notify` and `ask` publish keyed by the call. A
  notification is about the calling session (`sessions` / `session` / its id) when
  the caller names one; otherwise a question is about itself (`mcp` / `ask` / the
  key). `get_answer` and `close` work through `INotificationStatuses` and
  `INotifications.CloseAsync`, only for notifications source `mcp` published. Its
  answer handler only takes the answer. The session polls `get_answer` for pending,
  the answer, or closed with its reason.
- **MCP server, the relay.** A call with `toolUseId`,
  `questions` or `kind` goes to the Sessions
  [Question Relay](../sessions/domain.md#question-relay) through `IQuestionRelay`
  instead. `get_answer` and `close` given a waiting question's id answer from it
  (local ADR 0041). A call with none of them behaves as above.
- **Sync.** The question is published on the desktop whose code a device redeemed,
  with `answerOn: owner`. It closes by subject when the question is answered or the
  code expires unredeemed.
- **Devbook.** It closes the alert when the remark is resolved.
- **Tasks.** A task ends when it is marked Done, archived or deleted.
