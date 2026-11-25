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
related: [.devbook/domain/sync/domain.invariants.md#device, .devbook/domain/sync/features.md#pairing-a-device, .devbook/domain/sync/requirements.md#requirement-a-key-bound-device-is-taken-only-on-an-assertion-its-key-signed, .devbook/domain/tasks/domain.md#device]
```

One machine's registration with the sync service. It holds:

- the machine's [DeviceId](#deviceid);
- the [OwnerId](#ownerid) whose data it may see;
- the name a person calls it in the device list;
- the public half of its [Device key](#device-key), or, on a device not yet
  bound to one, a hash of its [Registration credential](#registration-credential).

It holds one proof or the other, never both. Neither is a secret on the service,
so a copy of the record is not a copy of one.

A device comes in once, by registering as the first device of a new owner or by
redeeming a [Pairing Code](#pairing-code). A paired device also names the device
whose code it redeemed, so the desktop that issued the code can ask its person
whether to keep it. It is the consistency boundary for "who is this caller": a
token is minted only for a device that signs a [Device assertion](#device-assertion)
with the key on file, or, without a key, presents the credential its stored hash
matches. The device whose code paired it can revoke it. Its proof then mints no
further token, and a token it already holds lasts until it expires.

A device on a credential moves onto a key once, at a token fetch, and the binding
drops its credential hash. Its credential path is closed from then on (local
[ADR 0058 §2](../../arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md#2-a-device-proves-itself-with-a-non-exportable-key)).

It is not the machine. The machine is the shared kernel's
[Device](../tasks/domain.md#device), which every context shares. This aggregate is
that machine as the sync service knows it, under an id the service issued.

## Pairing Code

```meta
type: aggregate
aliases: [PairingCode, pairing code, XXXX-XXXX code]
related: [.devbook/domain/sync/domain.invariants.md#pairing-code, .devbook/domain/sync/features.md#pairing-a-device, .devbook/domain/sync/requirements.md#pairing-a-device]
```

A short, single-use code a paired device issues so a second device can join its
[Owner](#owner). It has eight characters from an alphabet that drops visually confusable
letters and digits (`PairingCodeFormat`), and the person sees it as `XXXX-XXXX`. It
[works for ten minutes](requirements.md#requirement-a-pairing-code-works-for-ten-minutes)
and [pairs one device](requirements.md#requirement-a-pairing-code-pairs-one-device),
so a code that leaks unused stops being a way in on its own.

It is an invitation, not a credential: it authorizes one act, adding a device
with its key or a new registration credential, and only within its window. The service stores it by
hash and records who issued it, when it expires, and whether it has been redeemed.
Redeeming it is the one transition, and the code is burned before the new
[Device](#device) is added.

## Enrolment Ticket

```meta
type: aggregate
aliases: [EnrolmentTicket, enrolment code, owner sign-in]
related: [.devbook/domain/sync/domain.md#owner, .devbook/domain/sync/requirements.md#requirement-an-unpinned-deployed-service-enrols-no-device-beside-another-owners, .devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md#1-the-owner-is-one-github-account-matched-by-its-numeric-user-id]
```

One step of the [Owner](#owner)'s GitHub sign-in, which enrols the first device or
recovers one. A `SignIn` ticket stands for a sign-in that left for GitHub, and a
`Code` ticket for the one-time code the sign-in handed back to the device's
loopback. Each is stored by the hash of its state or code, beside the device's S256
challenge and loopback redirect, and expires within minutes.

A code authorizes one registration, and only with the verifier behind its challenge
(`enrolment.verifier_mismatch`). Like a [Pairing Code](#pairing-code) it is an
invitation, not a credential, and the registration it allows burns it.

## Connector Client

```meta
type: aggregate
aliases: [ConnectorClient, claude-connector, remote connector, connector secret]
related: [.devbook/domain/sync/domain.invariants.md#connector-client, .devbook/domain/sync/features.md#capturing-from-claudeai, .devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md#8-a-remote-agent-reaches-backlog-through-oauth-21-on-the-sync-service]
```

The one OAuth client the sync service's authorization server knows: claude.ai's
custom connector, `claude-connector`. It is registered statically, so no other
client can appear. The owner's desktop mints its secret, which the service shows
once and keeps only as a hash, and can revoke it.

Its generation moves on every new secret and every revoke. Each
[Connector Authorization](#connector-authorization),
[Connector Refresh Token](#connector-refresh-token) and
[Connector token](#connector-token) carries the generation it was issued under, so a
new secret or a revoke kills all of them without a query. The client, its
authorizations and its refresh tokens live in the `connectorCredentials` container.

## Connector Authorization

```meta
type: aggregate
aliases: [ConnectorAuthorization, authorization code, consent ticket]
related: [.devbook/domain/sync/domain.invariants.md#connector-authorization, .devbook/domain/sync/features.md#capturing-from-claudeai]
```

One run of claude.ai's authorization code grant, step by step:

- `SignIn`: an authorize request that left for GitHub;
- `Consent`: the owner signed in and the consent page is showing;
- `Code`: the owner allowed it, and a code is on its way to claude.ai.

Each step is stored by the hash of its state, ticket or code, beside claude.ai's S256
challenge, its redirect, the scope asked for and the `/mcp` resource the token is
for. It lives minutes and is burned once. Only the owner's GitHub account gets past
`SignIn`; any other account goes back to claude.ai as `access_denied`. A code starts
a refresh-token family, so a code used twice can revoke what its first use issued.

## Connector Refresh Token

```meta
type: aggregate
aliases: [ConnectorRefreshToken, refresh token, token family]
related: [.devbook/domain/sync/domain.invariants.md#connector-refresh-token, .devbook/domain/sync/requirements.md#requirement-a-refresh-token-works-once]
```

What lets claude.ai get a new [Connector token](#connector-token) without the owner
signing in again. It is stored by hash, lasts thirty days, and rotates on use: the
answer to a refresh carries its replacement, and the presented token stops working.
Every token of one authorization shares a family, so a rotated token presented
again is a replay, and the whole family is revoked.

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

### Device key

```meta
type: term
aliases: [device signing key, non-exportable key, hardware-bound key]
related: [.devbook/domain/sync/domain.md#device-assertion, .devbook/domain/sync/domain.md#device, .devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md#2-a-device-proves-itself-with-a-non-exportable-key]
```

The P-256 key pair a device makes for itself, whose private half cannot be exported:
in the TPM through CNG on Windows, in Android Keystore on the phone. The service
gets only the public half, at registration, at pairing, or when the device moves off
its [Registration credential](#registration-credential). A copy of the app's folder
on another machine has no key, so it cannot sync.

### Device assertion

```meta
type: term
aliases: [private_key_jwt, client assertion, assertion]
related: [.devbook/domain/sync/domain.md#device-key, .devbook/domain/sync/domain.md#device-token, .devbook/domain/sync/domain.invariants.md#invariant-an-assertion-is-taken-once]
```

The `private_key_jwt` (RFC 7523) a device signs with its [Device key](#device-key)
and trades for a [Device token](#device-token). It names the device as issuer and
subject and the token exchange as audience, carries a random id, and lives at most
five minutes. The service takes each one once.

### Registration credential

```meta
type: term
aliases: [device credential, credential]
related: [.devbook/domain/sync/features.md#pairing-a-device, .devbook/domain/sync/domain.md#device-token, .devbook/domain/sync/domain.md#device, .devbook/domain/sync/domain.md#device-key]
```

The long-lived secret a device without a [Device key](#device-key) receives at first
registration or at pairing, and
[is shown once](requirements.md#requirement-the-registration-credential-is-shown-once).
The device keeps it in its own OS credential store rather than anywhere the backlog
itself is stored, and exchanges it for a [Device token](#device-token) instead of
presenting it on a sync call. A device that moves onto a key deletes it, and the
service drops its hash.

### Device token

```meta
type: term
aliases: [access token, device session token, sync token]
related: [.devbook/domain/sync/domain.md#device-assertion, .devbook/domain/sync/domain.md#registration-credential, .devbook/domain/sync/domain.md#owner, .devbook/arc42/adr/guidelines/0012-authentication-external-identity-providers.md]
```

The short-lived credential a device actually presents on a sync call. It lasts
thirty minutes unless the deployment sets a lifetime between 15 and 60, and
[an expired one is refused](requirements.md#requirement-a-device-token-expires).
The device obtains it with a [Device assertion](#device-assertion), or, without a
key, by exchanging its [Registration credential](#registration-credential). It names
the device and the [Owner](#owner) it belongs to, and
[every sync call is scoped to that owner](requirements.md#requirement-every-sync-call-is-scoped-to-the-tokens-owner).
`/mcp` refuses it.

### Connector token

```meta
type: term
aliases: [connector access token, remote MCP token]
related: [.devbook/domain/sync/domain.md#connector-client, .devbook/domain/sync/requirements.md#requirement-a-connector-token-reaches-only-mcp]
```

The access token the authorization server issues claude.ai for the owner. It lasts
fifteen minutes, its audience is the sync service's `/mcp` resource, and it carries
the scopes granted, starting with `inbox:capture`, and the
[Connector Client](#connector-client)'s generation. It is signed with a key of its
own, so the sync API refuses it as a [Device token](#device-token).
