# Sessions

```meta
type: domain
status: active
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context; each chapter's
> `type` records which of those it is. An Aggregate's owned Entities, Value
> Objects, and Enums are chapters directly beneath it, typed `entity`,
> `value-object`, and `enum`. Value Objects/Enums shared across multiple
> aggregates get their own chapter at the end instead of being duplicated.

The Sessions context owns the record of what the AI coding agents have been doing:
which sessions an environment has run, which agent ran each one, where it worked,
when it was last active, and whether it is still going. It owns no work item, no machine
and no repository — every one of those belongs to a supplier context, and this one
only holds the identifier it was given.

It is a **read model over evidence somebody else wrote.** The agents leave records
on the environments they run on; this context reads them and says what they mean. It
never starts, stops or names a session, which is why nothing here is a command and
why the aggregate below has no invariant about state transitions — a session's state
is derived on every reading, not advanced.

An optional Collections MCP can add a second kind of evidence: sanitized activity
updates that a configured session reports as it moves. Those updates are an
**enrichment layer**, not a second authority. They say more about what a known
session was doing; they do not decide that a session existed in the first place.

Evidence can also **travel between the person's own machines**, so the environment
that ran a session need not be the one they are sitting at. That is replication,
not a third kind of evidence: a record read on the second machine is still the
record the first machine wrote. See
`.arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md`.

This subject was first modelled inside
[Dev PC Management](../dev-pc-management/domain.md#machine-registry) as
Copilot Session Tracking on the Machine, when Copilot was the only agent the
machines ran. Two agents run on them now, and "which agent, in which repository, for
how long" is a different question in a different language from "how is this PC
configured" — so the subject moved out to here and Dev PC Management stopped
modelling it.

## Session Log

```meta
type: aggregate
status: active
related: [.domain/dev-pc-management/domain.md#machine-registry, .domain/sessions/dependencies.md, .arc42/08-crosscutting-concepts.md#session-record-sync, .arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md]
```

Everything one environment can say about the agent sessions it has a record of.

The consistency boundary is **one environment**, not one fleet: an environment is
the only thing that can read its own agents' records, so a log that spanned several
would be asserting facts nobody had gathered. A view over many environments is a
composition of several Session Logs, which is why grouping by environment is a
derivation rather than a structural relationship.

Invariants:

- A session appears **once**, however many records the environment holds for it. An
  agent may leave one record per process and another per transcript; those are
  evidence of one session, not two.
- A session's `Session State` is **derived from the evidence available**, never
  asserted. Running and Stalled both require liveness evidence; with none, a session
  is Finished. A state no evidence supports is not reported at all — there is no
  Failed here, because a record that stops says nothing about why.
- The log **never fills a gap the agent left**. A fact an agent did not record is
  absent, not inferred: a repository guessed from a folder path is indistinguishable
  from a recorded one and wrong.
- The log **says how complete it is**. It may describe fewer sessions than exist, and
  when it does, how many exist is part of the answer rather than a detail the reader
  is left to discover.

**Records replicate; none of the above changes when they do.** Local ADR 0005 puts
session records in the same cloud replica as tasks, so a record gathered on one
machine can be read on another. Every invariant survives intact, because the thing
that replicates is a record, not an authority:

- **Still single-writer.** A session ran on one environment and only that
  environment holds the evidence for it, so only that environment writes records
  for it. There is nothing to reconcile: no second version, no last-write-wins,
  and no lost-edit failure mode. A session that moves gets a later record rather
  than an edit to an earlier one, which is the shape this context already has.
- **Still one environment per log.** A reading that spans machines is a
  composition of several Session Logs, exactly as grouping by environment already
  is. A replicated record names the environment that gathered it, so nothing
  asserts a fact nobody gathered.
- **Still derived, never asserted.** `Session State` is worked out on every
  reading from the evidence that reached the reader. A record whose environment is
  a machine away is stale rather than wrong, and the derivation says so by reading
  `stalled` or `finished` from the timestamps it has.
- **Only a whitelist travels**, fixed by local ADR 0005: the session id, the
  environment, the repository **alias** rather than the working folder, the
  branch, the activity window, and the turn and duration counts. Never prompts,
  never tool output, never file contents — which is the whole reason a record can
  leave the machine at all. A field not on the list does not travel, so
  `working_folder` does not: it describes one machine's disk and means nothing on
  the other.
- **The list is one field short of this context's identity, and that has to be
  settled where the list lives.** `Session Identity` is `agent` plus `session_id`,
  never `session_id` alone, because two agents may issue the same string; a record
  that travelled without its agent would let the receiving log merge two unrelated
  sessions — exactly the failure the identity rule exists to prevent. Widening the
  whitelist is a decision for local ADR 0005 rather than something the pushing code
  settles, so it is named here as an open point rather than assumed.
- **Retention is the store's, not this context's.** A replicated record expires
  after twelve months by container TTL. Nothing here reaps, and nothing here
  deletes a record.

**Replication is not the Collections MCP and changes nothing about it.** The MCP
stays exactly what it already is: optional, reached through an anti-corruption
layer, never authoritative for whether a session existed. It supplies a second
kind of evidence about a session this context already knows; replication moves
records this context already holds. Neither stands in for the other, and a
replicated record with no activity stream is a perfectly good record.

### Agent Session

```meta
type: entity
status: active
```

One working session of one agent. Identified by `Session Identity` — the agent plus
the identifier that agent gave the session — because a session id is only unique
within the agent that issued it.

Holds the environment it ran on, a display title, its `Working Location`, its
`Activity Window`, and its `Session State`. When external activity evidence exists,
the same entity also carries a `Session Activity Stream`, a `Session Enrichment
Summary`, and a `Reporting Capability` reading for that stream.

Its lifecycle is not this context's to run: it appears when an agent first leaves a
record, and it stops changing when the agent stops writing.

### Session Identity

```meta
type: value-object
status: active
```

What makes a session that session: `agent` plus `session_id`. Equality by both.
Never by `session_id` alone — two agents may issue the same string, and an
environment reading both would silently merge two unrelated sessions.

### Working Location

```meta
type: value-object
status: active
```

Where the session was working: `working_folder`, optional `repository` in
`owner/name` form, and optional `branch`. Equality by all three.

Both optional fields are optional because the agents disagree about what they
record, not because the value is unimportant: one writes the repository and branch
outright, the other writes neither. Absent means "the agent did not say".

### Activity Window

```meta
type: value-object
status: active
```

When the session was alive: optional `started_at` and a required `last_activity_at`.
Equality by both. The start is optional because some evidence is only a file with a
timestamp on it; the last activity is not, because that timestamp always exists and
is what `Liveness Assessment` reads.

### Session Activity Stream

```meta
type: value-object
status: active
```

Optional externally reported activity about one known session, ordered as that
session reported it.

The stream is keyed by `Session Identity`, not by a collection row id, because the
context cares which session the activity belongs to and not how the storage happened
to key it. It is optional because a session without Collections MCP reporting is
still a perfectly good session record.

### Session Activity Entry

```meta
type: value-object
status: active
```

One externally reported milestone about a session: what kind of activity it was, when
it happened, its per-session sequence, and a short summary fit to be shown back to a
person.

Identity inside the stream is by external `event_id`; order is by the session's own
sequence and then by occurrence time. Both are needed: duplicates must collapse, and
late arrivals must still land in the right place.

### Session Enrichment Summary

```meta
type: value-object
status: active
```

The one-line answer derived from a `Session Activity Stream`: the latest meaningful
activity text, when external activity last arrived, and what correlation facts can be
shown beside the base session row.

It is a value object because it is a reading over the stream, not a second entity. A
new activity entry replaces the summary by deriving a later one rather than by
editing a stored record.

### Session Catalog

```meta
type: value-object
status: active
```

What a reading of one environment answers with: the `Agent Session` list it will
describe, the sources it could **not** read, by name, and how many sessions it
discovered before any limit was applied.

Three parts rather than one list, because "no sessions" and "could not look" are
different answers and a reader who cannot tell them apart will go looking for a
fault that is not there. Equality is by all three.

### Agent

```meta
type: enum
status: active
```

Which assistant ran the session: `claude`, `copilot`.

A type on the session rather than a list per vendor. A list per vendor would make
"what has been running here" unanswerable without first knowing how many vendors
there are.

### Session State

```meta
type: enum
status: active
```

How far along a session is, as far as the evidence goes:

- `running` — the agent is present on the environment and something moved recently.
- `stalled` — still registered as present, but nothing has moved for longer than the
  `Stale Threshold`. A left-open window, usually.
- `finished` — over. Only its record is left.

Three values, and deliberately not five. Starting, waiting and failed are all
states an agent could be in and none is derivable from what it leaves behind, so
this context does not claim them.

### Reporting Capability

```meta
type: enum
status: active
```

Whether a session can report activity to the optional Collections MCP:

- `enabled` — the capability is configured and reachable enough to accept updates.
- `disabled` — no Collections MCP reporting is configured for this session or environment.
- `degraded` — reporting is configured, but delivery is currently failing or the MCP
  cannot be reached.

This is about the reporting path and not about the session's own health. A running
session can have degraded reporting, and a finished session can still have an enabled
path that already delivered its last update.

## Liveness Assessment

```meta
type: domain-service
status: active
related: [.domain/sessions/domain.md#session-state, .domain/sessions/flow.md]
```

Decides whether a session with liveness evidence is `running` or `stalled`, by
comparing its `last_activity_at` against the `Stale Threshold`.

A service rather than a property of the session, because the answer depends on the
current time and an `Agent Session` holds no clock. That is also why the threshold is
this context's and not a rendering concern: "how long is too long" is a policy, and a
policy that lived in a control library would be a product rule in the one place
nobody would look for it.

Invocation semantics: query-oriented; evaluated per reading, never persisted. A
session is not moved into `stalled` by anything — it simply reads as stalled while
the silence lasts, and reads as running again the moment the agent writes.

## Session Grouping

```meta
type: domain-service
status: active
related: [.domain/sessions/domain.md#session-log, .domain/sessions/features.md#session-grouping]
```

Carves a set of `Agent Session`s into named groups — one per environment, or one per
agent — each ordered most recently active first, with the groups themselves in a
stable order.

A service because grouping spans sessions rather than belonging to any one of them,
and a pure one: no clock, no I/O, no state. Two properties it guarantees, both of
which a reader relies on without being told:

- **Grouping rearranges and never filters.** Every session in, every session out. A
  count taken before grouping is still correct after it.
- **Group order does not depend on group size.** Environments sort by name and agents
  in the order the `Agent` enum declares them, so a section does not move under the
  reader as sessions come and go.

Invocation semantics: query/composition-oriented; invoked per view, never stored.
Which grouping is in force is the reader's choice and is not part of this context's
state.

## Session Activity Publishing

```meta
type: domain-service
status: proposed
related: [.domain/sessions/features.md#session-activity-enrichment, .domain/sessions/flow.md, .domain/sessions/dependencies.md]
```

Emits sanitized activity updates for a running session to the optional Collections
MCP when that session has reporting configured and reaches a meaningful milestone.

The service publishes the beginning of a session, meaningful activity deltas while it
runs, and a final update when it finishes. It never publishes prompts, transcripts,
tool output bodies, or secrets; it publishes only the coarse facts this context is
willing to read back later as activity evidence.

Invocation semantics: event-triggered policy/process-manager behavior; invoked from
session milestones, queued asynchronously, retried without blocking session work, and
allowed to degrade to local-only behavior when reporting is unavailable.

## Session Activity Enrichment

```meta
type: domain-service
status: proposed
related: [.domain/sessions/features.md#session-activity-enrichment, .domain/sessions/flow.md, .domain/sessions/dependencies.md]
```

Layers optional `Session Activity Stream` evidence onto the locally read `Agent
Session` so the session row can say more than the local record alone can say.

The service de-duplicates entries by external `event_id`, orders them by per-session
sequence and occurrence time, and derives the `Session Enrichment Summary` and
`Reporting Capability` that belong on the session. Local evidence stays authoritative
for whether the session exists, where it worked, and what `Session State` it reads as;
external activity enriches and never replaces those facts.

Invocation semantics: query/composition-oriented; evaluated per reading when external
activity evidence exists, and skipped entirely when it does not.

## Shared Value Objects

```meta
type: shared-value-objects
status: active
```

> Value Objects used by more than one aggregate in this bounded context.

Sessions has a single aggregate; every value object is documented under it.
This chapter is reserved for the day a second aggregate arrives — a fleet-level view
would be the likely first one.

## Shared Enums

```meta
type: shared-enums
status: active
```

> Enums used by more than one aggregate in this bounded context.

Single aggregate, so `Agent` and `Session State` are documented under it. Reserved
for the same reason as the chapter above.
