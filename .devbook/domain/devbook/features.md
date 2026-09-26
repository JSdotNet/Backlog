# Devbook

```meta
type: features
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Knowledge capture

```meta
type: feature
status: draft
related: [.devbook/domain/inbox/features.md#routing]
```

Store notes, references, ideas, and learnings as markdown from inbox triage,
manual creation, or import, attaching them to one or more projects, topics, or
tags.

## PARA organization

```meta
type: feature
status: draft
depends-on: [.devbook/domain/devbook/features.md#knowledge-capture]
```

Organize notes into Projects (active, deadline), Areas (ongoing), Resources
(reference), and Archive (inactive) buckets.

## Cross-project linking

```meta
type: feature
status: draft
```

Reference multiple projects and repos from a single note, and discover notes
across projects by tag.

## Topic and tag grouping

```meta
type: feature
status: draft
related: [.devbook/domain/devbook/domain.md#tag, .devbook/domain/devbook/domain.md#roadmap-contribution]
```

Group notes by topic (not just project), support cross-cutting tags, and search
across all knowledge content via the tag index.

These discovery tags are the context's own `#keyword`s and are a different thing
from a chapter's **roadmap contribution** — the roadmap-item tags a chapter names
in its `roadmap` metadata to say which planned work it feeds. A discovery tag finds
notes here; a roadmap contribution is read by Roadmap Planning when it gathers work
by tag, draws no edge, and is never confused with a `#keyword` even though both are
loosely "tags". A chapter may also declare an `effort` in story points, sized the
same way a Task is; like the roadmap contribution it is registered here
and only read by Roadmap Planning.

## Bi-directional linking

```meta
type: feature
status: draft
related: [.devbook/domain/tasks/features.md#search-filter-and-organize]
```

Link from tasks to notes (reference or embed inline) and from notes
back to related tasks or projects, supporting queries that cross both
domains and embedding knowledge context directly in task details.

## Repository devbook areas

```meta
type: feature
status: draft
feature-flag: .devbook/domain/devbook/context.md#repository-devbook
related: [.devbook/domain/repository-management/features.md#repository-knowledge-folder-settings, .devbook/domain/tasks/features.md#search-filter-and-organize]
```

Read the knowledge a repository already carries alongside its code, next to the
backlog rather than in a separate tool. Knowledge is grouped into named areas —
working instructions, domain, architecture, technology, and design — each backed
by the repository's own folder for that subject. Tasks concerns are not one of
them: they are their own workspace section, read and written rather than browsed,
so a repository's backlog folder is not a knowledge area. Areas are browsed from
a side pane that sits beside the task list so knowledge and work stay in view
together, and the pane's width is adjustable because the two compete for the same
screen.

### Area selection and scope

```meta
type: sub-feature
status: draft
feature-flag: .devbook/domain/devbook/context.md#devbook-sections
```

Switch between areas, and between repositories when more than one is registered,
so the knowledge shown always belongs to a known repository. The pane reads one
repository at a time even when the backlog beside it is scoped to several: it
follows the first repository taken into that scope, so widening the list never
moves the knowledge already being read. With no repository scoped there is
nothing to read and the pane is not offered; the folder the backlog is kept in
is never read as a devbook of its own. Every area is opt-in, and each can be
switched off on its own or pointed at a non-standard folder; when none is left
on there is nothing to browse and the pane says so rather than offering an empty
tab strip.

### Rendered devbook documents

```meta
type: sub-feature
status: draft
```

Present each area's documents as readable content rather than raw files:
headings and sections, the metadata each chapter declares, cross-references
between knowledge documents, and embedded diagrams rendered as diagrams. A
cross-reference may name a chapter in the repository's backlog folder even though
that folder is not a browsable area, and it is read with that folder's own status
vocabulary rather than as an unknown one.

The metadata is read against the devbook convention the repository's own checks
enforce, folder by folder, so a chapter the app shows as fine is not one those
checks reject:

- **A status at rest says nothing.** Architecture, domain and design chapters
  that are simply current carry no status at all and are shown as current;
  choosing "current" for one removes the word rather than writing it, and a
  chapter that spells it out anyway is pointed out. Technology and AI-adoption
  chapters are ratings, so theirs is always stated.
- **A decision is state, not content.** A domain chapter can stand approved —
  someone agreed it — and above that accepted — someone saw the built work
  against it and accepted it. Who decided and when, and where a review stands on
  the way there (requested, changes requested, cleared, and who owes the next
  move), is shown beside the chapter's status rather than among its fields. The
  app never offers those two steps as a status to pick: they are recorded by the
  approval step that makes the decision. Moving a chapter off either step takes
  its decision record with it. No other area has them, and a chapter outside the
  domain that carries one is pointed out.
- **Kinds.** Each area that classifies its chapters is read with its own set of
  kinds, including a domain context's boundary document, its requirements and
  invariants and the single rules inside them, its switches and settings, its
  actors, and a page of its own that the convention does not name. How a
  bounded context ships — as a service of its own or as a module inside a shared
  host — is shown with it, and said out loud when the context map and the
  context's own boundary document disagree about it.
- **Everything else a chapter declares** — the tests that back it, its number
  and place in the reading order, the day it records — is shown as it was
  written, and state another tool keeps on the chapter under its own name is
  carried through untouched.

### Remarks on a chapter

```meta
type: sub-feature
status: active
related: [.devbook/arc42/adr/0011-devbook-annotations-are-a-third-replica-container.md, .devbook/arc42/08-crosscutting-concepts.md#storage-and-sync]
```

Let a reader leave a remark against one block of a chapter while reading it —
a question, a flag, a note to self — beside the prose rather than inside it,
and keep it. A remark belongs to the person, not to the repository: it is kept
by the app, moves with the person's backlog, reaches their other devices
through sync, and is never written into the repository's knowledge folders —
which is what lets a reader remark on a chapter read from a branch nobody can
edit. A remark can be resolved, which keeps it visible and quiet, or deleted.

This is a different thing from the devbook convention's own note, the
`annotation` fence written into a chapter for a review to read: that is a
shared repository artefact with its own lifecycle, shown as a
[review note](#review-notes-in-a-chapter). Turning a private remark into one is a
possible later step, not something the app does today.

A remark is anchored to the block it was left on, and remembers what that block
said. When the chapter changes above it the remark follows its own passage to
wherever it has moved to. A remark whose passage has been deleted, or rewritten
into something else, is shown at the end of the chapter rather than lost.

### Review notes in a chapter

```meta
type: sub-feature
status: active
related: [.devbook/arc42/adr/0011-devbook-annotations-are-a-third-replica-container.md, .devbook/arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md, .devbook/domain/devbook/features.md#remarks-on-a-chapter]
```

Show the review notes a repository keeps in its own chapters — a comment, a
question, a suggestion or a flag, written beside the passage it is about, with
its replies — as notes attached to that passage, open or resolved, rather than
as the raw text they are stored in. They are the repository's shared review
state: whoever reviews the chapter reads the same notes, and they leave with the
change that resolves them.

A review note is never read as what the chapter says. An open question about
whether a rule still holds is not the rule, so anything the app hands on as the
chapter's content — to an assistant, to search, to an excerpt — leaves the notes
out, together with where the chapter's review stands.

The app shows these notes and does not write them; the convention's own tooling
does. They are also not the reader's [remarks](#remarks-on-a-chapter): a remark
is the person's own and stays in the app, a review note is the repository's and
stays in the chapter, and the two are shown apart and never merged — the
decision local ADR 0011 records.

### A devbook that stays current

```meta
type: sub-feature
status: draft
related: [.devbook/arc42/08-crosscutting-concepts.md#devbook-database]
```

Show what a repository's folders actually say, including when a chapter was
edited outside the app — by another tool, by a pull request, or by hand in an
editor. The folders are the source of truth and are never owned by the app; a
prepared view over them is a convenience that has to prove itself current before
it is shown, and has to give way to reading the folder directly when it cannot.

A repository where nothing has been prepared is a normal state, not an error: its
knowledge is still browsable, only slower. Preparing it is never a precondition
for reading it.

Preparation happens without being asked for and without being waited on — never
at the cost of opening the app, and never at the cost of drawing a pane that is
ready to show something. A chapter edited moments ago is shown from the folder
itself rather than held back until the prepared view catches up, so what is on
screen is never older than what is on disk.

The prepared view is the app's own and is kept with the app, never in the
repository: nothing is added to a repository's folders, and nothing there needs
to be told to ignore it. It is prepared per copy of the repository, so two
clones or two worktrees of one repository are each prepared from their own
files, and a repository read from a branch without a clone is prepared too.

### The devbook on the repository's latest version

```meta
type: sub-feature
status: draft
related: [.devbook/domain/repository-management/features.md#repository-knowledge-folder-settings]
```

Say whether the knowledge in view is the latest version the repository has, and
bring it up to date when it is not. A repository's folders are read from a local
clone, and a clone falls behind in silence: every chapter still opens, and nothing
in one says that somebody else has revised it since.

The question is asked only when somebody asks it. Answering means reaching for the
repository itself, and knowledge that reached out on its own would make opening a
folder wait on the network for an answer nobody wanted yet.

The answer says how far behind the clone is, and offers to bring it up to date only
when being behind is all that is in the way. A clone carrying edits nobody has
committed, one holding revisions the repository does not have, or one whose history
has parted from the repository's is reported as it stands rather than reconciled on
the reader's behalf: what to do about any of those belongs to whoever owns the clone.
Knowledge kept outside a repository has no latest version to be on, and is not asked
about.

Bringing a clone up to date replaces what its folders say, so the knowledge on screen
becomes the knowledge that arrived with it. The folders are the source of truth before
and after, by the rule above.

## Instruction optimization

```meta
type: feature
status: draft
feature-flag: .devbook/domain/devbook/context.md#instruction-optimization
depends-on: [.devbook/domain/devbook/features.md#repository-devbook-areas]
related: [.devbook/domain/devbook/domain.md#instruction-review, .devbook/domain/repository-management/features.md#repository-knowledge-folder-settings]
```

Review the working instructions as what they are — documents whose readers are
mostly not people. The coding agents working in the repository read them on every
run, each tool reads its own files, and the area grows by addition because adding
a rule feels safe and removing one feels risky. Findings say what an instruction
set costs, where it disagrees with itself, and what would make it sharper; the
repository keeps ownership of every word, so a finding is a proposal and nothing
is rewritten unattended.

Two costs are named separately and never traded silently. **Context load** is what
an always-loaded instruction spends on every agent turn whether or not it applies.
The other is the person's own cost of knowing which document to reach for, which
is what buys human judgement and is not a number to drive to zero. An instruction
set that got smaller while the agents got worse has failed, and the review reports
that rather than a saving.

### Instruction set inventory

```meta
type: sub-feature
status: draft
related: [.devbook/domain/devbook/domain.md#instruction-set]
```

See every instruction document the repository carries and which tool reads it: the
file a tool loads on every run, the ones it loads only when a condition matches,
and the skills it can reach. Each is shown with what it costs to load and whether
that cost is paid always or only on the branch that reaches it. A document
belonging to a tool the product does not recognize is listed as exactly that
rather than dropped, because the next tool arriving is a normal event.

### Context-load budget

```meta
type: sub-feature
status: draft
related: [.devbook/domain/devbook/domain.md#context-load]
```

Weigh a repository's always-loaded instruction weight against a budget and rank
what carries that weight, heaviest first, with the one change that would lighten
each. The ranking follows what a line actually costs across the turns that pay for
it rather than how long its file is, so a short document loaded every run outranks
a long one reached twice a month.

### Duplicate rule detection

```meta
type: sub-feature
status: draft
related: [.devbook/domain/devbook/domain.md#instruction-alignment]
```

Surface a rule stated in more than one place — across two documents, or twice
inside one — and name the single place that should own it. The half that matters
is the same meaning in different words; identical sentences are the easy case. A
candidate is offered for judgement rather than merged, because two rules that read
alike are sometimes two different rules and collapsing them loses one.

### Cross-tool alignment validation

```meta
type: sub-feature
status: draft
related: [.devbook/domain/devbook/domain.md#instruction-alignment]
```

Check whether the instruction sets different tools read for one repository still
agree where they overlap, and surface a rule one tool is told and another is not.
Two documents stating one rule in each tool's own agent, skill, and command names
are **aligned**, not duplicated: both are meant to keep saying it, so the finding
is a disagreement or an omission and never the fact that both exist. Documents
maintained as a pair are named as a pair, so editing one raises the other as work
not yet finished.

### Pointer and phrasing review

```meta
type: sub-feature
status: draft
```

Judge the wording that is supposed to do the work. A line naming a document held
elsewhere is judged on whether it will actually make an agent reach for it, and on
whether the conditions it lists cover the cases that document handles — must-have
guidance behind wording that only sometimes fires is worse than guidance that is
absent, because it fails unpredictably rather than visibly. A rule written as a
prohibition is judged the same way and offered back stated as the behaviour that is
wanted, since naming the banned behaviour is what puts it in front of the reader.

### Prune candidates

```meta
type: sub-feature
status: draft
```

Surface the lines that no longer earn their load: guidance the agent already
follows without being told, guidance that has gone stale against the behaviour it
describes, and guidance that restates what the repository's own configuration,
scripts, or folder layout already state — a copy that can only drift out of date.
These are proposals for review, because whether an instruction changes anything is
settled by running the agent without it rather than by arguing about it.

### Reviewed change proposals

```meta
type: sub-feature
status: draft
related: [.devbook/domain/devbook/domain.md#instruction-review]
```

Apply an accepted proposal one at a time on an explicit yes, with the previous
wording recoverable exactly as it was, then measure what the change did and offer
to withdraw it when the effect is not an improvement. Code blocks, commands, paths,
and error strings pass through a rewrite unchanged: they are the part of an
instruction that has to be exact.

### Saving evidence

```meta
type: sub-feature
status: draft
related: [.devbook/domain/devbook/domain.md#saving-evidence]
```

State how every claimed reduction was obtained — read from this machine's own agent
activity, or measured in a controlled before-and-after — and express it as a
reduction in what agents load rather than as money. A figure with no stated basis
is not shown.

## Devbook retrieval

```meta
type: feature
status: draft
related: [.devbook/domain/devbook/features.md#topic-and-tag-grouping, .devbook/domain/devbook/features.md#repository-devbook-areas, .devbook/domain/tasks/features.md#search-filter-and-organize, .devbook/arc42/08-crosscutting-concepts.md#devbook-database]
```

Find the chapter that answers a question across every area and every note at
once, both by the words a chapter uses and by what it means, so a recollection
that does not share the author's vocabulary still lands. Results name the chapter
they came from rather than returning loose text, because a chapter address is
what every other part of this context already links by.

This is the same surface that knowledge-backed AI assistance reads, so an answer
can always cite the chapters behind it. Retrieval by meaning is additive: when it
is unavailable, retrieval by words alone still answers, and neither is a
precondition for browsing an area.

Retrieval is the one capability that a repository nothing has prepared cannot
offer, because answering a question by reading every chapter each time is not a
slower answer but no answer. Where browsing quietly falls back to the folder,
search says plainly that it has nothing to search yet, and what would make it
available.
