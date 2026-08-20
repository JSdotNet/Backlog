# Domain: Sessions

```meta
status: active
order: ["features.md", "model.md", "flow.md", "dependencies.md", "naming.md"]
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context.

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

This subject was first modelled inside
[Dev PC Management](../dev-pc-management/domain.md#aggregate-machine-registry) as
Copilot Session Tracking on the Machine, when Copilot was the only agent the
machines ran. Two agents run on them now, and "which agent, in which repository, for
how long" is a different question in a different language from "how is this PC
configured" — so the subject moved out to here and Dev PC Management stopped
modelling it.

## Aggregate: Session Log

```meta
status: active
related: [.domain/dev-pc-management/domain.md#aggregate-machine-registry, .domain/sessions/dependencies.md]
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

### Entities

#### Agent Session

One working session of one agent. Identified by `Session Identity` — the agent plus
the identifier that agent gave the session — because a session id is only unique
within the agent that issued it.

Holds the environment it ran on, a display title, its `Working Location`, its
`Activity Window`, and its `Session State`. Its lifecycle is not this context's to
run: it appears when an agent first leaves a record, and it stops changing when the
agent stops writing.

### Value Objects

#### Session Identity

What makes a session that session: `agent` plus `session_id`. Equality by both.
Never by `session_id` alone — two agents may issue the same string, and an
environment reading both would silently merge two unrelated sessions.

#### Working Location

Where the session was working: `working_folder`, optional `repository` in
`owner/name` form, and optional `branch`. Equality by all three.

Both optional fields are optional because the agents disagree about what they
record, not because the value is unimportant: one writes the repository and branch
outright, the other writes neither. Absent means "the agent did not say".

#### Activity Window

When the session was alive: optional `started_at` and a required `last_activity_at`.
Equality by both. The start is optional because some evidence is only a file with a
timestamp on it; the last activity is not, because that timestamp always exists and
is what `Liveness Assessment` reads.

#### Session Catalog

What a reading of one environment answers with: the `Agent Session` list it will
describe, the sources it could **not** read, by name, and how many sessions it
discovered before any limit was applied.

Three parts rather than one list, because "no sessions" and "could not look" are
different answers and a reader who cannot tell them apart will go looking for a
fault that is not there. Equality is by all three.

### Enums

#### Agent

Which assistant ran the session: `claude`, `copilot`.

A type on the session rather than a list per vendor. A list per vendor would make
"what has been running here" unanswerable without first knowing how many vendors
there are.

#### Session State

How far along a session is, as far as the evidence goes:

- `running` — the agent is present on the environment and something moved recently.
- `stalled` — still registered as present, but nothing has moved for longer than the
  `Stale Threshold`. A left-open window, usually.
- `finished` — over. Only its record is left.

Three values, and deliberately not five. Starting, waiting and failed are all
states an agent could be in and none is derivable from what it leaves behind, so
this context does not claim them.

## Domain Service: Liveness Assessment

```meta
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

## Domain Service: Session Grouping

```meta
status: active
related: [.domain/sessions/domain.md#aggregate-session-log, .domain/sessions/features.md#feature-session-grouping]
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

## Shared Value Objects

```meta
status: active
```

> Value Objects used by more than one aggregate in this bounded context.

Sessions has a single aggregate; every value object is documented under it.
This chapter is reserved for the day a second aggregate arrives — a fleet-level view
would be the likely first one.

## Shared Enums

```meta
status: active
```

> Enums used by more than one aggregate in this bounded context.

Single aggregate, so `Agent` and `Session State` are documented under it. Reserved
for the same reason as the chapter above.
