# Domain

```meta
status: draft
type: domain
```

> The aggregates, the domain service and the value objects of the Sync context. The
> rules they enforce are in [domain.invariants.md](domain.invariants.md). The types
> live in `Backlog.Modules.Sync/DomainModels`.

## Device

```meta
status: draft
type: aggregate
aliases: [device registration, registered device]
related: [.devbook/domain/sync/domain.invariants.md#device, .devbook/domain/sync/features.md#pairing-a-device, .devbook/domain/tasks/domain.md#device]
```

One machine's registration with the sync service. It holds the machine's
[DeviceId](#deviceid), the [OwnerId](#ownerid) whose data it may see, the name a
person calls it in the device list, and a hash of its
[Registration credential](#registration-credential). The service never holds the
credential itself, so a copy of the record is not a copy of the secret.

A device comes in once, by registering as the first device of a new owner or by
redeeming a [Pairing Code](#pairing-code), and the record does not say which. It
is the consistency boundary for "who is this caller": a token is minted only for
a device whose stored hash matches the credential it presents.

It is not the machine. The machine is the shared kernel's
[Device](../tasks/domain.md#device), which every context shares. This aggregate is
that machine as the sync service knows it, under an id the service issued.

## Pairing Code

```meta
status: draft
type: aggregate
aliases: [PairingCode, pairing code, XXXX-XXXX code]
related: [.devbook/domain/sync/domain.invariants.md#pairing-code, .devbook/domain/sync/features.md#pairing-a-device, .devbook/domain/sync/requirements.md#pairing-a-device]
```

A short, single-use code a paired device issues so a second device can join its
[Owner](#owner). Eight characters from an alphabet that drops visually confusable
letters and digits (`PairingCodeFormat`), shown to the person as `XXXX-XXXX`. It
[works for ten minutes](requirements.md#requirement-a-pairing-code-works-for-ten-minutes)
and [pairs one device](requirements.md#requirement-a-pairing-code-pairs-one-device),
so a code that leaks unused stops being a way in on its own.

It is an invitation, not a credential: it authorizes one act, minting a
registration credential, and only within its window. The service stores it by
hash and records who issued it, when it expires, and whether it has been redeemed.
Redeeming it is the one transition, and the code is burned before the new
[Device](#device) is added.

## Change Precedence

```meta
status: draft
type: domain-service
aliases: [TaskChangePrecedence, AnnotationChangePrecedence, last-write-wins]
related: [.devbook/domain/sync/domain.invariants.md#change-precedence, .devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md, .devbook/arc42/adr/0011-devbook-annotations-are-a-third-replica-container.md, .devbook/domain/tasks/requirements.md#requirement-the-later-edit-wins-whole]
```

Decides whether a pushed copy of a document may replace the copy the replica
holds. It compares the device stamp (`updatedAt`) and the tombstone (`deletedAt`)
of the two copies and nothing else. It never reads the payload, which is what lets
one rule serve documents whose meaning belongs to other contexts.

It exists because every device pushes everything above its own watermark, and a
document it received sits above that watermark exactly as one it edited does. Each
device therefore echoes every document it pulled, once, on its next push. Without
the rule that echo would replace whatever the replica had learned in between: a
tombstone would come back as the live copy, and an edit would revert.

There are two copies of the rule, `TaskChangePrecedence` for the task feed and
`AnnotationChangePrecedence` for the annotation feed. The two change records share a
shape and not a type, and the rule is four lines (local ADR 0011). Both replica
adapters, the in-memory one and the Cosmos one, call the same copy, so they cannot
disagree about what a stale push is. Session records need no precedence: each is
written by one machine only (local ADR 0005 §Session records).

Invocation semantics: command-invoked, by a push, once per document.

## Shared Value Objects

```meta
status: draft
type: shared-value-objects
related: [.devbook/domain/sync/domain.invariants.md#shared-value-objects]
```

> Values the aggregates, the replicas and the endpoints all use.

### OwnerId

```meta
status: draft
type: value-object
aliases: [ownerId, owner id]
related: [.devbook/domain/sync/domain.md#owner]
```

The identifier of an [Owner](#owner): a version 7 GUID minted when the first device
registers. A type of its own rather than a bare GUID, because an owner id and a
device id travel together through every call, and swapping them would not fail to
compile. It is the partition key of every replica container. Equal when the GUIDs
are equal.

### DeviceId

```meta
status: draft
type: value-object
aliases: [deviceId, device id, machine id]
```

The identifier the sync service issues a [Device](#device) at registration or
pairing. It names the caller and grants nothing on its own. A session record is
stamped with it as the machine that wrote it. It is not the shared kernel's
installation identity, and the desktop maps one onto the other for its own records.

### OwnerScope

```meta
status: draft
type: value-object
related: [.devbook/domain/sync/requirements.md#requirement-every-sync-call-is-scoped-to-the-tokens-owner]
```

Who the current request is: the [OwnerId](#ownerid) and [DeviceId](#deviceid) read
out of its [Device token](#device-token) and nowhere else. Every handler that
touches an owner's data takes one, so the scope is a parameter a handler cannot
forget rather than a check it might. The store underneath does not re-check it:
the service reaches the store under one identity that can see every partition
(local ADR 0005 §Identity).

### SyncCursor

```meta
status: draft
type: value-object
aliases: [since cursor, sync cursor]
```

An owner and a store continuation that the service has verified belong together.
The service hands a client a signed cursor after each pull, and the client sends it
back as `since`. The codec (`HmacSyncCursorCodec`) checks the signature and the
owner before it produces a `SyncCursor`. A store continuation embeds the partition
it was minted for, so an unchecked one replayed by another owner would read that
owner's data, and the store would not object.

### Replica Cursor

```meta
status: draft
type: value-object
aliases: [TaskReplicaCursor, SessionReplicaCursor, AnnotationReplicaCursor]
related: [.devbook/domain/sync/domain.md#synccursor]
```

A verified position in one owner's task, session or annotation feed: an
[OwnerId](#ownerid) and a continuation. There is one type per feed, so a
continuation for one feed cannot be handed to another feed's replica, and no
replica port takes a bare string; the service reads each from a verified
[SyncCursor](#synccursor). A page read from a feed always carries the cursor after it, even when the
page is empty.

### Session Record Key

```meta
status: draft
type: value-object
aliases: [SessionRecordKey]
related: [.devbook/domain/sessions/domain.md#session-identity]
```

How one machine's copy of one session is addressed in the replica:
`{machineId}:{agentKind}:{sessionId}`. The agent is in the key because a session id
is unique only within its own agent. The machine id leads it, so a machine can
only ever address keys that begin with its own [DeviceId](#deviceid), and no
machine can overwrite another's record.

## Ubiquitous Language

```meta
status: draft
type: ubiquitous-language
```

> The words this context owns that are not chapters above.

### Owner

```meta
type: term
aliases: [the owner's backlog]
related: [.devbook/domain/sync/domain.md#ownerid, .devbook/domain/sync/features.md#syncing-between-devices, .devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md#identity]
```

The backlog an [OwnerId](#ownerid) names. It is not a person's account, since the
product requires none, but the identifier every device paired together shares. The
first device to register mints it; every device paired to that device afterward
registers under the same one. It is the partition every stored task, session
record, annotation and attachment carries. The sync service reads it out of a
device's presented [Device token](#device-token) to decide which partition a call
may touch.

### Registration credential

```meta
type: term
aliases: [device credential, credential]
related: [.devbook/domain/sync/features.md#pairing-a-device, .devbook/domain/sync/domain.md#device-token, .devbook/domain/sync/domain.md#device]
```

The long-lived secret a device receives at first registration or at pairing, and
[is shown once](requirements.md#requirement-the-registration-credential-is-shown-once).
The device keeps it in its own OS credential store rather than anywhere the backlog
itself is stored. Before every sync call, a device exchanges it for a
[Device token](#device-token) rather than presenting the credential itself on the
wire.

### Device token

```meta
type: term
aliases: [access token, device session token, sync token]
related: [.devbook/domain/sync/domain.md#registration-credential, .devbook/domain/sync/domain.md#owner, .devbook/arc42/adr/guidelines/0012-authentication-external-identity-providers.md]
```

The short-lived credential a device actually presents on a sync call. It lasts
thirty minutes unless the deployment sets a lifetime between 15 and 60, and
[an expired one is refused](requirements.md#requirement-a-device-token-expires).
The device obtains it by exchanging its
[Registration credential](#registration-credential), so the registration
credential itself never crosses the wire a second time. It names the device and
the [Owner](#owner) it belongs to, and
[every sync call is scoped to that owner](requirements.md#requirement-every-sync-call-is-scoped-to-the-tokens-owner).
