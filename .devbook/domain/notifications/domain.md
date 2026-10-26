# Domain

```meta
status: proposed
type: domain
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context; each chapter's
> `type` records which of those it is. An Aggregate's owned Entities, Value
> Objects, and Enums are chapters directly beneath it.

## Notification

```meta
status: proposed
type: aggregate
related: [.devbook/domain/notifications/domain.invariants.md#notification, .devbook/arc42/adr/0038-notifications-are-an-owned-document-kind-and-push-only-nudges.md]
aliases: [NotificationDraft, NotificationId]
```

One message a [Source](#source) raises on an [Owner instance](#owner-instance).
It has a [kind](#notification-kind), a [severity](#severity), a title, a short
summary, a Markdown body and a [lifecycle](#notification-state). It may be about
a [Subject](#subject).

The notification is the consistency boundary for its answer. Only the owner
instance writes it, so only the owner decides which [Reply](#reply) becomes the
[Answer](#answer). A viewer that wants to answer writes a reply instead.

Its content is meant to be shown on every channel:

- The title holds at most 80 characters, and the summary at most 180, both plain
  text. They are the only content that leaves the replica in an OS notification
  or a push payload. A push also names the notification's id, kind, severity and
  state.
- The body is Markdown, with Mermaid diagrams and images. Images travel as
  attachments through the blob store
  (`.devbook/arc42/adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md`).
- A source can mark a notification private. Outside the app its summary then
  reads "Backlog has a question for you".
- A notification may carry a deep link that opens the item that raised it.

A notification has no deadline and no default answer. A question stays open until
the owner accepts an answer, the publisher closes it, or its subject ends.

The rules it holds are `### Invariant:` chapters in
[`domain.invariants.md`](domain.invariants.md#notification).

### Source

```meta
status: proposed
type: value-object
aliases: [NotificationSource]
```

The boundary that raised the notification, and the key inside it: for example
`sessions` / `run-58`. The owner hands the accepted answer back to the handler
registered for the source's boundary. An alert-only source registers no handler.

### Subject

```meta
status: proposed
type: value-object
aliases: [NotificationSubject]
```

The entity the notification is about, named by its boundary, its type and its
id: for example `sessions` / `session` / `sess-91`. The boundary that owns the
entity says when it ends, and every open notification about it then closes. A
subject that comes back, such as a reopened task, does not reopen its
notifications. Two notifications are equal in subject when all three parts are
equal.

The subjects a notification can be about, and when each ends:

| Subject | Owning context | Ends when | Closed with |
| --- | --- | --- | --- |
| `sessions` / `session` / session id | Sessions | archived or deleted in the Claude desktop app | "Session archived" |
| `sessions` / `run` / run id | Sessions | the delivery run finishes on any status, or no run file holds it any more | "Run finished" |
| `sessions` / `pull-request` / `owner/name#number` | Sessions | merged or closed | "Pull request merged" |
| `tasks` / `task` / task id | Tasks | marked Done or Archived, or deleted | "Task done" |
| `devbook` / `remark` / remark id | Devbook | resolved or deleted | "Remark resolved" |
| `sync` / `pairing` / pairing id | Sync | its pairing code expires unredeemed, or the question about the device that redeemed it is answered; a pairing from an earlier run stays live | "Pairing finished" |

The owning context reports the end where it notices it, and answers a probe
about each subject at startup for the ends it missed. An end made on another
device reaches the owner instance as that context's own change, and closes there
the same way.

### Answer Spec

```meta
status: proposed
type: value-object
aliases: [AnswerSpec, AnswerChoice, OpenAnswer]
```

What makes a question a question: the answers the owner will accept. It takes
one of four shapes:

| Shape | What a reply may hold |
|---|---|
| Single choice | Exactly one of two to six set choices. Each choice has an id, a label, and an optional style: primary or destructive. |
| Multiple choice | Any subset of the set choices, within an optional minimum and maximum. |
| Open | Plain text, with an optional placeholder and length limit. Markdown is not allowed in an answer. |
| Choices with open | One of the set choices, or an open answer instead. |

An answer spec may also say `answerOn: owner`. Only a reply made on the owner
instance is then accepted. Without it, any paired viewer may answer, critical
questions included.

### Notification Kind

```meta
status: proposed
type: enum
aliases: [NotificationKind]
```

- **Alert** informs. It can be acknowledged, but nothing waits on it.
- **Question** waits for an answer, and carries an [Answer Spec](#answer-spec).

### Severity

```meta
status: proposed
type: enum
aliases: [NotificationSeverity]
```

How urgent the notification is: **info**, **warning** or **critical**. Severity
is independent of kind, so a question can be critical and an alert only
informational. Severity never limits who may answer.

### Notification State

```meta
status: proposed
type: enum
related: [.devbook/arc42/08-crosscutting-concepts.md#notifications]
```

```mermaid
stateDiagram-v2
  [*] --> Open : owner publishes
  Open --> Acknowledged : alert acknowledged
  Open --> Answered : owner accepts a reply
  Open --> Closed : publisher closes
  Open --> Closed : subject ends
  Acknowledged --> [*]
  Answered --> [*]
  Closed --> [*]
```

- **Open**: published, and not yet settled.
- **Acknowledged**: an alert that a person acknowledged on any instance.
- **Answered**: a question whose owner accepted a reply as its answer.
- **Closed**: the publisher closed it, or its subject ended. A closed question
  needs no answer, and a reply sent after it is rejected as closed.

Only the owner makes these transitions. Read marks are per device and are not
part of the state.

## Reply

```meta
status: proposed
type: aggregate
related: [.devbook/domain/notifications/domain.invariants.md#reply, .devbook/arc42/06-runtime-view.md#notification-reply]
aliases: [ReplyOutboxKind]
```

What a person submits to answer a question, from any instance. A reply is a
proposal until the owner accepts it. A viewer writes it once and never changes
it; the phone writes it through its device outbox, so a reply made offline is
kept.

The owner reads each reply, checks it against the question's
[Answer Spec](#answer-spec), and accepts or rejects it. It rejects a reply with a
reason: the question is already answered, it is closed, the reply came from a
viewer when the question says `answerOn: owner`, or the reply does not fit the
answer spec. The viewer can then reply again. The owner deletes a reply once it
has processed it.

A reply names the notification, its owner instance, the device it came from, the
chosen choice ids and the open text.

## Answer

```meta
status: proposed
type: value-object
aliases: [NotificationAnswer]
```

The reply the owner accepted. A question has at most one answer. The owner stores
it, hands it to the source's handler, and replays it to a handler that was not
running when the answer arrived. A handler therefore receives an answer at least
once and treats a second delivery for the same notification as the same answer.

## Ubiquitous Language

```meta
status: proposed
type: ubiquitous-language
```

### Owner instance

```meta
status: proposed
type: term
aliases: [ownerInstanceId]
related: [.devbook/domain/tasks/domain.md#device]
```

The desktop app instance that published a notification, named by its device
identity. It is the only writer of that notification and the only instance that
decides its answer. A desktop is the owner of its own notifications and a viewer
of every other desktop's. An owner that is unpaired keeps its notifications: no
other instance takes them over, and they expire.

### Viewer instance

```meta
status: proposed
type: term
related: [.devbook/domain/notifications/domain.md#reply]
```

Any other paired device: the Android phone or another desktop. It shows
notifications and submits replies, and never changes a notification.
