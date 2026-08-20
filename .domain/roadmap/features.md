# Features: Roadmap Planning

```meta
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: Owning a stored plan

```meta
status: draft
related: [.domain/roadmap/domain.md#aggregate-roadmap-plan]
```

Keep a plan that exists in its own right, so intent survives a restart and can be
written down before the work has been refined into anything. The plan lives with
the person's own storage location: point the workspace at a different folder and
the plan moves with it, because a plan kept somewhere other than where its owner
keeps everything else is a plan they will lose.

### Sub-feature: Planning work that has no backlog entry yet

```meta
status: draft
```

Add an item to the plan with a title and dates alone. Most planning happens before
refinement, and a planning tool that first demands a refined work item is a tool
that gets used after the decisions have already been made somewhere else.

### Sub-feature: Linking an item to the entry that executes it

```meta
status: draft
related: [.domain/backlog/domain.md#aggregate-backlog-entry]
```

Name the [Backlog Entry](../backlog/domain.md#aggregate-backlog-entry) that
carries out a planned item, so the plan can show real progress instead of a
guess — while status and priority of the work itself stay where they belong. The
link is optional in both directions and may dangle without breaking the plan.

### Sub-feature: Milestones

```meta
status: draft
related: [.domain/roadmap/domain.md#milestone]
```

Mark the fixed points a plan is read against — a release, a freeze, a review, a
commitment made to someone else. A milestone is a day, not a short piece of work,
and it can be depended on exactly as an item can.

Dates share one place at the top of the plan rather than a line inside each
repository's band: a release is a fact about the plan, not about one project.

### Sub-feature: A date the whole plan is read against

```meta
status: draft
related: [.domain/roadmap/domain.md#milestone]
```

Say that a particular date is one everything is measured against — a release, a
freeze — and have it drawn through the whole plan rather than only where its marker
sits, so what lands before it can be read without tracing a vertical line by eye.

Whether a date is that kind of date is a planning judgement, so it is recorded on
the milestone rather than decided by whatever is drawing it. A plan where every date
claimed it would be a plan of lines.

## Feature: Priority planning

```meta
status: draft
depends-on: [.domain/roadmap/features.md#feature-owning-a-stored-plan]
related: [.domain/roadmap/domain.md#planning-priority]
```

Decide what matters most across projects at once, and record that decision in the
plan rather than in each project. The plan's priority is its own judgement: it is
never overwritten by the priority of a linked backlog entry, and setting it never
reaches into that entry — so reprioritising a quarter does not mean editing a
dozen issues.

## Feature: Dependency planning

```meta
status: draft
depends-on: [.domain/roadmap/features.md#feature-owning-a-stored-plan]
related: [.domain/roadmap/domain.md#dependency, .domain/roadmap/domain.md#domain-service-plan-sequencing]
```

Record what has to land before what, between any two things in the plan — item to
item, item to milestone, milestone to item. This is the capability that makes a
roadmap more than a list of dates: the order is stated once, and everything read
off the plan respects it.

### Sub-feature: Refusing a circular plan

```meta
status: draft
```

Reject a dependency that would make something wait, however indirectly, on
itself. The plan is left untouched and the reason is reported. A circular plan is
not a plan with a mistake in it; there is no order it could be executed in.

### Sub-feature: Surfacing contradictions instead of fixing them

```meta
status: draft
related: [.domain/roadmap/domain.md#domain-service-plan-sequencing]
```

Show where the plan disagrees with itself — work starting before the thing it
waits on has finished, or finishing after a milestone it was meant to precede.
These are reported, not silently corrected: discovering that a date does not fit
is the point of drawing the plan, and quietly moving the dependent work would
hide it.

## Feature: Repository-scoped planning

```meta
status: draft
depends-on: [.domain/roadmap/features.md#feature-owning-a-stored-plan]
related: [.domain/roadmap/domain.md#repository-scope, .domain/repository-management/features.md#feature-repository-registration]
```

Relate planned work to the repositories it happens in, using the same repository
identity the rest of the product already uses, so one plan can span a portfolio
and still be read one project at a time.

### Sub-feature: Reading the plan by repository

```meta
status: draft
```

Group the plan into per-repository bands, with the person's own lanes inside each.
Work that names several repositories is shown once, under the first of them, and
stays findable under any of them; work that names none reads as unfiled rather than
being hidden.

### Sub-feature: Surviving a repository that is no longer configured

```meta
status: draft
related: [.domain/roadmap/domain.md#domain-service-repository-scope-resolution]
```

Keep planned work readable when the repository it named is no longer configured.
The alias is kept as written and reads as unresolved; nothing the person typed is
deleted because a registry changed underneath it.

## Feature: Reading and rescheduling on a timeline

```meta
status: draft
depends-on: [.domain/roadmap/features.md#feature-dependency-planning, .domain/roadmap/features.md#feature-repository-scoped-planning]
related: [.domain/roadmap/flow.md, .design/interaction-guidelines.md]
```

See the whole plan against time — bands, lanes, spans, milestones and the arrows
between them — and change when something happens by moving it. A reschedule is a
change to the plan and is stored as one; the view proposes a new placement and
the plan decides whether it stands.

### Sub-feature: Rescheduling without a mouse

```meta
status: draft
```

Move and resize planned work from the keyboard, with every step announced. A plan
that could only be dragged would be a plan some people can read and nobody can
edit.

### Sub-feature: Telling one project from another at a glance

```meta
status: draft
related: [.design/color-scheme.md#band-identity-tokens, .domain/repository-management/features.md#sub-feature-repository-identity-colour]
```

Draw each repository's band in that repository's own colour, so a plan spanning a
portfolio can be read one project at a time without tracing every row back to its
label.

The colour is an **identity and nothing more**: it says which repository, never a
status, a severity or a priority. It is never the only thing saying it either — the
band is labelled, every span names its band when read aloud, and the repository
filter lists them in full — so a reader who cannot tell two hues apart loses
nothing. Priority on a plan is the ordinal shade ramp on the spans, which is one
colour and stays that way.

**The plan does not decide the colour and does not store it.** Which colour a
repository wears is a fact about that repository, settled in the registry, and the
plan is told it — the same reason this context holds repository aliases as opaque
strings rather than resolving them. A plan that recorded a colour of its own would
make the same project one colour here and another on the filter beside it, and
would have to be rewritten whenever the choice changed.

The band for work naming no repository, and the band carrying the plan's dates,
take no colour at all. A colour here means "which repository", and neither of those
is one.

## Feature: Editing the plan in place

```meta
status: draft
depends-on: [.domain/roadmap/features.md#feature-owning-a-stored-plan]
related: [.domain/roadmap/domain.md#aggregate-roadmap-plan, .domain/roadmap/features.md#feature-dependency-planning]
```

Add planned work, change it, and take it off the plan, from the same place the plan
is read. Planning is not a thing done once: dates move, priorities change, and what
something waits for is usually learned after it was first written down.

An edit submits the whole item — title, window, priority, repositories, lane, notes —
rather than the fields that changed, because "leave this alone" and "clear this" are
different intentions and a partial edit cannot tell them apart. The item's identity
survives every edit, so anything waiting on it still is.

### Sub-feature: Editing what something waits for

```meta
status: draft
related: [.domain/roadmap/features.md#sub-feature-refusing-a-circular-plan]
```

Add and remove dependencies while looking at the item that has them. A dependency is
answered on the spot rather than when the rest of the edit is saved, because it is
the one change that can be refused for a reason none of the fields explain — it
would make the plan circular — and that has to be said while the change is still the
thing being looked at.

### Sub-feature: Refusing an edit rather than half-applying it

```meta
status: draft
```

An edit that cannot stand — no title, or dates that do not make a window — changes
nothing at all, and says why without closing what is being edited. A form that
closed on a refusal would look exactly like one that saved.

## Feature: Publishing planned intent for observation

```meta
status: draft
depends-on: [.domain/roadmap/features.md#feature-owning-a-stored-plan]
related: [.domain/roadmap/domain.md#domain-event-roadmapitemscheduled, .domain/monitoring/features.md]
```

Announce when planned work is scheduled or moved, so
[Monitoring & Dashboard](../monitoring/domain.md#aggregate-progress-signal) can
compare intent against delivery. Roadmap publishes and does not subscribe: nothing
observed downstream reaches back in and edits the plan.
