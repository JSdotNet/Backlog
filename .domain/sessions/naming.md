# Sessions

```meta
type: naming
status: active
```

> Canonical ubiquitous-language terms for this bounded context and their aliases.
> Each term links to where it is modeled (related); surface names it is also known by
> are recorded in the aliases metadata field so any synonym resolves back to one
> canonical concept.

## Agent Session

```meta
type: term
status: active
aliases: [AgentSession, session, session_id]
related: [.domain/sessions/domain.md#agent-session]
```

One working session of one AI coding agent on one environment: when it started, where
it worked, when it was last active, and how far along it is.

"Session" alone is the everyday word and is safe inside this context. Outside it,
say **agent session** — Dev PC Management also has a *remote desktop session*, and the
bare word does not distinguish an agent working in a repository from a person
connecting to a PC.

`session_id` is the agent's own identifier for it and is only half of the identity;
see [Session Identity](#session-identity).

## Agent

```meta
type: term
status: active
aliases: [AgentSessionKind, agent, agent_kind]
related: [.domain/sessions/domain.md#agent]
```

The AI coding assistant that ran a session: Claude or GitHub Copilot.

Deliberately **not** "Copilot", which is what this subject was called while Copilot
was the only one. A vendor name used as the category name is what made the earlier
model unable to describe the second vendor without a parallel list.

## Session Identity

```meta
type: term
status: active
aliases: [SessionIdentity]
related: [.domain/sessions/domain.md#session-identity]
```

What makes a session that session: the agent plus the identifier that agent issued.

Both halves, always. A session id is unique only within its own agent, so an
environment that keyed on the id alone could merge two unrelated sessions and would
show one row where there were two.

## Environment

```meta
type: term
status: active
aliases: [environment, machine_name]
related: [.domain/sessions/domain.md#session-log, .domain/environment/naming.md#environment]
```

Where an agent ran. Today that is a development PC, named as that machine names
itself.

**This is not the Environment context's Environment.** That one is a launchable
destination — a local harness, a staging app, a cloud dashboard — and belongs to a
person's shortcuts. This one is the place a session happened. The words collide, the
concepts do not, and the `related` link above exists so that a reader who lands on
either finds the other rather than assuming they are the same thing.

It is also not Dev PC Management's **Machine**, though today every environment is
one. Machine is a registered, wakeable, compliance-tracked box; environment is
wherever an agent can run, and nothing in this model stops that being a container or
a hosted runner later. Where the two name the same box, that is a lookup — see
`dependencies.md`.

## Session State

```meta
type: term
status: active
aliases: [AgentSessionState, state]
related: [.domain/sessions/domain.md#session-state]
```

How far along a session is, as far as the evidence goes: `running`, `stalled`, or
`finished`.

Read on every reading rather than stored. "Stalled" in particular is not a thing that
happens to a session — it is what a running session reads as while its silence lasts.

## Stale Threshold

```meta
type: term
status: active
aliases: [StaleAfter]
related: [.domain/sessions/domain.md#liveness-assessment]
```

How long a session with liveness evidence may be silent before it reads as `stalled`
rather than `running`.

A policy, and therefore this context's rather than a rendering detail. The control
library that draws a stalled chip refuses to own it for exactly that reason: deciding
"how long is too long" needs a clock and a product opinion, and neither belongs in a
component.

## Working Location

```meta
type: term
status: active
aliases: [WorkingLocation, cwd, working_folder]
related: [.domain/sessions/domain.md#working-location]
```

Where a session was working: the folder, and — when its agent recorded them — the
repository in `owner/name` form and the branch.

`cwd` is what both agents call the folder in what they write. The repository and
branch are absent rather than empty when the agent did not record them: "not
recorded" is a fact, and inferring either from the folder would produce a claim
nothing supports.

## Session Activity Stream

```meta
type: term
status: proposed
aliases: [SessionActivityStream, activity stream, session updates]
related: [.domain/sessions/domain.md#session-activity-stream, .domain/sessions/features.md#session-activity-enrichment]
```

The ordered external activity a configured session reported about itself through the
Collections MCP.

Stream rather than log because it is read as a sequence attached to one known session,
not as the complete system log of everything all sessions did. It enriches an
`Agent Session`; it does not replace one.

## Session Activity Entry

```meta
type: term
status: proposed
aliases: [SessionActivityEntry, event_id, sequence, activity summary]
related: [.domain/sessions/domain.md#session-activity-entry]
```

One milestone in a `Session Activity Stream`: what happened, when, in what order, and
how to summarize it back to a person.

`event_id` names the entry for de-duplication; `sequence` places it within its own
session. The two are deliberately not synonyms.

## Session Enrichment Summary

```meta
type: term
status: proposed
aliases: [SessionEnrichmentSummary, latest activity, enrichment]
related: [.domain/sessions/domain.md#session-enrichment-summary]
```

The one-line answer derived from the activity stream: the latest meaningful summary,
when external activity last arrived, and the safe correlation facts worth showing.

Summary rather than status because it does not say whether the session is running,
stalled, or finished; `Session State` already does that. This term says what the
session was doing, not whether it was alive.

## Collections MCP

```meta
type: term
status: proposed
aliases: [collections mcp, collection store, activity collection]
related: [.domain/sessions/dependencies.md]
```

The optional MCP-backed collection a session can report sanitized activity into and the
Sessions context can read back from.

It is a transport and storage capability, not the owner of session meaning. The
session model stays in this context even when the activity passes through that store.

## Session Catalog

```meta
type: term
status: active
aliases: [AgentSessionCatalog]
related: [.domain/sessions/domain.md#session-catalog]
```

What one reading of an environment answers with: the sessions it describes, the
sources it could not read, and how many sessions it discovered.

Three parts because "none found" and "could not look" are different answers, and
because a reading that stops at the most recent sessions has to say so.

## Session Limit

```meta
type: term
status: active
aliases: [AgentSessionLimits, PerAgent]
related: [.domain/sessions/features.md#say-how-much-was-left-out]
```

How many sessions per agent a reading will describe.

Per agent, not overall. An environment can hold hundreds of records and the agents do
not hold them in equal numbers — so a single overall limit would be filled by whichever
agent kept more history, and would quietly empty a list whose purpose is showing both.

A limit on how much is described, never a claim about how much exists: the number
discovered travels back beside the sessions, and a reading that dropped anything says
so.

## Reporting Capability

```meta
type: term
status: proposed
aliases: [reporting status, capability, enabled, degraded]
related: [.domain/sessions/domain.md#reporting-capability]
```

Whether the optional external reporting path is available for a session: `enabled`,
`disabled`, or `degraded`.

This is kept separate from `Session State` on purpose. A stalled session can still
have enabled reporting, and a running session can have degraded reporting.

## Session View

```meta
type: term
status: active
aliases: [AgentSessionView, view, live, all]
related: [.domain/sessions/domain.md#session-view, .domain/sessions/features.md#open-on-the-live-sessions]
```

Which of the sessions a reading described the reader wants in front of them: `live`, or
`all`.

**Live** and **past** are defined against `Session State` and against nothing else. A
session is live while it reads as `running` or `stalled`, and past once it reads as
`finished`. So "live" inherits every limit on the evidence behind that state and means
"nothing here says this has ended" rather than "this is certainly still going" —
which is the whole of the difference between the two agents, since only one of them
leaves a liveness marker to read.

A view, not a grouping. `Session Grouping` rearranges and carries every session it is
given; a view is the one choice in this context that takes rows away. That is why they
are two terms and two controls rather than a third way of grouping, and it is what the
surface's count is naming when it quotes two numbers instead of one.

Like the grouping, it is the reader's choice and is held by whatever is displaying the
list — not part of this context's state, and not part of what a reading returns.
Unlike a stored preference it outlives nothing: the surface holding it opens at `live`
every time, and re-reading the sessions leaves the choice exactly where the reader put
it.

## Session Grouping

```meta
type: term
status: active
aliases: [AgentSessionGrouping]
related: [.domain/sessions/domain.md#session-grouping]
```

How a reader has asked for the list to be carved up: not at all, by environment, or
by agent.

The reader's choice, held by whatever is displaying the list — not part of this
context's state, and never a filter: every grouping carries every session.
