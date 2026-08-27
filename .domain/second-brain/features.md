# Second Brain

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
related: [.domain/inbox/features.md#routing]
```

Store notes, references, ideas, and learnings as markdown from inbox triage,
manual creation, or import, attaching them to one or more projects, topics, or
tags.

## PARA organization

```meta
type: feature
status: draft
depends-on: [.domain/second-brain/features.md#knowledge-capture]
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
related: [.domain/second-brain/domain.md#tag, .domain/second-brain/domain.md#roadmap-contribution]
```

Group notes by topic (not just project), support cross-cutting tags, and search
across all knowledge content via the tag index.

These discovery tags are the context's own `#keyword`s and are a different thing
from a chapter's **roadmap contribution** — the roadmap-item tags a chapter names
in its `roadmap` metadata to say which planned work it feeds. A discovery tag finds
notes here; a roadmap contribution is read by Roadmap Planning when it gathers work
by tag, draws no edge, and is never confused with a `#keyword` even though both are
loosely "tags". A chapter may also declare an `effort` in story points, sized the
same way a Backlog Entry is; like the roadmap contribution it is registered here
and only read by Roadmap Planning.

## Bi-directional linking

```meta
type: feature
status: draft
related: [.domain/backlog/features.md#search-filter-and-organize]
```

Link from backlog entries to notes (reference or embed inline) and from notes
back to related backlog items or projects, supporting queries that cross both
domains and embedding knowledge context directly in backlog item details.

## Repository knowledge areas

```meta
type: feature
status: draft
feature-flag: repository-knowledge
related: [.domain/repository-management/features.md#repository-knowledge-folder-settings, .domain/backlog/features.md#search-filter-and-organize]
```

Read the knowledge a repository already carries alongside its code, next to the
backlog rather than in a separate tool. Knowledge is grouped into named areas —
working instructions, domain, architecture, technology, and design — each backed
by the repository's own folder for that subject. Backlog concerns are not one of
them: they are their own workspace section, read and written rather than browsed,
so a repository's backlog folder is not a knowledge area. Areas are browsed from
a side pane that sits beside the entry list so knowledge and work stay in view
together, and the pane's width is adjustable because the two compete for the same
screen.

### Area selection and scope

```meta
type: sub-feature
status: draft
feature-flag: knowledge-sections
```

Switch between areas, and between repositories when more than one is registered,
so the knowledge shown always belongs to a known repository. Every area is
opt-in, and each can be switched off on its own or pointed at a non-standard
folder; when none is left on there is nothing to browse and the pane says so
rather than offering an empty tab strip.

### Rendered knowledge documents

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

## Instruction optimization

```meta
type: feature
status: draft
feature-flag: instruction-optimization
depends-on: [.domain/second-brain/features.md#repository-knowledge-areas]
related: [.domain/second-brain/domain.md#instruction-review, .domain/repository-management/features.md#repository-knowledge-folder-settings]
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
related: [.domain/second-brain/naming.md#instruction-set]
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
related: [.domain/second-brain/naming.md#context-load]
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
related: [.domain/second-brain/naming.md#instruction-alignment]
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
related: [.domain/second-brain/naming.md#instruction-alignment]
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
related: [.domain/second-brain/domain.md#instruction-review]
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
related: [.domain/second-brain/naming.md#saving-evidence]
```

State how every claimed reduction was obtained — read from this machine's own agent
activity, or measured in a controlled before-and-after — and express it as a
reduction in what agents load rather than as money. A figure with no stated basis
is not shown.
