# Sessions

```meta
type: features
status: active
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Session inventory

```meta
type: feature
status: active
related: [.domain/sessions/domain.md#session-log]
```

Answer "what have my agents been doing here" as one list: every session the
environment has a record of, most recently active first, whichever agent ran it.

One list rather than one per agent, because the question is about the work and not
about the vendor. A person who has both agents installed does not think in two
inventories.

### Live and past sessions together

```meta
type: sub-feature
status: active
related: [.domain/sessions/features.md#open-on-the-live-sessions]
```

A session that is running now and a session that finished last week are rows in the
same list, distinguished by their state rather than by which list they are in. Two
lists would force the reader to know which one to look in before knowing whether the
session had ended — which is usually the thing they are trying to find out.

The list **opens on part of itself**, and that is a smaller concession than splitting
it. One list narrowed by a control is still one list: the rows left out are one press
away under the same heading, the count beside the title names both numbers whenever
any are being left out, and the empty state the narrowing can produce says which
states it kept and how many sessions are on the other side. A reader looking for a
session has one place to look and one control to move.

What two lists would have cost is exactly what none of that costs. A finished session
is never *somewhere else* — only not currently shown, by a choice the reader can see
they made.

### Open on the live sessions

```meta
type: sub-feature
status: active
related: [.domain/sessions/domain.md#session-view, .domain/sessions/naming.md#session-view]
```

The list opens on the sessions that are still going, and a `Live` / `All` choice sits
first in the row of controls above it — before the grouping, because it decides which
rows exist before the grouping decides where they sit. "What is going on right now" is
the question somebody opens this for, and several hundred finished records are the
answer to a different one.

Live means running **or** stalled, both. Stalled is computed from silence rather than
declared by the agent — nothing writes "I have stopped" — so a stalled session is one
nobody can show has ended, and a default that dropped it would hide the row most worth
looking at: the one that may be sitting there waiting on the reader.

The choice is not remembered. Every open starts on `Live`, the way the grouping starts
at `Nothing`, because a reader should not have to remember having already answered
"what is going on right now" — and a view silently restored from last week is how a
reader concludes the product has lost their sessions.

**The default is only as good as the evidence under it, and for one agent that is
thin.** Claude leaves a record per running session, so its live rows are evidence.
Copilot leaves no liveness marker at all: its sessions read as running while their
record is fresh and as finished once it has been quiet for longer than the
`Stale Threshold`, and they never read as stalled. A Copilot session that is genuinely
running but has said nothing for half an hour therefore falls out of this view. That is
a limit of what Copilot writes down rather than a rule this product wanted, and it is
why the way back to `All` is named in the empty state instead of left to be discovered.

### Only what the agent recorded

```meta
type: sub-feature
status: active
related: [.domain/sessions/domain.md#working-location]
```

Each session shows what its own agent wrote down and no more. The agents disagree
about what they record — one names the repository and branch, the other names
neither — so a column is empty for one agent and filled for the other, and the empty
one is visibly "not recorded" rather than blank.

The alternative was inferring the missing facts from the working folder. A wrong
repository attributed to a session renders exactly as convincingly as a right one,
which is what makes inference the more expensive option.

### Re-read on request

```meta
type: sub-feature
status: active
```

The list is read when it is opened and again when the reader asks for it. Nothing
polls.

A surface that refreshed itself would be claiming to be live, and the evidence does
not support the claim: one of the two agents leaves no liveness marker at all, so a
self-moving list would move without meaning.

## Session grouping

```meta
type: feature
status: active
related: [.domain/sessions/domain.md#session-grouping]
```

Carve the same list up by the two things that separate sessions from each other in
practice, without removing any of them.

### Group by environment

```meta
type: sub-feature
status: active
related: [.domain/sessions/naming.md#environment]
```

A section per environment, named after it, with its own count — so "how many are
running on that box" is read rather than counted.

Single-valued for as long as an environment can only read its own records: one
section, named after the machine the reader is sitting at. It becomes the useful
grouping the moment a second environment reports.

### Group by agent

```meta
type: sub-feature
status: active
```

A section per agent, so Claude's sessions and Copilot's can be read separately
without the two being separate lists.

### Grouping never hides a session

```meta
type: sub-feature
status: active
related: [.domain/sessions/features.md#open-on-the-live-sessions]
```

The total is the same whichever grouping is chosen, and the surface keeps that total
on screen beside the title, above the controls that act on the list — so a grouping
can be trusted not to be a filter in disguise.

That count is what carries the guarantee, and it answers to the view and never to the
grouping: it does not move while `Group by` moves the same rows around. When the view
is leaving some of them out the count names both numbers — kept and total — so the
total is still there to be checked against, with the reader's own subtraction stated
beside it rather than folded into it. One number where two are owed would let the only
control that removes rows borrow the innocence of the ones that do not.

## Honest partial answers

```meta
type: feature
status: active
related: [.domain/sessions/domain.md#session-catalog]
```

Say what could not be read and what was left out, rather than presenting whatever
arrived as the whole picture.

### Name the source that could not be read

```meta
type: sub-feature
status: active
```

When one agent's records cannot be read — never installed, or not readable by this
user — the other agent's sessions are still shown and the unreadable one is named.

"No Copilot sessions" and "Copilot could not be read" are different facts, and only
one of them is worth investigating.

### Say how much was left out

```meta
type: sub-feature
status: active
related: [.domain/sessions/naming.md#session-limit]
```

An environment can hold hundreds of session records — enough that reading all of
them costs real time and showing all of them buries the handful that are running. A
reading therefore describes the most recent sessions per agent, and when it does, it
states how many exist.

Per agent rather than overall, so the agent that happens to keep more history cannot
crowd the other one out of a list whose whole point is showing both.

## Session activity enrichment

```meta
type: feature
status: proposed
depends-on: [.domain/sessions/features.md#session-inventory]
related: [.domain/sessions/domain.md#session-activity-publishing, .domain/sessions/domain.md#session-activity-enrichment]
```

Layer externally reported session activity onto the same session list, so a reader can
see what a session has been doing without replacing the locally read record that says
the session existed.

The extra layer is optional by design. A session with no Collections MCP reporting is
still read, grouped, and counted exactly as it is today; a session with reporting adds
timeline and correlation detail to that same row.

### Optional Collections MCP reporting

```meta
type: sub-feature
status: proposed
related: [.domain/sessions/domain.md#session-activity-publishing, .domain/sessions/naming.md#collections-mcp]
```

When a session has the Collections MCP configured, it reports start, meaningful
activity, and finish updates to that MCP. When it does not, nothing about the session
surface breaks or changes shape.

Capability detection is ordinary product behavior rather than setup drama: missing
configuration is simply `disabled`, not an error that needs a banner.

### Latest meaningful activity

```meta
type: sub-feature
status: proposed
related: [.domain/sessions/domain.md#session-enrichment-summary, .domain/sessions/domain.md#session-activity-entry]
```

Show the latest human-readable activity summary, when it arrived, and any safe
correlation facts that help a person place the session — for example the orchestration
run or issue it was working against.

Meaningful rather than exhaustive. The point is to answer "what is this session doing"
without turning the session list into a transcript or a tool log.

### Reporting degrades honestly

```meta
type: sub-feature
status: proposed
related: [.domain/sessions/domain.md#reporting-capability]
```

When reporting is configured but the Collections MCP cannot be reached, the session
still appears and the reporting path reads as degraded.

That answer is more honest than either hiding the reporting layer or pretending no
external activity exists. A broken path is a fact worth surfacing, and it is separate
from the session's own `Session State`.

## Turn the area off

```meta
type: feature
status: active
```

The whole area can be switched off, and when it is there is no way in and nothing to
find — not a disabled control and not an empty surface.

One switch for the area rather than one per column or per grouping, because "should
this product show me sessions at all" is a single question.
