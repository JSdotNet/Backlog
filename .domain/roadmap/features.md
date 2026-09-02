# Roadmap Planning

```meta
type: features
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Owning a stored plan

```meta
type: feature
status: draft
related: [.domain/roadmap/domain.md#roadmap-plan]
```

Keep a plan that exists in its own right, so intent survives a restart and can be
written down before the work has been refined into anything. The plan lives with
the person's own storage location: point the workspace at a different folder and
the plan moves with it, because a plan kept somewhere other than where its owner
keeps everything else is a plan they will lose.

### Planning work that has no task yet

```meta
type: sub-feature
status: draft
```

Add an item to the plan with a title and dates alone. Most planning happens before
refinement, and a planning tool that first demands a refined work item is a tool
that gets used after the decisions have already been made somewhere else.

### Linking an item to the task that executes it

```meta
type: sub-feature
status: draft
related: [.domain/tasks/domain.md#task]
```

Name the [Task](../tasks/domain.md#task) that
carries out a planned item, so the plan can show real progress instead of a
guess — while status and priority of the work itself stay where they belong. The
link is optional in both directions and may dangle without breaking the plan.

### Milestones

```meta
type: sub-feature
status: draft
related: [.domain/roadmap/domain.md#milestone]
```

Mark the fixed points a plan is read against — a release, a freeze, a review, a
commitment made to someone else. A milestone is a day, not a short piece of work,
and it can be depended on exactly as an item can.

Dates share one place at the top of the plan rather than a line inside each
repository's band: a release is a fact about the plan, not about one project.

### A date the whole plan is read against

```meta
type: sub-feature
status: draft
related: [.domain/roadmap/domain.md#milestone]
```

Say that a particular date is one everything is measured against — a release, a
freeze — and have it drawn through the whole plan rather than only where its marker
sits, so what lands before it can be read without tracing a vertical line by eye.

Whether a date is that kind of date is a planning judgement, so it is recorded on
the milestone rather than decided by whatever is drawing it. A plan where every date
claimed it would be a plan of lines.

## Tagging planned work

```meta
type: feature
status: draft
depends-on: [.domain/roadmap/features.md#owning-a-stored-plan]
related: [.domain/roadmap/domain.md#roadmap-tag]
```

Give every planned item a short tag other work can be filed against, so a Task
 or a knowledge chapter can say "this belongs to that plan item" without the
plan having to name it first. The tag is derived from the item's title when the
item is created — a person does not invent it — and is then editable on its own.

It deliberately **does not** move when the title is later renamed. The tag is the
word other places have already written down, and quietly reslugging it would make
every task filed under it and every chapter naming it stop matching, with nothing
to say so. Renaming the title and retagging the item are two different acts, and
keeping them apart is what makes a tag safe to write elsewhere.

Every item has a tag — there is no untagged item — and a title that comes out
empty once slugified falls back to a plain `item` so there is always something to
file against. Tags are not forced to be unique: two items can share one on
purpose, and the plan can list the tags in use and show the items sitting under
any one of them, so grouping planned work under a shared tag is a first-class
thing to do rather than an accident to be prevented.

## Gathering work under an item and totalling its effort

```meta
type: feature
status: draft
depends-on: [.domain/roadmap/features.md#tagging-planned-work]
related: [.domain/roadmap/domain.md#roadmap-item-gathering, .domain/tasks/features.md#effort-registration, .domain/second-brain/features.md#topic-and-tag-grouping]
```

Read, for one planned item, everything that belongs to it and what it all adds up
to in story points — so an item on the plan can show the size of the work behind
it without anyone maintaining that number by hand.

An item gathers work two ways at once. It gathers what it **names** outright: the
Task it links, and the knowledge chapters it references directly. And it
gathers what carries its **tag**: every Task filed under the item's tag,
and every knowledge chapter whose own roadmap list names that tag. Something
reached both ways — linked and tagged — is shown once, but the plan remembers it
was held by both threads, because a person about to remove a link needs to see
whether the tag would still hold the work afterwards.

Over all of it, the item reports its **total registered effort**: the story points
that were actually registered, added up, with nothing invented for the work that
was never estimated. Because dropping the unestimated work would make the total
read smaller than the work really is, the item also says **how many gathered
things carry no estimate**, so a small total that hides a pile of unsized work
cannot be mistaken for a small pile of work. The plan owns none of these numbers —
they are registered on the tasks and the chapters, in Tasks and
Second Brain — and it only reads and adds them.

## Priority planning

```meta
type: feature
status: draft
depends-on: [.domain/roadmap/features.md#owning-a-stored-plan]
related: [.domain/roadmap/domain.md#planning-priority]
```

Decide what matters most across projects at once, and record that decision in the
plan rather than in each project. The plan's priority is its own judgement: it is
never overwritten by the priority of a linked task, and setting it never
reaches into that task — so reprioritising a quarter does not mean editing a
dozen issues.

## Dependency planning

```meta
type: feature
status: draft
depends-on: [.domain/roadmap/features.md#owning-a-stored-plan]
related: [.domain/roadmap/domain.md#dependency, .domain/roadmap/domain.md#plan-sequencing]
```

Record what has to land before what, between any two things in the plan — item to
item, item to milestone, milestone to item. This is the capability that makes a
roadmap more than a list of dates: the order is stated once, and everything read
off the plan respects it.

### Refusing a circular plan

```meta
type: sub-feature
status: draft
```

Reject a dependency that would make something wait, however indirectly, on
itself. The plan is left untouched and the reason is reported. A circular plan is
not a plan with a mistake in it; there is no order it could be executed in.

### Surfacing contradictions instead of fixing them

```meta
type: sub-feature
status: draft
related: [.domain/roadmap/domain.md#plan-sequencing]
```

Show where the plan disagrees with itself — work starting before the thing it
waits on has finished, or finishing after a milestone it was meant to precede.
These are reported, not silently corrected: discovering that a date does not fit
is the point of drawing the plan, and quietly moving the dependent work would
hide it.

## Repository-scoped planning

```meta
type: feature
status: draft
depends-on: [.domain/roadmap/features.md#owning-a-stored-plan]
related: [.domain/roadmap/domain.md#repository-scope, .domain/repository-management/features.md#repository-registration]
```

Relate planned work to the repositories it happens in, using the same repository
identity the rest of the product already uses, so one plan can span a portfolio
and still be read one project at a time.

### Reading the plan by repository

```meta
type: sub-feature
status: draft
```

Group the plan into per-repository bands, with the person's own lanes inside each.
Work that names several repositories is shown once, under the first of them, and
stays findable under any of them; work that names none reads as unfiled rather than
being hidden.

### Surviving a repository that is no longer configured

```meta
type: sub-feature
status: draft
related: [.domain/roadmap/domain.md#repository-scope-resolution]
```

Keep planned work readable when the repository it named is no longer configured.
The alias is kept as written and reads as unresolved; nothing the person typed is
deleted because a registry changed underneath it.

## Reading and rescheduling on a timeline

```meta
type: feature
status: draft
depends-on: [.domain/roadmap/features.md#dependency-planning, .domain/roadmap/features.md#repository-scoped-planning]
related: [.domain/roadmap/flow.md, .design/interaction-guidelines.md]
```

See the whole plan against time — bands, lanes, spans, milestones and the arrows
between them — and change when something happens by moving it. A reschedule is a
change to the plan and is stored as one; the view proposes a new placement and
the plan decides whether it stands.

### Rescheduling without a mouse

```meta
type: sub-feature
status: draft
```

Move and resize planned work from the keyboard, with every step announced. A plan
that could only be dragged would be a plan some people can read and nobody can
edit.

### Telling one project from another at a glance

```meta
type: sub-feature
status: draft
related: [.design/color-scheme.md#band-identity-tokens, .domain/repository-management/features.md#repository-identity-colour]
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

## Editing the plan in place

```meta
type: feature
status: draft
depends-on: [.domain/roadmap/features.md#owning-a-stored-plan]
related: [.domain/roadmap/domain.md#roadmap-plan, .domain/roadmap/features.md#dependency-planning]
```

Add planned work, change it, and take it off the plan, from the same place the plan
is read. Planning is not a thing done once: dates move, priorities change, and what
something waits for is usually learned after it was first written down.

An edit submits the whole item — title, window, priority, repositories, lane, notes —
rather than the fields that changed, because "leave this alone" and "clear this" are
different intentions and a partial edit cannot tell them apart. The item's identity
survives every edit, so anything waiting on it still is.

### Editing what something waits for

```meta
type: sub-feature
status: draft
related: [.domain/roadmap/features.md#refusing-a-circular-plan]
```

Add and remove dependencies while looking at the item that has them. A dependency is
answered on the spot rather than when the rest of the edit is saved, because it is
the one change that can be refused for a reason none of the fields explain — it
would make the plan circular — and that has to be said while the change is still the
thing being looked at.

### Refusing an edit rather than half-applying it

```meta
type: sub-feature
status: draft
```

An edit that cannot stand — no title, or dates that do not make a window — changes
nothing at all, and says why without closing what is being edited. A form that
closed on a refusal would look exactly like one that saved.

## Publishing planned intent for observation

```meta
type: feature
status: draft
depends-on: [.domain/roadmap/features.md#owning-a-stored-plan]
related: [.domain/roadmap/domain.md#roadmapitemscheduled, .domain/monitoring/features.md]
```

Announce when planned work is scheduled or moved, so
[Monitoring & Dashboard](../monitoring/domain.md#progress-signal) can
compare intent against delivery. Roadmap publishes and does not subscribe: nothing
observed downstream reaches back in and edits the plan.

## Sequencing work into tracks

```meta
type: feature
status: proposed
depends-on: [.domain/roadmap/features.md#tagging-planned-work, .domain/roadmap/features.md#gathering-work-under-an-item-and-totalling-its-effort]
related: [.domain/roadmap/features.md#dependency-planning, .domain/roadmap/features.md#reading-and-rescheduling-on-a-timeline, .domain/roadmap/domain.md#planning-lane, .domain/tasks/features.md#effort-registration, .domain/second-brain/features.md#topic-and-tag-grouping]
```

**An idea, written down to be argued with — not an agreed model.** This folder's
status vocabulary has no `idea`, so `proposed` carries it here: nothing below is
settled, and the open questions are as much the point of the chapter as the
description is.

Plan a repository's work as **tracks** rather than as dates. A track is an *area
within one repository* holding a chain of work that has to be done in order — each
piece written against the one before it, the way one feature depends on another.
What the plan then answers is not *when does this run*, but **what can be picked
up right now, and what would collide if two workers picked up at once**.

The goal is parallel work without conflicting changes. Two tracks are, by
construction, different areas, so the head of each can be worked at the same time;
two pieces inside one track cannot, because the later one is written against the
earlier. That makes the plan a statement about **safe concurrency** rather than a
forecast, and it is the thing a dated plan cannot say: a
[Planned Window](domain.md#planned-window) tells you two items overlap in *time*,
never whether they overlap in *code*.

It would be built on what this context already has, not beside it. The chain is
the existing [Dependency](domain.md#dependency) — acyclicity, and
[Plan Sequencing](domain.md#plan-sequencing)'s reachability answers, are exactly
what an ordered chain needs. The work a track holds is reached the way an item
already reaches it: by named link and by tag, across Tasks and Second
Brain. And **the repository line stays as it is** — a track sits inside a
repository band exactly as lanes and items do today, so a portfolio still reads
one project at a time.

**Open questions.** Each of these changes what the idea is, not merely how it is
built:

- **Replacement or addition?** If dated planning goes, the Planned Window, the
  timeline reading, and [RoadmapItemScheduled](domain.md#roadmapitemscheduled) go
  with it — and that event is the one contract
  [Monitoring](../monitoring/domain.md#progress-signal) consumes, so comparing
  intent against delivery would need a new answer or a different question. If both
  are kept, one plan carries two ways of ordering the same work, and something has
  to say which one a reader is looking at.
- **Is a track a [Planning Lane](domain.md#planning-lane) with an order and a size,
  or a new node?** A lane today is a free-form label the person owns and nothing
  depends on. A track is ordered and depended upon. Making the lane ordered would
  change what every label already written means, so this is a rename with
  consequences rather than a small extension.
- **What is an "area", and who decides that two areas cannot collide?** The safety
  claim rests entirely on this. If an area is the person's own word — as a lane is —
  the guarantee is their judgement, and the plan should say so rather than imply
  otherwise. If the product derives it from paths the repository actually has,
  Roadmap starts holding repository facts it has deliberately never owned:
  [Repository Scope](domain.md#repository-scope) keeps opaque aliases for exactly
  that reason, and resolving them is a supplier's job.
- **Does a track hold work, or gather it?** If the chain runs between plan nodes it
  is the existing Dependency and nothing moves. If it runs between Tasks,
  the dependency leaves this context — and Tasks models no dependency
  today, which is why Roadmap is described as the only context that holds one
  between two pieces of planned work.
- **Do milestones survive?** A [Milestone](domain.md#milestone) is a day, and a plan
  with no dates has nowhere to put one. Either it stays as the plan's single
  remaining tie to a calendar, or the commitments it records need somewhere else to
  live.

### Sizing a track by the effort it gathers

```meta
type: sub-feature
status: proposed
related: [.domain/roadmap/domain.md#roadmap-item-gathering, .domain/tasks/features.md#effort-registration]
```

Read how big a track is from the **total registered effort** of the work it
gathers — story points, a relative size — instead of from a span of days. This is
what takes the calendar's place: "this track is twice the one beside it" is a
judgement the person can actually make, where "this track ends on the 14th" is one
they mostly cannot.

The arithmetic is the one already described in
[gathering work under an item](#gathering-work-under-an-item-and-totalling-its-effort),
unchanged and for the same reasons: the points are registered on the tasks and
the chapters, Roadmap only adds them, something reached both by link and by tag
counts once, and the count of gathered things that registered no estimate is
reported next to the total rather than folded into it. A track whose size hid its
unestimated work would read as small precisely when it is least understood.

### Picking work that cannot collide

```meta
type: sub-feature
status: proposed
related: [.domain/roadmap/domain.md#plan-sequencing]
```

Ask the plan what is safe to start now: the head of every track whose predecessor
has landed, with the tracks sharing an area held back rather than offered. One
question, answered from the chain and the areas, in place of comparing two spans
on a timeline by eye.

Whether the plan may state this as a **guarantee** or only as advice is the
unresolved part, and it decides how much the idea is worth — see the open question
about what an area is.
