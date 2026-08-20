# Features: Sessions

```meta
status: active
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: Session inventory

```meta
status: active
related: [.domain/sessions/domain.md#aggregate-session-log]
```

Answer "what have my agents been doing here" as one list: every session the
environment has a record of, most recently active first, whichever agent ran it.

One list rather than one per agent, because the question is about the work and not
about the vendor. A person who has both agents installed does not think in two
inventories.

### Sub-feature: Live and past sessions together

```meta
status: active
```

A session that is running now and a session that finished last week appear in the
same list, distinguished by their state rather than by which list they are in.
Separating them would force the reader to know which list to look in before knowing
whether the session had ended — which is usually the thing they are trying to find
out.

### Sub-feature: Only what the agent recorded

```meta
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

### Sub-feature: Re-read on request

```meta
status: active
```

The list is read when it is opened and again when the reader asks for it. Nothing
polls.

A surface that refreshed itself would be claiming to be live, and the evidence does
not support the claim: one of the two agents leaves no liveness marker at all, so a
self-moving list would move without meaning.

## Feature: Session grouping

```meta
status: active
related: [.domain/sessions/domain.md#domain-service-session-grouping]
```

Carve the same list up by the two things that separate sessions from each other in
practice, without removing any of them.

### Sub-feature: Group by environment

```meta
status: active
related: [.domain/sessions/naming.md#term-environment]
```

A section per environment, named after it, with its own count — so "how many are
running on that box" is read rather than counted.

Single-valued for as long as an environment can only read its own records: one
section, named after the machine the reader is sitting at. It becomes the useful
grouping the moment a second environment reports.

### Sub-feature: Group by agent

```meta
status: active
```

A section per agent, so Claude's sessions and Copilot's can be read separately
without the two being separate lists.

### Sub-feature: Grouping never hides a session

```meta
status: active
```

The total is the same whichever grouping is chosen, and the surface says the total
beside the control that carves it up — so a grouping can be trusted not to be a
filter in disguise.

## Feature: Honest partial answers

```meta
status: active
related: [.domain/sessions/domain.md#session-catalog]
```

Say what could not be read and what was left out, rather than presenting whatever
arrived as the whole picture.

### Sub-feature: Name the source that could not be read

```meta
status: active
```

When one agent's records cannot be read — never installed, or not readable by this
user — the other agent's sessions are still shown and the unreadable one is named.

"No Copilot sessions" and "Copilot could not be read" are different facts, and only
one of them is worth investigating.

### Sub-feature: Say how much was left out

```meta
status: active
related: [.domain/sessions/naming.md#term-session-limit]
```

An environment can hold hundreds of session records — enough that reading all of
them costs real time and showing all of them buries the handful that are running. A
reading therefore describes the most recent sessions per agent, and when it does, it
states how many exist.

Per agent rather than overall, so the agent that happens to keep more history cannot
crowd the other one out of a list whose whole point is showing both.

## Feature: Turn the area off

```meta
status: active
```

The whole area can be switched off, and when it is there is no way in and nothing to
find — not a disabled control and not an empty surface.

One switch for the area rather than one per column or per grouping, because "should
this product show me sessions at all" is a single question.
