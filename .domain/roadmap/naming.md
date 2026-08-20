# Naming: Roadmap Planning

```meta
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their aliases.
> Each term links to where it is modeled (`related`); the surface names it is also
> known by are recorded in the `aliases` metadata field so a synonym can always be
> resolved back to one canonical concept.

## Term: Roadmap Plan

```meta
status: draft
aliases: [RoadmapPlan, plan, the plan]
related: [.domain/roadmap/domain.md#aggregate-roadmap-plan]
```

The single stored plan for a workspace, and the consistency boundary for
everything in it. One plan per storage location — "the roadmap" in conversation
means this.

## Term: Roadmap Item

```meta
status: draft
aliases: [RoadmapItem, roadmap_item_id, planned work, planned item]
related: [.domain/roadmap/domain.md#roadmap-item]
```

One piece of planned work in the plan: a title, when it is intended to run, how
much the plan wants it, which repositories it belongs to, and what it waits on. It
carries no status of its own — see `Backlog Entry Link`.

`roadmap_item_id` is stable across every reschedule, which is what makes it safe
for another context to keep as a foreign id.

## Term: Milestone

```meta
status: draft
aliases: [Milestone, roadmap_milestone_id, fixed point]
related: [.domain/roadmap/domain.md#milestone]
```

A single day the plan is read against — a release, a freeze, a review, a
commitment. Not a short Roadmap Item: it has no duration, and it is drawn and
rescheduled as one date.

## Term: Roadmap Node

```meta
status: draft
aliases: [RoadmapNodeId, node, dependency endpoint]
related: [.domain/roadmap/domain.md#dependency]
```

Either end of a dependency: a Roadmap Item or a Milestone. The term exists because
a dependency does not care which of the two it points at, and inventing a shared
base type would imply a shared lifecycle they do not have.

## Term: Planned Window

```meta
status: draft
aliases: [PlannedWindow, start, end, span]
related: [.domain/roadmap/domain.md#planned-window]
```

The stretch of time a Roadmap Item is intended to run, as a first and a last day.
**Both days are inclusive** — "through the 31st" means the 31st. Every consumer
and every drawing of the plan reads it that way.

## Term: Planning Priority

```meta
status: draft
aliases: [PlanningPriority, planning priority]
related: [.domain/roadmap/domain.md#planning-priority, .domain/backlog/domain.md#priority]
```

How much the plan wants an item relative to the others: `low`, `medium`, `high`,
`critical`.

The same four words as Backlog Management's
[Priority](../backlog/domain.md#priority), and deliberately a different
value: that one ranks a work item for execution, this one ranks intent across
projects. When both appear in one sentence, say which.

## Term: Repository Scope

```meta
status: draft
aliases: [RepositoryScope, repository_aliases, repos, scope]
related: [.domain/roadmap/domain.md#repository-scope, .domain/repository-management/naming.md#term-repository]
```

The repositories a Roadmap Item or Milestone belongs to, as a set of repository
aliases held opaquely. Empty means unfiled, not "all". The alias is the same key
Backlog's `repo_ids` and the knowledge folders already scope by, so a repository
means the same thing everywhere.

## Term: Dependency

```meta
status: draft
aliases: [Dependency, depends_on, depends_on_id, waits on, blocked by]
related: [.domain/roadmap/domain.md#dependency, .domain/roadmap/domain.md#domain-service-plan-sequencing]
```

The statement that one Roadmap Node must land before another can. Stored on the
waiting side, which is why the phrase in code and in conversation is "depends on"
rather than "blocks".

## Term: Planning Lane

```meta
status: draft
aliases: [PlanningLane, lane, row]
related: [.domain/roadmap/domain.md#planning-lane]
```

A free-form row label within a repository band, chosen by the person rather than
by the product — the plan's counterpart to a Backlog Entry's
[Area](../backlog/naming.md#term-area). Blank means the default lane.

## Term: Backlog Entry Link

```meta
status: draft
aliases: [BacklogEntryLink, backlog_entry_id]
related: [.domain/roadmap/domain.md#backlog-entry-link, .domain/backlog/naming.md#term-backlog-entry]
```

The optional foreign id naming the Backlog Entry that executes a Roadmap Item. It
is how the plan shows real progress without owning any, it may dangle, and a
dangling link reads as unlinked rather than as an error.

## Term: Roadmap Tag

```meta
status: draft
aliases: [RoadmapTag, tag, roadmap tag, slug]
related: [.domain/roadmap/domain.md#roadmap-tag, .domain/backlog/naming.md#term-roadmap-tag]
```

The lowercase kebab-case slug a Roadmap Item is filed under, and the vocabulary
two other contexts borrow to say work belongs to that item. Derived from the
title when the item is created and then independent of it — **a rename never
changes the tag**, because entries and chapters already written against it would
stop matching. Every item has one; a title that slugifies to nothing takes the
constant `item`. Not unique across items: a shared tag is how a person groups
planned work on purpose.

## Term: Knowledge Ref

```meta
status: draft
aliases: [KnowledgeRef, knowledge_refs, knowledge reference]
related: [.domain/roadmap/domain.md#knowledge-ref, .domain/second-brain/naming.md#term-knowledge-note]
```

A direct `<path>#<slug>` reference from a Roadmap Item to a knowledge chapter that
informs it. The knowledge counterpart of the `Backlog Entry Link`: an id-shaped
reference the plan holds, never reads through, and never validates. It may dangle
and a dangling ref reads as unresolved rather than as an error.

## Term: Effort

```meta
status: draft
aliases: [effort, story points, story-point estimate, total registered effort]
related: [.domain/roadmap/domain.md#domain-service-roadmap-item-gathering, .domain/backlog/naming.md#term-effort]
```

Size measured in story points. Roadmap Planning never registers effort — that is
done on Backlog Entries and knowledge chapters — but it **totals** it: over
everything a Roadmap Item gathers, the *total registered effort* is plain
arithmetic over the points that were actually registered, reported alongside the
count of gathered things that registered none. A total, not a measurement of time,
and never an inference: unestimated work is counted as unestimated, not as zero
that hides in the sum.

## Term: Contradiction

```meta
status: draft
aliases: [contradiction, conflicting dates]
related: [.domain/roadmap/domain.md#domain-service-plan-sequencing]
```

A plan that disagrees with itself about dates — work opening before the thing it
waits on has closed, or finishing after a milestone it was meant to precede.
Reported, never corrected. Distinct from a **cycle**, which is refused outright:
a contradiction is a plan with a date problem, a cycle is not a plan.
