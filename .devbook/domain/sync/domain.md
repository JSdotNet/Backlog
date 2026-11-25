# Domain

```meta
type: domain
```

> The rules these types enforce are in [domain.invariants.md](domain.invariants.md). The types
> live in `Backlog.Modules.Sync/DomainModels`.

## Device

```meta
type: aggregate
aliases: [device registration, registered device]
related: [.devbook/domain/sync/domain.invariants.md#device, .devbook/domain/sync/features.md#pairing-a-device, .devbook/domain/tasks/domain.md#device, .devbook/domain/sync/domain.md#device-key, .devbook/domain/sync/domain.md#device-assertion, .devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md#2-a-device-proves-itself-with-a-non-exportable-key]
```

One machine's registration with the sync service. It holds:

- the machine's [DeviceId](#deviceid);
- the [OwnerId](#ownerid) whose data it may see;
- the name a person calls it in the device list;
- the device whose code paired it, none for an owner's first device;
- one proof: the public half of its [Device key](#device-key), or, for a device
  without one, a hash of its [Registration credential](#registration-credential).

Neither proof is a secret, so a copy of the record copies no secret. Binding a
key drops the credential hash, so a device never holds both (`Device`).

A device comes in once. The first device of an owner enrols through the owner's
GitHub sign-in (see [Owner](#owner)), and every later one redeems a
[Pairing Code](#pairing-code). The desktop that issued the code can then ask its
person whether to keep the device. The aggregate is the consistency boundary for
"who is this caller". A key-bound device is minted a token only for a
[Device assertion](#device-assertion) its key signed, once per assertion. A
device without a key is minted one only for a credential whose hash matches. The
device whose code paired it can revoke it. It then mints no further token, and a
token it already holds lasts until it expires.

It is not the machine. The machine is the shared kernel's
[Device](../tasks/domain.md#device), which every context shares. This aggregate is
that machine as the sync service knows it, under an id the service issued.

## Pairing Code

```meta
type: aggregate
aliases: [PairingCode, pairing code, XXXX-XXXX code]
related: [.devbook/domain/sync/domain.invariants.md#pairing-code, .devbook/domain/sync/features.md#pairing-a-device, .devbook/domain/sync/requirements.md#pairing-a-device, .devbook/domain/sync/domain.md#device-key]
```

A short, single-use code a paired device issues so a second device can join its
[Owner](#owner). It has eight characters from an alphabet that drops visually confusable
letters and digits (`PairingCodeFormat`), and the person sees it as `XXXX-XXXX`. It
[works for ten minutes](requirements.md#requirement-a-pairing-code-works-for-ten-minutes)
and [pairs one device](requirements.md#requirement-a-pairing-code-pairs-one-device),
so a code that leaks unused stops being a way in on its own.

It is an invitation, not a credential: it authorizes one act, adding a device
under the code's owner, and only within its window. The new device is bound to
the [Device key](#device-key) it sent, or is given a
[Registration credential](#registration-credential) when it sent none. The service stores it by
hash and records who issued it, when it expires, and whether it has been redeemed.
Redeeming it is the one transition, and the code is burned before the new
[Device](#device) is added.

## Change Precedence

```meta
type: domain-service
aliases: [TaskChangePrecedence, AnnotationChangePrecedence, DevbookSubjectChangePrecedence, last-write-wins]
related: [.devbook/domain/sync/domain.invariants.md#change-precedence, .devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md, .devbook/arc42/adr/0011-devbook-annotations-are-a-third-replica-container.md, .devbook/arc42/adr/0044-devbook-subject-state-lives-in-backlogs-own-store.md, .devbook/domain/tasks/requirements.md#requirement-the-later-edit-wins-whole]
```

Decides whether a pushed copy of a document may replace the copy the replica
holds. It compares the device stamp (`updatedAt`) and the tombstone (`deletedAt`)
of the two copies and nothing else. It never reads the payload, which is what lets
one rule serve documents whose meaning belongs to other contexts.

Every device pushes everything above its own watermark, and a document it received sits above that watermark exactly as one it edited does. Each
device therefore echoes every document it pulled, once, on its next push. Without
the rule that echo would replace what the replica learned in between: a
tombstone would come back as the live copy, and an edit would revert.

There are three copies of the rule: `TaskChangePrecedence` for the task feed,
`AnnotationChangePrecedence` for the annotation feed, and
`DevbookSubjectChangePrecedence` for the devbook subject feed. The change records
share a shape and not a type, and the rule is four lines (local ADRs 0011 and
0044). The devbook subject copy compares the stamp alone, because a subject's
state is never deleted and has no tombstone. Both replica adapters of a feed, the
in-memory one and the Cosmos one, call the same copy, so they cannot disagree
about what a stale push is. Session records need no precedence: each is
written by one machine only (local ADR 0005 §Session records).

Invocation semantics: command-invoked, by a push, once per document.

## Shared Value Objects

```meta
type: shared-value-objects
related: [.devbook/domain/sync/domain.invariants.md#shared-value-objects]
```

### OwnerId

```meta
type: value-object
aliases: [ownerId, owner id]
related: [.devbook/domain/sync/domain.md#owner]
```

The identifier of an [Owner](#owner). A deployment that configures `Owner:OwnerId`
uses that GUID. Otherwise the sync service derives a name-based (version 8) GUID from
the owner's GitHub user id, the same on every start. Only a Development run with no
owner configured mints a version 7 one. It is a type of its own rather than a bare GUID. An owner id and a
device id travel together through every call, and swapping them would not fail to
compile. It is the partition key of every replica container. Two are equal when their
GUIDs are equal.

### DeviceId

```meta
type: value-object
aliases: [deviceId, device id, machine id]
```

The identifier the sync service issues a [Device](#device) at registration or
pairing. It names the caller and grants nothing on its own. A session record is
stamped with it as the machine that wrote it. It is not the shared kernel's
installation identity, and the desktop maps one onto the other for its own records.

### OwnerScope

```meta
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
type: value-object
aliases: [since cursor, sync cursor]
```

An owner and a store continuation that the service has verified belong together.
The service hands a client a signed cursor after each pull, and the client sends it
back as `since`. The codec (`HmacSyncCursorCodec`) checks the signature and the
owner before it produces a `SyncCursor`. A store continuation embeds the partition
it was minted for, so an unchecked one replayed by another owner would read that
owner's data. The store would not object.

### Replica Cursor

```meta
type: value-object
aliases: [TaskReplicaCursor, SessionReplicaCursor, AnnotationReplicaCursor, DevbookSubjectReplicaCursor]
related: [.devbook/domain/sync/domain.md#synccursor]
```

A verified position in one owner's task, session, annotation or devbook subject feed: an
[OwnerId](#ownerid) and a continuation. There is one type per feed, so a
continuation for one feed cannot be handed to another feed's replica. No
replica port takes a bare string: the service reads each from a verified
[SyncCursor](#synccursor). A page read from a feed always carries the cursor after it, even when the
page is empty.

### Session Record Key

```meta
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
type: ubiquitous-language
```

### Owner

```meta
type: term
aliases: [the owner's backlog]
related: [.devbook/domain/sync/domain.md#ownerid, .devbook/domain/sync/features.md#syncing-between-devices, .devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md#identity, .devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md#1-the-owner-is-one-github-account-matched-by-its-numeric-user-id]
```

The backlog an [OwnerId](#ownerid) names, held by one GitHub account matched by its
numeric user id. The owner signs in only to enrol the first device and to recover. A
recovery under that owner revokes its other devices. The local app still asks for no
sign-in, and every device paired afterward registers under the same owner. The sync service reads it out of a device's presented
[Device token](#device-token) to decide which partition a call may touch.

It is the partition each stored document and file carries:

- a task;
- a session record;
- an annotation;
- a devbook subject state;
- an attachment.

### Registration credential

```meta
type: term
aliases: [device credential, credential]
related: [.devbook/domain/sync/features.md#pairing-a-device, .devbook/domain/sync/domain.md#device-token, .devbook/domain/sync/domain.md#device, .devbook/domain/sync/domain.md#device-key, .devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md#2-a-device-proves-itself-with-a-non-exportable-key]
```

The long-lived secret a device receives at registration or pairing only when it
brings no [Device key](#device-key). It
[is shown once](requirements.md#requirement-the-registration-credential-is-shown-once),
and the device keeps it in its own OS credential store. Two kinds of device hold one:

- a device on a host with no key store, which today is the mobile web harness;
- a device paired before local ADR 0058 §2 gave devices keys.

A device on a host with a key store moves onto its key at its next token fetch. It
sends the credential, its public key, and an assertion that key signed. Then the
service and the device both drop the credential
([A device on a credential moves onto its key once](requirements.md#requirement-a-device-on-a-credential-moves-onto-its-key-once)).

### Device key

```meta
type: term
aliases: [key-bound device, non-exportable key, public key]
related: [.devbook/domain/sync/domain.md#device, .devbook/domain/sync/domain.md#device-assertion, .devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md#2-a-device-proves-itself-with-a-non-exportable-key]
```

A P-256 key pair a device generates, whose private half cannot be exported. A device
that has one is key-bound. The key is kept:

- on Windows, in CNG, through the TPM's Platform Crypto Provider, or as a
  non-exportable software key where no TPM is usable (`CngDeviceKeyStore`);
- on Android, in Android Keystore, in StrongBox where the phone has one
  (`AndroidKeystoreDeviceKeyStore`).

The service stores only the public half. The device sends it when it enrols or
pairs, or binds it once when it moves off a
[Registration credential](#registration-credential). A key that exists but will
not open is reported unavailable and never replaced (`SyncTokenProvider`).

**Why:** a copied app folder holds no secret, so it cannot sync from another
machine (local ADR 0058 §2).

### Device assertion

```meta
type: term
aliases: [client assertion, private_key_jwt]
related: [.devbook/domain/sync/domain.md#device-key, .devbook/domain/sync/domain.md#device-token, .devbook/domain/sync/domain.invariants.md#device]
```

The short-lived `private_key_jwt` assertion (RFC 7523) a key-bound device signs
with its [Device key](#device-key) for each token fetch. It is an ES256 JWT
whose issuer and subject are the device and whose audience is
`backlog-sync/devices/token`. It carries an assertion id (`jti`) and lives at most
five minutes (`SyncAssertion`). The service checks the signature before any claim
and accepts each assertion once
([its invariants](domain.invariants.md#device)).

### Device token

```meta
type: term
aliases: [access token, device session token, sync token]
related: [.devbook/domain/sync/domain.md#registration-credential, .devbook/domain/sync/domain.md#device-assertion, .devbook/domain/sync/domain.md#owner, .devbook/arc42/adr/guidelines/0012-authentication-external-identity-providers.md]
```

The short-lived credential a device actually presents on a sync call. It lasts
thirty minutes unless the deployment sets a lifetime between 15 and 60, and
[an expired one is refused](requirements.md#requirement-a-device-token-expires).
A key-bound device obtains it by signing a fresh
[Device assertion](#device-assertion), and a device without a key by presenting
its [Registration credential](#registration-credential). Either way, the
long-lived proof never rides a sync call. It names the device and
the [Owner](#owner) it belongs to, and
[every sync call is scoped to that owner](requirements.md#requirement-every-sync-call-is-scoped-to-the-tokens-owner).
