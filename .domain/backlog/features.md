# Features: Backlog Management

```meta
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: Backlog entry creation

```meta
status: draft
related: [.domain/inbox/features.md#feature-routing]
```

Create entries manually or from triaged inbox items, capturing title, body, type
(prompt, task, idea, follow-up), tags, and project/repo link. New entries default
to `draft`.

## Feature: Refinement and prioritization

```meta
status: draft
depends-on: [.domain/backlog/features.md#feature-backlog-entry-creation]
```

Edit and enrich entries over time: set priority and status, add context links,
and flag oversized items with suggested splits. The desktop experience keeps
Markdown canonical while letting users adjust type, priority, repository,
status, and metadata tags directly from the reading layout. Expanded entries
open into an inline Markdown editor for the full entry body; compact entries can
stay on one line when they only need metadata-level refinement.

![Desktop backlog entry with inline Markdown editing](assets/backlog-entry-inline-markdown-editing.png)

### Sub-feature: Sub-items and steps

```meta
status: draft
```

Break an entry into ordered sub-items with title, status (pending → done), and
notes. Sub-items can be toggled between open and done from the rendered entry,
reorder/add/remove independently, parent progress reflects completion (e.g. 3/5
done), and they can project to GitHub issue task lists.

A step is a sub-item; the product has one concept here, not two. A sub-item
carries those four attributes and nothing more — it has no type, priority, entry
status or tags of its own, and none of the entry's scheduling or dependency
attributes. A breakdown step needing its own priority is an entry rather than a
step, and a step inherits its parent's deadline by belonging to it.

### Sub-feature: Attached material

```meta
status: draft
related: [.domain/backlog/domain.md#attachment]
```

Point an entry at the folder or archive where its material is kept — the review
pack, the screenshots, the exported data — and take the pointer off again. The
entry says which place and what it is called, and whether that place is a folder
or an archive.

One place per entry, not a list. What a person means by "the files for this" is
usually a folder they already keep them in, and an entry that listed members
would grow its own presentation by however many files somebody dropped on it. A
second place is not a second attachment; it is either the same folder further up
or a different entry.

A pointer, not a copy. Nothing is imported and nothing is stored beside the
entry: the entry stays the Markdown that gets committed and shared, and the
material stays where its owner put it. The consequence is stated rather than
hidden — a path is meaningful on the machine that wrote it, so an entry read
somewhere else may name a place that is not there, and the entry is no less valid
for it. Attaching is a claim about where material lives, not a promise that the
reader can reach it.

How much is in the place is not recorded, only where it is. A count would be true
at the moment it was written and wrong after the next file was added, and an
entry that asserted one would be asserting something it cannot keep.

## Feature: Scheduling and recurrence

```meta
status: proposed
depends-on: [.domain/backlog/features.md#feature-refinement-and-prioritization]
related: [.domain/backlog/domain.md#aggregate-backlog-entry]
```

Say when an entry is due, when to be reminded of it, and whether it comes back.
Three separate facts rather than one: a deadline, an alarm, and a shape. A
backlog that could only say "Friday" could not distinguish the entry that is due
that day from the one that should interrupt you that morning.

All three are optional and none of them changes the entry lifecycle. An entry
with a due date in the past is overdue, which is read by comparison rather than
recorded as a status, so nothing has to sweep the backlog to keep status honest.

The scheduling fields are written on the entry's metadata line as named tokens
— `due:2026-08-21`, `remind:2026-08-21T09:00`, `repeat:weekly` — alongside the
existing sigil tokens for type, priority, status, area and tags. Named rather
than sigil-prefixed because the punctuation namespace is nearly exhausted and
five one-character marks for five date-shaped concepts would be unreadable in a
file people hand-edit.

### Sub-feature: Due dates

```meta
status: proposed
```

Commit an entry to a calendar day. A due date is a date and not an instant: it
carries no time and no timezone, so "due Friday" stays Friday when the device
moves. How that date is said on screen — "Today", "Friday, 21 August", or a
localized format — belongs to the channel showing it, not to the entry.

### Sub-feature: Reminders

```meta
status: proposed
```

Ask to be reminded of an entry at a chosen local date and time. A reminder is
wall-clock intent: 09:00 means 09:00 wherever the person is when it arrives,
rather than the instant 09:00 once meant somewhere else.

A reminder is a request recorded on the entry, not a promise about delivery. One
whose time has passed reads as overdue and keeps reading that way until it is
cleared or the entry is completed, so a reminder that came due while the app was
closed surfaces rather than being silently missed.

### Sub-feature: Recurring entries

```meta
status: proposed
related: [.domain/backlog/domain.md#domain-service-recurrence, .domain/backlog/features.md#feature-archive-and-lifecycle]
```

Repeat an entry on a schedule: every day, every week, every month, every year,
optionally restricted to particular weekdays so "every weekday" is expressible.
The repeat is anchored to the due date rather than to when the work actually
finished, so an entry completed three days late still falls due on its original
weekday.

Completing a recurring entry leaves it completed and creates the next occurrence
as a new entry. The finished occurrence stays as the record of what was done
rather than being rolled forward and overwritten, which means a repeating entry
accumulates one completed entry per occurrence — archiving is what keeps that
from crowding the default views.

## Feature: My Day

```meta
status: proposed
depends-on: [.domain/backlog/features.md#feature-refinement-and-prioritization]
related: [.domain/backlog/features.md#feature-scheduling-and-recurrence]
```

Pick the entries to work on today, separately from when they are due. My Day is
this morning's decision about what to look at, and it is deliberately not a
deadline: an entry due next Friday can be in today's My Day, and an entry due
today need not be.

Because it is a decision about a particular day, it expires on its own. An entry
carries the date it was picked for, and it is in My Day exactly while that date
is the reader's current local date — so yesterday's list clears itself with no
timer, no timezone rule and no overnight sweep, and a device that was switched
off for a week comes back to an empty My Day rather than a stale one.

## Feature: Entry dependencies

```meta
status: proposed
depends-on: [.domain/backlog/features.md#feature-refinement-and-prioritization]
related: [.domain/backlog/domain.md#aggregate-backlog-entry, .domain/backlog/features.md#sub-feature-manual-ordering]
```

Record that an entry waits on other entries, so a set of prompts or tasks meant
to be worked in order says so instead of relying on the reader remembering.
An entry can wait on several others: needing two things finished before it can
start is ordinary, and asking which of the two is the real predecessor has no
answer.

This is a different fact from manual ordering. Ordering is the sequence a person
arranged the backlog in; a dependency is a constraint that holds whatever order
the list happens to be shown in.

### Sub-feature: Readiness and chain order

```meta
status: proposed
```

Read off what can actually be started. An entry is done, ready when everything
it waits on is finished, or blocked when something is not — and a blocked entry
says what it is waiting for rather than only that it cannot proceed. Readiness is
derived every time it is read, never stored, so finishing one entry unblocks its
dependents with nothing to recalculate or keep in sync.

Two questions get separate answers because they are separate questions: which
entry to pick up first, and which entries could be started now. In a straight
chain those are the same entry; when finishing one step unblocks three, they are
not, and a view that answered only the first would leave the other two to be
noticed by their waiting lines disappearing.

A dependency naming an entry that cannot be found still blocks. Treating an
unresolvable id as satisfied would let a chain report itself ready when the step
it waits on is merely missing from view, which is the one failure that looks
exactly like success. Dependency loops are named rather than broken: which edge
in a loop is the wrong one is answerable only by the person who wrote them.

## Feature: Multi-repo targeting

```meta
status: draft
depends-on: [.domain/backlog/features.md#feature-backlog-entry-creation]
related: [.domain/backlog/features.md#feature-projection]
```

Let one logical entry target multiple repositories (`repo_ids[]`) while remaining
a single source of truth — one item, one priority, one status — with a unified
view across all contexts.

## Feature: Projection

```meta
status: draft
depends-on: [.domain/backlog/features.md#feature-multi-repo-targeting]
related: [.domain/monitoring/features.md#sub-feature-backlog-and-github-progress]
```

Spawn and close downstream artifacts from an entry: one GitHub issue and/or
Copilot CLI task per target repo, created when work starts and closed on
completion, without duplicating the backlog item.

### Sub-feature: Issue projection and state read-back

```meta
status: draft
related: [.domain/repository-management/features.md#sub-feature-github-access-resolution, .domain/monitoring/features.md#sub-feature-backlog-and-github-progress]
```

Push an entry to its target repository as an issue carrying the entry's title,
body, and tags, and keep the resulting issue reference on the entry so the link
is part of the item rather than a note about it. The entry can then be asked to
re-read that issue's current state, and the pull request that references it, so
downstream progress is visible from the backlog. Reading GitHub state is a
deliberate act rather than a background poll, because the backlog has to open
instantly and offline.

## Feature: Search, filter and organize

```meta
status: draft
related: [.domain/second-brain/features.md#feature-bi-directional-linking]
```

Search across title, body, tags, and linked knowledge notes; filter by area
(a self-chosen grouping such as "repos", "projects", or "inbox"), repo, type,
status, priority, and recency; grouped views; and inline embedding of Second
Brain content.

### Sub-feature: Manual ordering

```meta
status: draft
```

Hand-sequence entries within the backlog by dragging them into a preferred
order, independent of recency or priority. An entry that has never been
manually ranked falls back to recency.

## Feature: Prompt features

```meta
status: draft
```

One-click copy of prompt text to clipboard, usage-history logging on copy/use,
and reopening historical prompts from the usage log.

### Sub-feature: Hand-off to Copilot CLI

```meta
status: draft
related: [.domain/productivity/features.md#sub-feature-ai-activity-capture]
```

Hand an entry to GitHub Copilot CLI as a task brief without retyping it: the
entry's own markdown — title, metadata, body, and sub-items — is the brief, and
the hand-off is recorded in the entry's usage history so the entry itself shows
that AI was put to work on it.

## Feature: AI assistance over the visible backlog

```meta
status: draft
related: [.domain/second-brain/features.md#feature-repository-knowledge-areas, .domain/productivity/features.md#feature-ai-productivity-tracking]
```

Ask questions about the work currently in view and get an answer grounded in it.
The question is answered from the entries the active filters leave visible plus
the loaded backlog knowledge, not from the entire backlog, so the answer matches
what the person is actually looking at. Entries that were opened but never
edited are left out, and the assembled context is capped so a large backlog
degrades into a partial answer rather than a failure. AI assistance is an opt-in
capability and the product remains fully usable with it switched off.

## Feature: Archive and lifecycle

```meta
status: draft
```

Move entries between active and archived states; archived entries are excluded
from default views but always accessible and restorable.

## Feature: Roadmap planning

```meta
status: deprecated
depends-on: [.domain/backlog/features.md#feature-refinement-and-prioritization]
related: [.domain/roadmap/features.md, .domain/roadmap/domain.md#aggregate-roadmap-plan, .domain/backlog/domain.md#aggregate-backlog-entry]
```

**Superseded.** Roadmap planning is a bounded context of its own — see
[Roadmap Planning](../roadmap/features.md). This chapter is kept, at this heading,
because other chapters reference it; it is not the model any more.

What this chapter used to say was that the roadmap groups *selected backlog
entries* by theme, horizon, environment or repository, and that its progress is
derived from those entries rather than stored. The first half turned out to be too
narrow: a plan has to be able to hold work that has not been refined into an entry
yet, which is most of what planning is, so the plan is stored in its own right and
a planned item may *optionally* name the entry that executes it.

The second half survives, and is the part worth carrying forward: **Backlog Entry
remains the source of truth for a work item's status and execution priority.**
Roadmap Planning owns planning priority and sequence; it never writes an entry's
status or priority, and the progress it shows for a linked item is read from the
entry rather than maintained by hand.

What Backlog Management keeps from this feature is the
[refinement and prioritization](#feature-refinement-and-prioritization) that makes
an entry ready in the first place, and the
[Partnership link](../roadmap/dependencies.md) that lets a plan point at it.

### Sub-feature: Roadmap views

```meta
status: deprecated
related: [.domain/roadmap/features.md#feature-reading-and-rescheduling-on-a-timeline]
```

**Superseded** by
[reading and rescheduling on a timeline](../roadmap/features.md#feature-reading-and-rescheduling-on-a-timeline)
and
[reading the plan by repository](../roadmap/features.md#sub-feature-reading-the-plan-by-repository)
in Roadmap Planning. Now/Next/Later is not how the plan is read — a horizon is a
grouping, and the plan is read against dates, in repository bands with the
person's own lanes inside them.