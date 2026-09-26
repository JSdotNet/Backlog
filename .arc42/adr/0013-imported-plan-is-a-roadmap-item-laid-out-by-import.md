# ADR 0013: An imported plan is one Roadmap Item; a `plan` entry is the same grammar, and the importer places it

```meta
status: active
related: [".arc42/adr/0007-import-reuses-the-entry-text-grammar.md", ".arc42/adr/0002-backlog-module-owns-the-entry-text-language.md", ".arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".arc42/adr/guidelines/0014-persistence-and-repository-boundaries.md", ".domain/roadmap/domain.md#roadmap-item", ".domain/roadmap/domain.md#roadmap-item-gathering", ".domain/roadmap/features.md#laying-out-imported-plans", ".domain/roadmap/features.md#sequencing-work-into-tracks", ".domain/roadmap/dependencies.md", ".domain/tasks/features.md#import", ".domain/tasks/features.md#re-importing-an-updated-plan", ".domain/tasks/features.md#effort-registration", ".design/content-editing.md#structured-metadata-sigils", ".design/content-editing.md#scheduling-and-dependency-tokens"]
issue: null
```

## Status

Accepted on 2026-09-23, through the plan's `confirm-rulings` entry, with two
amendments made when the rulings were confirmed — in
[ruling 2](#2-a-plan-entry-is-the-same-grammar-with-a-fourth-type-word) and
[ruling 5](#5-placed_by_import-and-what-a-re-import-may-touch), and marked there:

- **A cycle refuses the whole import**, rather than dropping the one edge and
  creating the item anyway. The roadmap already refuses an edit rather than
  half-applying it (`.domain/roadmap/features.md#refusing-an-edit-rather-than-half-applying-it`),
  and an import is one edit.
- **A tag more than one item carries updates the first of them by creation
  order**, and reports the ambiguity, rather than updating none.

Written as the first entry of the `roadmap-imported-plans` plan, so that every
later entry in it — the `plan` type word, the intake port, the placement rule,
the velocity setting, the steps drawn inside an item, the shelf — implements one
decision instead of each making its own. All six rulings are built, and the
whole feature was validated end to end in the desktop harness on 2026-09-24 by
the plan's `validate-combined-import` entry; what that run changed is recorded
under [Deviations](#deviations).

## The six rulings

For the `confirm-rulings` entry, in one screen. The reasoning is in
[Decision](#decision); each ruling links to its section.

1. **[Identity](#1-one-roadmap-item-per-plan-identity-is-the-tag-nothing-new).**
   One Roadmap Item per imported plan. The item's Roadmap Tag is the plan's
   shared tag with the sigil lifted: tasks carry `+slug`, `import_plan_id`
   **keeps** the sigil (`+slug`, exactly what `ImportPlanCommand.SharedTag`
   returns today), the item holds the bare `slug`, and `CarriesPlanTag` is the
   one place the two meet. A plan stays a tag inside Tasks — ADR 0007 is not
   reopened, no new table. A plan written `#slug` still imports and still gathers,
   and is not offered as a plan tag anywhere.
2. **[A `plan` entry](#2-a-plan-entry-is-the-same-grammar-with-a-fourth-type-word).**
   A roadmap-level entry is the same entry-text grammar with the bare type word
   `plan`. It maps title → item title, its `+slug` → Roadmap Tag (required;
   without one the entry is reported and skipped), `repo:` → Repository Scope,
   `*priority` → Planning Priority, `after:` → roadmap Dependency (two passes
   against sibling `plan` entries' `id:`, `id:` defaulting to the tag; a cycle
   refuses the whole import), `due:` →
   Planned Window end. `effort:` is not honoured; no start-date token exists. A
   `plan` entry never becomes a Task, and outside Import it is refused.
3. **[Combination](#3-one-document-two-kinds-one-path).** One document may hold
   `plan` entries and task entries together, or only one kind. Task entries are
   created exactly as today, first; `plan` entries are then handed from Tasks'
   Import to Roadmap through a Tasks-side port, `IRoadmapPlanIntake`, answered by
   an adapter under `Backlog.Infrastructure.FileSystem/Roadmap` that calls
   Roadmap's own import command — the join shape `RoadmapPlanTagSource` already
   uses the other way. Both orders work, and a task-only document may be laid out
   through a dialog option, one item per distinct plan tag.
4. **[Placement and velocity](#4-the-importer-places-the-window-velocity-is-the-readers).**
   End is `due:` when written. Start is never read from the document: it is the
   day after the latest end among the item's roadmap predecessors, else today.
   Without `due:`, length is gathered effort ÷ a workspace "story points per day"
   setting (velocity, default 1), rounded up, never under 1 day, and 5 days when
   nothing is gathered or nothing is estimated. Velocity is a reading preference
   the person owns, not an estimate the plan registers. *Amended 2026-09-25: the
   setting is story points **a week**, default 7, and the length is effort × 7 ÷
   it in calendar days — see [Deviations](#deviations).*
5. **[Provenance and re-import](#5-placed_by_import-and-what-a-re-import-may-touch).**
   An import-created item carries `placed_by_import`, cleared the moment a person
   reschedules it by hand. A roadmap-level re-import, matched by tag, replaces
   title, repository scope, priority and dependencies; re-places a window still
   `placed_by_import`; keeps a hand-moved one; deletes nothing. A tag several
   items carry updates the first by creation order and is reported. A task-level
   re-import changes nothing on the item except — while still `placed_by_import`
   by effort — its length.
6. **[Steps inside the item](#6-drawing-the-plans-steps-inside-its-item).** On the
   timeline an item expands into the tasks it gathers, ordered by the tasks' own
   `after:` (topological, ties by creation order), each step's width proportional
   to its effort within the item's window, unestimated steps at a minimum width
   with a marker, coloured by task status; the item bar carries a progress fill of
   done effort over total effort with the unestimated count beside it. Progress
   is read from Tasks and never stored.

## Context

ADR 0007 settled what a plan *is* inside Tasks: entry text with more than one
`#`-titled entry, brought in by Import, identified by the tag every entry shares,
re-imported by clearing what nobody started and writing the new version. It said
nothing about the roadmap, because at the time there was nothing to say — the
Roadmap Planning context was modelled later, and its
[Roadmap Item](../../.domain/roadmap/domain.md#roadmap-item) grew the one
capability that makes this record possible: it **gathers by tag**. A task filed
under an item's Roadmap Tag is reached without the item naming it, and the item
totals the effort those tasks registered
([Roadmap Item Gathering](../../.domain/roadmap/domain.md#roadmap-item-gathering)).

Pull request 484 then changed how a task spells that tag, and documented it in
`.domain/tasks/naming.md` and nowhere else. `EntryTextParser` now reads a `+slug`
token on the metadata line as a plan tag and stores it **with** the sigil, beside
the bare `slug` a `#slug` token becomes; `RoadmapPlanTagSource` offers the
roadmap's tags to the task picker as `+slug`; and `RoadmapItemRollupBuilder.CarriesPlanTag`
lifts the `+` before comparing a task's tag with the item's bare slug. So today,
already: a plan's tasks carry `+roadmap-imported-plans`,
`ImportPlanCommand.SharedTag` returns that same `+roadmap-imported-plans` and it
is written into `import_plan_id`, and an item whose Roadmap Tag is
`roadmap-imported-plans` gathers all of them. Three spellings of one slug, joined
at exactly one point. This record's first job is to say that this is the design
and not an accident, because `.design/content-editing.md` still says the sigil
namespace is closed at five.

The thing nobody has decided is what an imported plan looks like *on the
roadmap*. Today a person imports a plan, gets its tasks, and then — if they want
to see it in time — creates a Roadmap Item by hand, types its tag to match, and
guesses a window. The item then gathers the tasks and totals their effort, but
shows nothing of how the plan is going or which step is where. The
[tracks idea](../../.domain/roadmap/features.md#sequencing-work-into-tracks)
asked the larger question — whether a roadmap should be dated at all, or ordered
by chains and sized by effort — and left it open. Two of its open questions are
answered here, one of its claims is wrong and is corrected here, and the rest
stay open.

Three constraints from the material read shape every ruling:

- **Roadmap invents no estimate.** The plan "invents no estimate for anything
  that registered none, and owns none of the values it adds"
  (`.domain/roadmap/domain.md#roadmap-plan`). Any length the importer computes
  has to come from registered effort and a factor the *person* owns, or it is an
  estimate the plan made up.
- **Roadmap subscribes to nothing.** "A plan changes because a person changed
  it" (`.domain/roadmap/dependencies.md`). Anything that writes to the plan from
  outside has to be a person's own gesture, not a reaction to an event.
- **Modules do not see each other.** Tasks may not reference Roadmap and Roadmap
  may not reference Tasks (`ModuleBoundaryTests`); every join lives in an
  infrastructure adapter that may see both — `RoadmapPlanTagSource` and
  `RoadmapItemRollupService` are the two that exist.

## Decision

### 1. One Roadmap Item per plan; identity is the tag, nothing new

**An imported plan is represented on the roadmap by exactly one Roadmap Item,
whose Roadmap Tag is the plan's shared tag with the sigil lifted.** Nothing new
is invented for identity: the item is the plan's planning-side record, the tag is
the plan's identity in both contexts, and the existing gather-by-tag is what ties
them.

The three spellings are settled as follows, and the choice is *where the sigil is
lifted*:

| Where | Spelling | Why |
|---|---|---|
| A task's `tags` | `+slug` | What pull request 484 stores; what the picker offers; what tells a plan tag from a `#` general tag on a line a person reads. |
| `import_plan_id` | `+slug` — **keeps the sigil** | It is the shared tag *as the tasks carry it*, verbatim. ADR 0007 defines it as "populated from that shared tag", and `SharedTag` returns the sigilled value today. One comparison rule inside Tasks — ordinal equality against the task's own tags — and no second spelling to keep in step. |
| The item's Roadmap Tag | `slug` — bare | The roadmap's tag is a slug other contexts borrow; a sigil is a task-side spelling (`.domain/tasks/naming.md#roadmap-tag`), and a knowledge chapter's `roadmap` list names the bare slug too. |

The sigil is lifted in **one** place: at the boundary into Roadmap.
`CarriesPlanTag` already does it on the read path; the intake adapter of
[ruling 3](#3-one-document-two-kinds-one-path) does it on the write path, and
nothing else may. Dropping the sigil from `import_plan_id` instead was
considered and rejected: it would require rewriting every `import_plan_id`
stored since pull request 484 (a persisted identifier, and a rename that loses
state silently), and it would make a plan imported as `#slug` and one later
imported as `+slug` collide into one id when they were brought in as two.

**Re-import matching stays consistent with that choice.** Tasks matches a later
version by ordinal equality on `(import_plan_id, import_item_id)` exactly as it
does today, sigil included. Consequently a plan first imported under `#slug` and
later re-imported under `+slug` is **two plans** to Tasks: the second import
clears nothing of the first. That is accepted, and it is what
`plugins/backlog-tools`' grammar already tells its readers ("a plan written
`#slug` would be a different plan from the one the app shows").

**A plan written with a general `#slug` still imports and still gathers.** It
imports because ADR 0007 never enforced a tag shape; it gathers because the
bridge accepts a bare tag on the task (`CarriesPlanTag` compares the bare form,
whichever way it was written). What it is not is *offered*: `RoadmapPlanTagSource`
offers `+slug` only, the task picker shows a bare `slug` as a `#` general tag, and
the shelf and the layout option of [ruling 3](#3-one-document-two-kinds-one-path)
key on `+` tags alone. A legacy plan reaches the roadmap through a hand-made item
whose tag matches, and through nothing automatic.

**A plan stays a tag inside Tasks.** ADR 0007's storage decision — through
`ITaskRepository`, no new table, no kept raw text — is not reopened. Roadmap gains
no table either: an item is a node in the one `roadmap_plan` document row
(`.arc42/08-crosscutting-concepts.md`), and the fields this record adds to it
([ruling 5](#5-placed_by_import-and-what-a-re-import-may-touch)) are fields on
that node.

### 2. A `plan` entry is the same grammar, with a fourth type word

**A roadmap-level entry is entry text, discriminated by the bare type word
`plan` on its metadata line.** It is not a second format, for the reason ADR 0007
gave for the task-level plan: everything a Roadmap Item needs to be created —
a title, a tag, repositories, a priority, what it waits on, a date — is a fact
the grammar already has a token for. The type word is bare because the type is
the one thing an entry *is* (`.design/content-editing.md#structured-metadata-sigils`),
and `plan` joins `prompt`, `task` and `idea` in that position.

`plan` is a type word in the grammar and **not** a value of Tasks' `Task Type`
enum. `EntryTextParser` recognises it as the segment's kind and leaves the
entry's type unset; a `ParsedEntry` of that kind carries the same fields as any
other and is what Import hands across. The mapping:

| On the `plan` entry | On the Roadmap Item | Rule |
|---|---|---|
| `# Title` | `title` | As written. |
| `+slug` | Roadmap Tag, bare | **Required, and exactly one.** A `plan` entry with no `+` tag, or with more than one, is reported in the import result and skipped; it is not created under a guessed tag, because the tag is the one thing that lets a task-level import find the item later. |
| `id:` | — (resolution only) | Names the entry for siblings' `after:`. **Defaults to the tag** when absent, so a roadmap document need not write both. |
| `after:` (repeatable) | Dependency | Resolved in two passes exactly as ADR 0007 resolves task ids, against sibling `plan` entries' `id:` first — see below. |
| `repo:` (repeatable) | Repository Scope | Each value is an alias, held as written (`.domain/roadmap/domain.md#repository-scope`). The Import dialog's repository matching applies to it as to any `repo:`; an unmatched name is **not** registered — registration is Import's leniency for tasks that need a `repo_id`, and an item never needs one. It reads as unresolved, which is the roadmap's ordinary state for an alias the registry does not hold. |
| `*priority` | Planning Priority | The same four words. Absent → `medium`. |
| `due:` | Planned Window `end` | A day, inclusive, as every window is. |
| body prose, `##` sub-items, checklist lines | `notes` | The body verbatim. A `plan` entry's body is what the plan is about, and the item has a place for prose already. |
| `effort:` | — | **Not honoured.** The plan owns no estimate (`.domain/roadmap/domain.md`); an item's size is the effort it gathers. The token is reported once in the import result so a person who wrote it learns why the item shows a gathered total instead. |
| `!status`, `@area`, `#tag`, `remind:`, `repeat:`, `myday:`, `files:`, `view:` | — | Not honoured; each describes a task. Ignored without a report — they are ordinary tokens the reader may have copied from a task entry, not mistakes worth interrupting for. |

**No new token is added for a start date.** The start is the importer's to place
([ruling 4](#4-the-importer-places-the-window-velocity-is-the-readers)); a
`start:` token would put a date in the document that the first re-import would
have to argue with.

**`after:` resolves in two passes, against the roadmap.** Pass one parses every
`plan` entry and collects its `id:` (or its tag, when it wrote none). Pass two
creates or matches the items and resolves each `after:` value in a fixed order of
confidence: a sibling `plan` entry's `id:` in the same document; then the Roadmap
Tag of an item already on the plan, when exactly one item carries it; then a real
`roadmap_item_id` or `roadmap_milestone_id`, as any dependency may name a
milestone. A value that resolves to nothing is dropped and reported rather than
stored — the aggregate rejects an edge to an unknown node, and this record does
not soften that invariant. A value that would close a cycle **refuses the whole
import**, through the aggregate's own acyclicity check, and the stored plan is
left exactly as it was: nothing created, nothing revised. *(Amended on
acceptance. As proposed, the one edge was dropped and the item still created;
that half-applies an import, which the roadmap refuses for every other edit.)*

**A `plan` entry never becomes a Task.** Only Import may act on one. Every other
path that turns entry text into a task — the editor's paste, the quick-add,
sub-item creation — refuses a segment of kind `plan` with a clear error naming the
way in ("a `plan` entry is a roadmap item; bring it in through Import") rather
than creating a task titled after it. Silent creation would leave a task carrying
a type word the task model does not have, that the canonical rewrite would then
drop — exactly the data loss `.design/content-editing.md`'s canonical-rewrite
rule exists to prevent.

### 3. One document, two kinds, one path

**One document may hold `plan` entries and task entries together, or only one
kind.** `SplitSegments` already yields one segment per `#` heading whatever the
kinds; Import sorts the segments by kind after parsing and nothing about the
split changes.

**Task entries are created exactly as today**, by the unchanged ADR 0007 path:
shared tag, clear-then-write, two-pass `after:`. The shared tag
(`import_plan_id`) is computed over the **task entries alone**; `plan` entries
take no part in it, because a roadmap-level document naturally holds several
plans and would otherwise share nothing. A task entry does **not** inherit a
`plan` entry's tag when it wrote none: the grammar stays literal, and a task
without the tag is the accepted ADR 0007 limitation it always was.

**`plan` entries are handed from Tasks to Roadmap through a port.** The order
inside one Import run is fixed: the task entries are persisted **first**, then
the `plan` entries cross. Placement ([ruling 4](#4-the-importer-places-the-window-velocity-is-the-readers))
reads the effort the item gathers, and an item placed before its tasks landed
would be placed against the previous version's effort.

- **Port:** `IRoadmapPlanIntake`, in `Backlog.Modules.Tasks.Abstractions/Services`
  beside `IRoadmapTagSource`. Tasks' Import calls it once per run with two
  things: the `plan` entries the document held (title, tag, ids, dependencies,
  aliases, priority, due date, notes — plain values, no parser type crosses),
  and the distinct `+` plan tags the document's task entries carried.
- **Adapter:** `RoadmapPlanIntake`, under
  `Backlog.Infrastructure.FileSystem/Roadmap`, registered in
  `RoadmapCrossContextAdapterRegistration` as `Scoped` like its two siblings. It
  lifts the sigil, and calls Roadmap's own import feature slice.
- **Roadmap command:** a new feature slice in `Backlog.Modules.Roadmap`,
  `Features/ImportPlanItems`, to be exposed on `IRoadmapPlanning` by the entry
  that builds the intake port, that creates or updates one item per `plan` entry
  ([ruling 5](#5-placed_by_import-and-what-a-re-import-may-touch)), re-places the
  import-placed items whose tag the task entries touched, and — when asked —
  creates an item for each touched tag that has none yet.

This is the join shape `RoadmapPlanTagSource` already uses in the other
direction: a module asks its own port, an adapter that may see both answers it,
and `ModuleBoundaryTests` stay green because neither module references the
other. A domain event from Tasks that Roadmap subscribed to was the alternative,
and it is rejected on the roadmap's own terms: "Roadmap Planning publishes to
Monitoring and subscribes to nothing. A plan changes because a person changed
it." **A person pressing Import is a person changing the plan** — the gesture
carries the intent, the port carries the gesture, and the plan is written by a
command in its own module. `.domain/roadmap/dependencies.md` gains this inbound
edge, and its note is reworded to say that what Roadmap does not do is *react*;
being *told* by a person's command is how every edit reaches it.

**Both orders work.**

- *Roadmap first.* A roadmap-level document creates the items, each placed by
  [ruling 4](#4-the-importer-places-the-window-velocity-is-the-readers) with
  nothing gathered yet (the default span). Later task-level imports fill each
  item by tag — the gather is by tag, so nothing links them — and re-length it.
- *Tasks first.* Task-level plans import as today. Their `+` tags with no item
  under them appear on a **shelf** on the roadmap (entry `unplanned-plans-shelf`),
  read from Tasks through a Roadmap-side read port answered by the same adapter
  family; picking one creates its item, placed by ruling 4 from the effort
  already there.
- *Task-only document, laid out at once.* The Import dialog offers "Lay out on
  the roadmap". With it on, a document with task entries and no `plan` entries
  creates one item per distinct `+` plan tag its entries carry, placed the same
  way. It is off by default: a plan re-imported for its tasks should not grow an
  item the person did not ask for.

### 4. The importer places the window; velocity is the reader's

**End.** A `plan` entry with `due:` ends on that day.

**Start is never read from the document.** It is the day after the latest `end`
among the item's roadmap predecessors — the nodes its `after:` resolved to, a
milestone counting with the day it falls on — or **today** when it has none.
`plan` entries in one document are placed in the topological order their
dependencies imply, so a predecessor is placed before anything waits on it; a
predecessor outside the document contributes the window it has now.

When a `due:` end falls before the computed start, the item is placed as a
one-day item on its due date: `start = end = due`. The person's date is kept,
and [Plan Sequencing](../../.domain/roadmap/domain.md#plan-sequencing) reports
the contradiction — a successor opening before its predecessor closes — exactly
as it does for a hand-typed plan. A contradiction is reported, never corrected.

**Length, when there is no `due:`.** The item's `end` is `start + days − 1`,
where

```
days = ceil(total registered effort ÷ velocity)   — never under 1
days = 5                                           — when nothing is gathered, or nothing gathered is estimated
```

in **calendar days**, because the plan is drawn against a calendar and Roadmap
models no working week; a working-day rule would be a new fact this record does
not introduce. The minimum of 1 keeps a window a window (`end >= start`); the
default of 5 is a working week, the smallest span that reads as "a plan" rather
than "a day", and it applies whenever the total would be a number invented from
nothing.

**Velocity** is a workspace-level setting, *story points per day*, default `1`,
kept with the other workspace settings (`.arc42/08-crosscutting-concepts.md`:
per-device JSON, deliberately not replicated). Entry `velocity-setting` builds
it. It is stated plainly: **velocity is a reading preference the person owns,
not an estimate the plan registers.** The plan still invents no estimate — the
effort is the tasks' own, registered in Tasks, and the only thing added is the
person's own statement of how fast *they* work through points. A window it
produces is stored like any other window and is not recomputed when the setting
changes; it is re-placed only by a re-import
([ruling 5](#5-placed_by_import-and-what-a-re-import-may-touch)). *Amended
2026-09-25: the setting is points a week, and a person changing it re-lengthens
every window still sized by effort — ruling 5 and [Deviations](#deviations).* So
`.domain/roadmap/domain.md`'s invariant stands with one word added to it: the
total is plain arithmetic over registered points, and the *length* is plain
arithmetic over that total and a factor the person set.

Placement writes the window through the same path a hand edit does, so
`RoadmapItemScheduled` is published as for any newly planned item — with no
previous window on creation, and with the previous one on a re-placement.
Monitoring sees an imported plan the way it sees every plan. No publisher exists
for the event yet, for hand edits or imports alike; until one does, the import's
result carries the event's payload for every window it set or moved, so the
publisher has one place to read it from.

### 5. `placed_by_import`, and what a re-import may touch

**An item created by import carries `placed_by_import`**, a field on the item
node. It is not a boolean but a small value with three states, because a later
re-import has to know *which rule* placed the window:

| Value | Meaning |
|---|---|
| `effort` | The importer set both ends: start from predecessors, length from gathered effort and velocity. |
| `due-date` | The importer set the start from predecessors and the end from the entry's `due:`. |
| absent | A person placed it — by hand at creation, or by rescheduling since. |

Either present value reads as "still placed by import"; the prompt's boolean
reading holds. **It is cleared the moment a person reschedules the item by
hand** — a drag, a keyboard move, an edit that changes the window — and never
set again except by a fresh import creating the item anew. A window a person
touched is a decision, and the importer does not overrule decisions.

**Re-importing a roadmap-level document**, matched by tag (bare slug against
Roadmap Tag; when more than one item carries the tag, the **first by creation
order** is updated and the ambiguity is reported, naming the others, so the
person sees it rather than finding a second item quietly created beside them —
*amended on acceptance; as proposed, none was updated*):

- **Replaced** from the new version: title, repository scope, planning priority,
  dependencies (the import's set replaces the item's set; a dependency a person
  added by hand to an import-created item is therefore lost on re-import — an
  accepted cost, recorded below), and notes.
- **Re-placed** when the window is still `placed_by_import`: start from
  predecessors as they stand now, end from `due:` or from length, and the value
  updated to whichever rule applied. A hand-moved window is **kept**, dates and
  all.
- **Never deleted.** A plan the document has stopped describing stays planned.
  This is the opposite of ADR 0007's clear-then-write for tasks, deliberately: a
  not-yet-started task holds nothing worth keeping over what the plan now says,
  but a Roadmap Item is a *node in a graph other things hang on* — other items
  wait on it, Monitoring has consumed its window, a person may have hand-placed
  it — and a roadmap document is one source of items among several, not the
  authority for which items exist. Taking work off the plan is the person's own
  gesture ([editing the plan in place](../../.domain/roadmap/features.md#editing-the-plan-in-place)).

**Re-importing the task-level plan under an existing item changes nothing on
the item** — not its title, scope, priority or dependencies, which the tasks
never carried — **except, while still `placed_by_import` = `effort`, its
length**: the end is recomputed from the newly gathered effort, the start stays.
A `due-date`-placed item keeps its end, because that end is a date the person
wrote; a hand-placed item is untouched.

**A person changing the pace re-lengthens by the same rule** — *amended
2026-09-25, on the owner's request; as accepted, a window was stored and not
recomputed when the setting changed.* Typing a new pace while it is the one in
use, or choosing another, recomputes the end of every item still `effort`-placed
from what it gathers now, keeping the start; `due-date` and hand-placed items are
untouched, and nothing that waits on a re-lengthened item moves. It is still a
person's gesture that moves a window: finished work shifting a measured pace
moves no bar until the next change, import, or update from tasks.

### 6. Drawing the plan's steps inside its item

**On the timeline an item expands into the tasks it gathers**, drawn as steps
inside the item's bar. This is the reading the tracks idea wanted — effort, not
dates, saying how big each piece is — inside the dated window the roadmap keeps.

- **Order:** topological over the gathered tasks' own `after:` dependencies,
  considering only edges between tasks the item gathers; ties broken by creation
  order (creation time, oldest first). A cycle among them — Tasks tolerates one —
  is broken at the youngest task for drawing, and marked.
- **Width:** each estimated step takes `effort ÷ total estimated effort` of the
  item's window width. Each unestimated step takes a fixed **minimum width** and
  wears a marker saying it is unsized; the estimated steps share what remains.
  Positions are **proportions, not dates**: a step drawn across the 14th is not
  scheduled for the 14th, and the surface says so where a reader could think
  otherwise.
- **Colour:** by task status, the status vocabulary Tasks owns.
- **The item bar** carries a **progress fill** of done effort over total
  registered effort, with the **unestimated count shown beside it** — the same
  rule the gathered total already follows: a fill that silently dropped unsized
  work would read as further along than the plan is. A `done` task with no
  estimate counts in that number, not in the fill.

**Progress is read from Tasks and never stored.** `IRoadmapItemRollup` already
returns each gathered task's key, title and effort; it grows the three facts
the drawing needs — status, `depends_on`, creation order — on its
`RoadmapGatheredLink`, and `RoadmapItemRollupBuilder` keeps owning the
arithmetic. Roadmap holds no status, no percentage and no order of its own; the
item still "has no status and no percentage"
(`.domain/roadmap/domain.md#roadmap-item`), it draws the tasks'.

**What this answers in the tracks idea.** *Replacement or addition?* — **dates
stay**; effort sizes the steps inside a dated window, so the Planned Window,
the timeline and `RoadmapItemScheduled` all survive and Monitoring's contract
holds. *Does a track hold work, or gather it?* — it **gathers**; the chain
between steps is Tasks' own `after:`, read here and never held, and the chain
between plans is Roadmap's Dependency, as it was. The idea's claim that "Tasks
models no dependency today" has been false since ADR 0007 and is corrected in
that chapter. *Is a track a lane or a new node?* — an imported plan is an
existing node, the item. *Do milestones survive?* — yes, because dates do. The
area question — what makes two plans safe to work at once — is **not** answered
here and stays open.

## Deviations

Where this record departs from what is written or built today, so the entries
that implement it know what they are changing:

- `.design/content-editing.md#structured-metadata-sigils` says the namespace is
  closed at five. Pull request 484 minted a sixth, `+`, without amending it.
  This record admits the sixth and rewrites the rule: a sigil is admitted when
  the *kind* already had one and only the *sub-kind* is new — `+` and `#` are
  both tags, told apart on a line a person reads — and closed against a new kind
  of metadata, which still takes a `name:value` token.
- `.design/content-editing.md#scheduling-and-dependency-tokens` lists no
  `effort:` row although the parser reads it and `.domain/tasks/domain.md`
  models it. The row is added, because the "not honoured on a `plan` entry" rule
  has to hang on it.
- `.domain/roadmap/dependencies.md` says Roadmap subscribes to nothing and lists
  no inbound edge from Tasks. It still subscribes to nothing; it gains an
  inbound *command* edge, and the note is reworded.
- `.domain/roadmap/features.md#sequencing-work-into-tracks` claims Tasks models
  no dependency. It does, `after:`, since ADR 0007; the claim is corrected.
- ADR 0007 speaks of the shared `#tag`. Its addendum points here for the `+`
  spelling; its decisions are unchanged.

Found by the validation run, and changed with it:

- **[Ruling 3](#3-one-document-two-kinds-one-path) computes the shared tag over
  the task entries alone, and a roadmap document's task entries share none
  either.** The reason the ruling gives for leaving `plan` entries out — a
  roadmap-level document naturally holds several plans — is just as true of the
  steps under them, so a document of two plans gave none of its steps a plan
  id, and re-importing it wrote every step a second time. The fix is in ADR
  0007's own Deviations, because the identity rule is that record's: an entry's
  one `+` plan tag is its plan when the document shares no tag.
- **[Ruling 6](#6-drawing-the-plans-steps-inside-its-item) reads the rollup on
  every load, and the band reloaded only when the plan itself changed.** A step
  marked done while the band was on screen stayed drawn as it was until the page
  was reloaded, because a task write is not a plan write. Roadmap now has a
  signal port, `IRoadmapWorkChanges` in
  `Backlog.Modules.Roadmap.Abstractions/Services`, answered in
  `RoadmapCrossContextAdapterRegistration` by `RoadmapWorkChanges`, which
  forwards every local task write heard through Tasks' `ITaskChangeSignal`. The
  band reloads on it as it does on `IRoadmapPlanning.Changed`, coalescing the
  burst an import makes into one reload after another. A write the sync pull
  applies is suppressed at the task signal, so another machine's edit is still
  read on the next load rather than live.

Changed on the owner's request, on 2026-09-25:

- **[Ruling 4](#4-the-importer-places-the-window-velocity-is-the-readers)'s
  velocity is story points a week, not a day.** The setting is kept as
  `storyPointsPerWeek`, default 7 — one a day, so nobody's bars change length —
  and a file holding only the old `storyPointsPerDay` reads as seven times it.
  A measured pace is effort finished over 2, 4 or 8 weeks. Placement multiplies
  by seven before dividing, so 4 points at 4 a week is exactly 7 days rather than
  a hair over, rounded up to 8.
- **[Ruling 5](#5-placed_by_import-and-what-a-re-import-may-touch): a pace change
  re-lengthens every `effort`-placed item**, as amended there. Built as
  `Features/RelengthenPlan`, reached through
  `IRoadmapPlanning.RelengthenPlanFromEffortAsync`; the band gathers each item's
  effort through `IRoadmapItemRollup` and hands it over when the pace control
  reports a change to the pace in use.

Changed on the owner's request, when the roadmap became a full-screen surface:

- **[Ruling 6](#6-drawing-the-plans-steps-inside-its-item)'s expansion is not
  offered in the app.** The roadmap shows plans, not their tasks: the band passes
  `ShowSteps="false"` to `RoadmapTimeline`, so a bar keeps its progress fill and
  its unestimated count but never opens into a row per task. The rollup is still
  read on every load, because the fill is drawn from it, and the library still
  supports the expansion for another host.
- **An item filed in several repositories is drawn once per repository.** Each
  band shows that repository's part, with a fill read from the gathered tasks
  filed there (a task filed nowhere goes to the first part). The parts share the
  item's one Planned Window, so moving any of them reschedules the item; a part's
  bar id is `<item id>@<band>`, and `RoadmapPlanView.NodeIdOf` reads the item
  back out of it. The shelf of plans not yet placed lists each repository's
  share the same way.

## Consequences

Positive:

- A plan is one thing in three places — a tag on its tasks, an `import_plan_id`
  on each, a Roadmap Tag on its item — joined by one slug and one bridge. Nothing
  new is identified, stored twice, or kept in sync.
- The roadmap-level document is the task-level grammar with one more type word.
  Every token added to the grammar later is available on a `plan` entry the day
  it lands, and `plugins/backlog-tools` can emit both kinds from one generator.
- The importer places; the person decides. Every date the importer writes is
  overridable by a drag, and once dragged it is never written again. Velocity
  makes the placement the person's own reading of their pace, not the plan's
  guess.
- The steps drawing gives the tracks idea its effort-sized reading without
  giving up dates, milestones or Monitoring's contract.

Negative:

- Two spellings of the same tag on the same task (`+slug` on a task filed after
  pull request 484, bare `slug` on one filed before) both gather, but only the
  first is offered or shelved. A legacy plan reaches the roadmap by hand only.
- A plan imported once as `#slug` and again as `+slug` is two plans to Tasks and
  the first's not-yet-started entries are not cleared. Accepted, per ruling 1;
  the plugin's grammar already says so.
- A dependency a person added by hand to an import-created item is replaced on
  the next roadmap-level re-import. The alternative — merging the two sets — would
  make it impossible for a document to *remove* a dependency, and the document
  is what the person is editing when they re-import.
- A velocity change does not move anything already placed. A person who changes
  their pace and wants the plan to follow re-imports it; that is one gesture, and
  making the setting reach into stored windows would make a preference an
  editor of the plan.
- A window computed from effort ignores the working week. An item of 10 points
  at velocity 1 spans 10 calendar days, weekends included. Until Roadmap models
  a calendar, that is the honest reading; the person's velocity can absorb it.
- Steps drawn as proportions inside a dated bar look like dates and are not.
  The surface has to say so; entry `steps-in-item` owes that.

Neutral:

- `placed_by_import` is a field on the item node inside the `roadmap_plan`
  document row. Additive, read as absent when missing, and within ADR 0006's
  terms for that row.
- `IRoadmapPlanIntake` is the third cross-context adapter under
  `Backlog.Infrastructure.FileSystem/Roadmap`, and the first that *writes*. The
  write goes through Roadmap's own command and port, so the adapter still holds
  no roadmap logic — it lifts a sigil and delegates.
- The "Lay out on the roadmap" dialog option and the shelf are two doors into one
  command. Neither invents a rule the `plan` entry path does not already follow.
