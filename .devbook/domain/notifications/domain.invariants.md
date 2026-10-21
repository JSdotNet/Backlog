# Invariants

```meta
status: proposed
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate. Nothing is built yet, so no invariant names a test.

## Notification

```meta
status: proposed
type: invariants
related: [.devbook/domain/notifications/domain.md#notification]
```

> The rules the Notification holds its kind, its answer and its lifecycle to.

### Invariant: Only the owner instance changes a notification

```meta
status: proposed
type: invariant
```

Every state change and every accepted answer comes from the owner instance that published the notification (`notification.not_owner`). One writer per document is what lets the replica carry it without merge rules.

Enforced at: all mutations

### Invariant: A question has an answer spec and a subject

```meta
status: proposed
type: invariant
```

A notification of kind Question carries an answer spec and a subject (`notification.question_incomplete`). An alert carries no answer spec, and may have no subject.

Enforced at: constructor

### Invariant: Kind and severity come from their fixed sets

```meta
status: proposed
type: invariant
```

The kind is Alert or Question, and the severity is info, warning or critical (`notification.unknown_kind`, `notification.unknown_severity`).

Enforced at: constructor

### Invariant: No deadline and no default answer

```meta
status: proposed
type: invariant
```

A notification carries no deadline and no default answer, so time alone never settles a question (`notification.deadline_not_supported`).

Enforced at: constructor

### Invariant: State moves only forward from Open

```meta
status: proposed
type: invariant
```

A notification starts Open and moves once, to Acknowledged, Answered or Closed (`notification.already_settled`). Only an alert is acknowledged, and only a question is answered.

Enforced at: `Acknowledge()`, `Accept()`, `Close()`, `CloseForSubject()`

### Invariant: A question has at most one answer

```meta
status: proposed
type: invariant
```

The first valid reply the owner processes becomes the answer, and every later reply is rejected as already answered (`notification.already_answered`).

Enforced at: `Accept()`

### Invariant: Title and summary stay within their bounds

```meta
status: proposed
type: invariant
```

The title holds at most 80 characters and the summary at most 180, both plain text (`notification.content_too_long`). The body holds at most 32 KB of Markdown.

Enforced at: constructor

## Reply

```meta
status: proposed
type: invariants
related: [.devbook/domain/notifications/domain.md#reply]
```

> The rules a reply must meet before the owner accepts it as the answer.

### Invariant: A reply fits the answer spec

```meta
status: proposed
type: invariant
```

A reply holds only choice ids the answer spec offers, within its count limits, and open text only where the spec allows it and within its length (`reply.invalid`).

Enforced at: `Accept()` on the Notification

### Invariant: A reply to a closed question is rejected

```meta
status: proposed
type: invariant
```

A reply that reaches the owner after the question closed is rejected as closed (`reply.closed`).

Enforced at: `Accept()` on the Notification

### Invariant: An owner-only question takes only the owner's reply

```meta
status: proposed
type: invariant
```

When the answer spec says `answerOn: owner`, a reply from any viewer instance is rejected (`reply.owner_only`). Without it, any paired viewer may answer.

Enforced at: `Accept()` on the Notification

### Invariant: A reply comes from the same owner

```meta
status: proposed
type: invariant
```

A reply is accepted only from a device under the same owner as the notification (`reply.foreign_owner`). The sync service checks it first, and the owner instance checks it again.

Enforced at: the sync reply endpoint, and `Accept()` on the Notification
