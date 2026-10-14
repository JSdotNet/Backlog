# Devbook

```meta
type: domain
status: draft
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context; each chapter's
> `type` records which of those it is. An Aggregate's owned Entities, Value
> Objects, and Enums are chapters directly beneath it, typed `entity`,
> `value-object`, and `enum`. Value Objects/Enums shared across multiple
> aggregates get their own chapter at the end instead of being duplicated.

## Knowledge Note

```meta
type: aggregate
status: draft
related: [.devbook/domain/tasks/domain.md#task, .devbook/arc42/08-crosscutting-concepts.md#shared-data-types, .devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md, .devbook/domain/devbook/domain.md#brain, .devbook/domain/devbook/domain.invariants.md#knowledge-note]
aliases: [KnowledgeNote, Brain note]
```

An organized unit of project knowledge stored as Markdown, and the boundary for
its own organization and links. Its area, its `PARA Category`, is its only
lifecycle state: the lifecycle is a set of workflow phases read off the area
([Knowledge note lifecycle](flow.md#knowledge-note-lifecycle)). The rules a note
is held to are in [its invariants](domain.invariants.md#knowledge-note).

A Knowledge Note lives in the [Brain](#brain) as one Markdown file, a devbook
chapter like any other. It keeps the devbook `meta` block and `annotation` fences
(local ADR 0016), and adds three fields to the block (local ADR 0026):

| Field | Holds | Value object |
|---|---|---|
| `para` | The note's area. Derived from the top folder the note sits in, and written only to override it. | [PARA Category](#para-category) |
| `repos` | The repositories a project note concerns. Project notes only. | [Project Ref](#project-ref) |
| `tasks` | The tasks the note links to. | [Task Link](#task-link) |

The file is the note. Nothing derived about it is written into the Brain folder,
and the folder's own sync carries it between machines. A Knowledge Note never
rides the task feed, which tells it apart from an Inbox
[Note](../inbox/domain.md#note) (local ADR 0025). An Inbox Note waits for triage,
and triaging it **To Brain** makes a Knowledge Note of it.

Any knowledge chapter, a Brain note or a repository chapter, may carry two
planning fields in its `meta` block. The first is an optional [Effort](#effort).
The second is a `roadmap` list of
[Roadmap Contributions](#roadmap-contribution). Roadmap Planning reads both when
a Roadmap Item gathers and totals the work behind it. Devbook registers and owns
them, and Roadmap only reads them.

The Knowledge Note aggregate has no independently identified child entities;
`Project Ref`, `Tag`, `Roadmap Contribution`, and `Task Link` are value objects
owned by the root.

### Project Ref

```meta
type: value-object
status: draft
aliases: [ProjectRef, repo_id]
```

A scope link to a project/repo: `repo_id` and `project_name`. Equality is by
value. A note may be scoped to any number of projects or repositories, none
included.

### Task Link

```meta
type: value-object
status: draft
aliases: [TaskLink, task_id]
```

A typed link to a Task: `task_id`, `link_type`
(reference or embed), and `linked_at`. Immutable; equality is by value.

A note writes it as one value of its `tasks` field: the task's stored id, a GUID,
compared case-insensitively and with or without braces. Only `task_id` is written
today; a link is a reference. A value that is no GUID links no task, and the note
lists it as written.

### Tag

```meta
type: value-object
status: draft
```

A `#keyword` for cross-cutting discovery (e.g. `#architecture`, `#performance`).
Equality is by canonical `name`.

It is **this context's own** vocabulary: a person invents a `#keyword` to find
notes across projects later, and it means whatever they use it to mean. It is not
a [Roadmap Contribution](#roadmap-contribution).

### Roadmap Contribution

```meta
type: value-object
status: draft
aliases: [roadmap contribution, contributes to]
```

A [Roadmap Item](../roadmap/domain.md#roadmap-item) tag this chapter declares it
contributes to: a lowercase kebab-case slug, held in the chapter's `roadmap` list.
A note may declare several, or none. Equality is by value.

It **names** a roadmap item rather than **addressing** a chapter, and that
distinction is the one most easily misread. A knowledge cross-reference is a
`<path>#<slug>` address that resolves to a specific node and draws an edge in the
knowledge graph, and a `Task Link` resolves to one task by its id. A `Roadmap Contribution` is a bare tag
slug that resolves to nothing here. It is the same vocabulary a
[Roadmap Tag](../roadmap/domain.md#roadmap-tag) holds and a Task files under, and
Roadmap Planning reads a chapter's contributions when it gathers work by tag.

Because it names rather than addresses, it behaves like an alias: a chapter
contributing to `sync-service` is not linked to any document called that; see
[its invariant](domain.invariants.md#invariant-a-roadmap-contribution-draws-no-graph-edge).
Nothing validates the slug, and a slug matching no current roadmap item is a
normal, harmless state.

It is also not a [Tag](#tag), even though both are loosely "tags". A Tag is this
context's own `#keyword` for discovery, and it finds notes here. A Roadmap
Contribution names a slug [Roadmap Planning](../roadmap/domain.md#roadmap-tag)
owns, and only Roadmap Planning reads it. The two are never collapsed.

### PARA Category

```meta
type: value-object
status: draft
aliases: [PARACategory, Brain area, para]
related: [.devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md, .devbook/domain/devbook/features.md#brain-folder-setup]
```

The area a note is organized under: one top folder of the [Brain](#brain), shown
as one tab. The list is open. The Brain's `CONVENTIONS.md` declares the areas,
each with its name, its folder and its place in the tab order. A note's area is
the top folder it sits in, unless its `para` field overrides it. Equality is by
folder name.

`CONVENTIONS.md` declares the areas under a heading named `Areas`, at any level.
It uses either a table with a `Folder` column, plus optional `Area` (or `Name`), `Holds`
and `Order` columns, or a list whose items name the folder in backticks. A
folder that is not one plain segment, or a folder declared twice, is skipped. The
order is the order written, unless the `Order` column numbers the rows.

When `CONVENTIONS.md` declares no areas, the PARA set applies, in this order:

- `_inbox` — notes not yet filed; where a capture lands.
- `projects` — active work with a goal and a deadline.
- `areas` — ongoing responsibilities without a deadline.
- `resources` — reference material and collected knowledge.
- `people` — notes about the people the work involves.
- `journal` — dated entries.
- `archive` — inactive notes preserved for future search.

The name stays *PARA Category* because PARA is the default and the vocabulary a
person starts from; a Brain that declares its own areas is still organized the
same way, one area per note.

### Note Source

```meta
type: enum
status: draft
```

Origin of the note: `inbox` (routed **To Brain** from triage), `manual`
(written in the Brain, through the pane's **Capture** or an assistant's capture
over MCP), or `import`.

## Remark

```meta
type: aggregate
status: draft
aliases: [DevbookAnnotation, remark, reading note]
related: [.devbook/domain/devbook/features.md#remarks-on-a-chapter, .devbook/arc42/adr/0011-devbook-annotations-are-a-third-replica-container.md, .devbook/domain/devbook/domain.md#review-note, .devbook/domain/devbook/domain.invariants.md#remark, .devbook/domain/devbook/requirements.md#remarks-on-a-chapter]
```

A person's own note on one block of a chapter, kept by the app rather than by the
repository. It holds the repository's alias and the chapter's path inside the
repository, the block it is anchored to, its body, its author, and whether it is
resolved. The alias is the name a repository already has on every device, so one
remark names the same chapter on all of them.

A remark is anchored by two things: the block's index and a digest of what the
block said. The index says where to look. The digest says whether the block found
there is still the one the remark was about. Where the read view shows a remark
once the chapter has changed is in
[the requirements of Remarks on a chapter](requirements.md#remarks-on-a-chapter).

It is an aggregate because its lifecycle is the person's alone: add, edit,
resolve, delete. No other record changes with it. It replicates as a whole
document through a replica container of its own (local ADR 0011). A live remark
with an empty body is a *draft*, open in the read view and not yet written. A
remark the sync service would refuse also stays on this device: one filed under
no repository, as a remark on the storage-folder devbook is, or one with a field
past its bound. A warning names it, and it is offered again only after an edit.
A deletion replicates without the remark's body. How each change is stamped,
which version wins, what a deletion leaves, what happens to a draft, which remark
stays local, and where a remark is stored are
[the Remark invariants](domain.invariants.md#remark).

A remark lives with the app rather than in the repository, so a reader can remark
on a branch snapshot nobody can write to. It is not a
[Review Note](#review-note), and neither is converted into the other.

## Cross-Linking

```meta
type: domain-service
status: draft
related: [.devbook/domain/tasks/domain.md#task]
```

Maintains bi-directional links between Knowledge Notes and Tasks and
supports queries that span both domains (search backlog + knowledge together,
embed knowledge snippets into backlog details). It is a service because a link
has two owners in different contexts and must be kept consistent from both sides
rather than living inside a single aggregate. Invocation semantics: consistency policy invoked when note/backlog links are created, removed, or reconciled.

## Instruction Review

```meta
type: domain-service
status: draft
related: [.devbook/domain/devbook/features.md#instruction-optimization, .devbook/domain/repository-management/features.md#repository-knowledge-folder-settings]
```

Reviews the agent-facing instruction documents a repository carries, the
working-instructions knowledge area for each tool. It produces findings about what
they cost, where they disagree, and which wording is not doing its job.

It is a service rather than an aggregate because the documents belong to the
repository. They are read through the knowledge-folder settings
[Repository Management](../repository-management/features.md#repository-knowledge-folder-settings)
owns, and several tools' instruction sets are examined as one subject. No aggregate
here may hold their content without becoming a second, staler copy of it.

Each [Instruction Finding](#instruction-finding) is of one of these kinds:

| Finding kind | Sub-feature that surfaces it |
| --- | --- |
| Context load carried by an always-loaded document | [Context-load budget](features.md#context-load-budget) |
| One meaning stated in more than one place | [Duplicate rule detection](features.md#duplicate-rule-detection) |
| A disagreement or an omission between the sets two tools read | [Cross-tool alignment validation](features.md#cross-tool-alignment-validation) |
| Wording that will not reliably make an agent reach the document it names | [Pointer and phrasing review](features.md#pointer-and-phrasing-review) |
| Wording that steers by prohibition where the wanted behaviour would steer better | [Pointer and phrasing review](features.md#pointer-and-phrasing-review) |
| Guidance the agent already follows unprompted | [Prune candidates](features.md#prune-candidates) |
| Guidance that has gone stale | [Prune candidates](features.md#prune-candidates) |
| Guidance that copies what the repository's own configuration already states | [Prune candidates](features.md#prune-candidates) |

Accepting a finding edits the repository's own file, and the repository stays its
owner. The previous wording remains recoverable exactly. Code blocks, commands,
paths, and error strings pass through a rewrite unchanged. A change that shrinks an
instruction set at the cost of how well the agents work is a failed change rather
than a saving. That is why an applied proposal is measured afterwards and can be
withdrawn. A claimed reduction carries its [Saving Evidence](#saving-evidence).

Findings live for the review that produced them.

Invocation semantics: command-invoked read-only analysis over a repository's
instruction documents; it writes only through a proposal a person has accepted, one
at a time.

## Shared Enums

```meta
type: shared-enums
status: draft
```

> Enums used by more than one aggregate in this bounded context.

No enum is shared between Devbook's aggregates. `PARA Category` and `Note Source`
belong to the Knowledge Note and are documented under it, `PARA Category` as a
value object because the Brain's `CONVENTIONS.md` declares its values. This
chapter is reserved for future cross-aggregate enums.

## Ubiquitous Language

```meta
type: ubiquitous-language
```

### Devbook

```meta
type: term
status: draft
aliases: [Devbook, Knowledge]
related: [.devbook/domain/devbook/domain.md#knowledge-note, .devbook/domain/context-map.md#context-map, .devbook/domain/devbook/domain.md#brain]
```

The bounded context, and the module, pane, database and tooling named after it.
*Second Brain* is not an alias of it: that phrase means the [Brain](#brain).

`Knowledge` is an alias. The Inbox uses it as informal shorthand when routing
here, and older code named the context `Knowledge*` (`Backlog.Modules.Knowledge`,
`KnowledgePane`, `_meta/knowledge.db`).

What the context reads in a repository keeps the convention's own name,
*devbook folders*: `.devbook/arc42`, `.devbook/domain`, `.devbook/tech`,
`.devbook/design` and `.devbook/ai`.

### Brain

```meta
type: term
status: draft
aliases: [Brain, Second Brain, SecondBrain, brain scope, Brain scope]
related: [.devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md, .devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md, .devbook/arc42/12-glossary.md#terms, .devbook/domain/devbook/features.md#brain-scope]
```

The person's own notes: one devbook-shaped folder per user, outside every
repository, whose [Knowledge Notes](#knowledge-note) are organized under the
areas its `CONVENTIONS.md` declares. The app builds its database into its own
storage, never into the folder. *Second Brain* is an alias: until 2026-09-15 it
named the whole context, and now it means only the person's own notes.

**Brain** is also the scope of everything filed against no repository. It is the
first chip of the header's repository scope. Pressed, it narrows every view that
reads the scope to the tasks with no repository, and it opens the Brain in the
Devbook pane. Both senses are one person's own things: the work that belongs to no
repository and the notes kept outside every repository. That is why they share a
name. Where the scope is stored, Brain is the empty string, a value no repository
alias can take.

### Repo link

```meta
type: term
status: draft
aliases: [repo link, RepoLink, From your Brain]
related: [.devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md, .devbook/domain/devbook/features.md#repo-links-and-backlinks]
```

A link from a Brain note into a repository devbook, written
`repo:<alias>/<devbook path>[#slug]` in the note's `related` or in its body. It
runs one way: a repository devbook never links into a Brain. Its reverse is read,
never written: a repository chapter lists **From your Brain** the Brain notes that
link to it, for the Brain's owner only, from the Brain's own database.

### Effort

```meta
type: term
status: draft
aliases: [effort, story points, story-point estimate]
related: [.devbook/domain/devbook/domain.md#knowledge-note, .devbook/domain/tasks/domain.md#effort]
```

The size of a knowledge chapter in **story points**, carried in its `meta` block.
It is an optional non-negative integer with the same three-valued edges as a
Task's effort: absent means "not estimated", `0` is a real estimate, and a
negative value is rejected. It sizes the knowledge work rather than timing it.
Devbook registers and owns it. Roadmap Planning reads and totals it across the
chapters an item gathers, but never sets it.

### Instruction Set

```meta
type: term
status: draft
aliases: [instructions, agent instructions, working instructions]
related: [.devbook/domain/devbook/domain.md#instruction-review, .devbook/domain/devbook/features.md#instruction-set-inventory]
```

The documents one tool reads as its instructions for a repository: the file it
loads on every run, the ones it loads when a condition matches, and the skills it
can reach. One repository carries several instruction sets, one per tool. They are
the content of the working-instructions knowledge area and belong to the
repository, not to this product.

### Context Load

```meta
type: term
status: draft
aliases: [context load, always-loaded weight]
related: [.devbook/domain/devbook/features.md#context-load-budget]
```

What an instruction spends on every agent turn because it is loaded whether or not
it applies. It is a property of *when* a document is loaded rather than of how long
it is: a line reached only on the branch that needs it carries almost none.

Distinct from the person's own cost of knowing which document to reach for. That
one buys human judgement and is spent on purpose, so the two are never summed and
never traded against each other silently.

### Instruction Finding

```meta
type: term
status: draft
aliases: [finding, proposal]
related: [.devbook/domain/devbook/domain.md#instruction-review]
```

One reviewed observation about an instruction set: where it is, what kind of
problem it is, and the change proposed. Always a proposal — a finding never means
a file was changed, and an accepted one is applied singly with the previous wording
recoverable.

### Instruction Alignment

```meta
type: term
status: draft
aliases: [alignment, cross-tool alignment]
related: [.devbook/domain/devbook/features.md#cross-tool-alignment-validation]
```

Two tools' instruction sets stating the same rule for the same repository, each in
its own agent, skill, and command names.

**Distinct from duplication**, and the distinction is load-bearing. Duplication is
one meaning stated twice where one place would do, and the fix is to keep one
place. Alignment is one meaning that deliberately has to be stated in each tool's
set. Its fix is never to delete one of them. It is to make them agree, or to add
the rule to the set that is missing it. Documents held in alignment are maintained
as a pair, and a change to one leaves the other unfinished.

### Saving Evidence

```meta
type: term
status: draft
aliases: [evidence basis]
related: [.devbook/domain/devbook/features.md#saving-evidence]
```

How a claimed reduction was obtained: read from local agent activity, or measured
in a controlled before-and-after. It travels with the number so a reader knows what
the number is worth. It is stated in what agents load rather than in money — no
billed amount is claimed, and a figure with no basis is not shown.

### Sync Verdict

```meta
type: term
status: draft
aliases: [DevbookSyncVerdict, sweep verdict, drift verdict]
related: [.devbook/domain/devbook/features.md#sync-verdicts-beside-a-chapter, .devbook/domain/sessions/domain.md#delivery-run-recording]
```

A Sync Verdict is the last thing a sweep said about one chapter. It holds the
chapter's drift verdict (`aligned`, `code-ahead`, `spec-ahead`, `conflict` or
`unresolved`) and the sweep's line of evidence. It also names the chapter's
[Sync Unit](#sync-unit), that unit's verdict and the action the sweep took, and the
pull request or [drift issue](../../ai/05-unattended-runs.md#scheduled-devbook-upkeep)
it produced.

A verdict is filed by repository, by the chapter's file as `DevbookChapterKey`
spells it, and by the heading's anchor. The latest replaces the one before it for
that chapter alone. The words are the sweep's, carried verbatim, because the sweep
owns the report's shape. A verdict arrives on the sweep's `Delivery Run` in the
Sessions context, which keeps the run's verdicts as sent. This context keeps them
per chapter, because a reader of a chapter asks whether the code still agrees
with it.

### Sync Unit

```meta
type: term
status: draft
aliases: [sync unit, SyncUnit]
related: [.devbook/domain/devbook/domain.md#sync-verdict, .devbook/ai/05-unattended-runs.md#scheduled-devbook-sync-sweeps]
```

What one sync direction governs, and what a sweep checks and acts on as a whole.
The devbook convention defines it: an aggregate with everything it owns, a domain
service, a feature with its sub-features and requirements, one switch or setting,
one actor, one building-block file, or one component chapter. Each unit's
direction is `push`, `pull`, `sync`, `report` or `off`. It is set on the unit's
root chapter or inherited from its page, its context or its folder, and `report`
applies when none of them states one.

### Review Note

```meta
type: term
status: draft
aliases: [review note, annotation, annotation fence]
related: [.devbook/domain/devbook/features.md#review-notes-in-a-chapter, .devbook/domain/devbook/domain.md#remark]
```

A note the repository keeps in a chapter file itself, as an `annotation` fence
beside the passage it is about. It has a kind (comment, question, suggestion or
flag), a status (open or resolved), an author, a date, an optional quote, and its
replies. It is the repository's shared review state: the convention's own tooling
writes it, and Devbook only reads it. A review note is never chapter content, and
it is never a [Remark](#remark).
