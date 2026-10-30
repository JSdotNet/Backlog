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

## Subject State

```meta
type: aggregate
status: draft
aliases: [DevbookSubjectState, IDevbookSubjectStateStore, subject state]
related: [.devbook/domain/devbook/features.md#rendered-devbook-documents, .devbook/arc42/adr/0044-devbook-subject-state-lives-in-backlogs-own-store.md, .devbook/domain/devbook/domain.md#remark, .devbook/domain/devbook/domain.invariants.md#subject-state, .devbook/domain/devbook/requirements.md#rendered-devbook-documents, .devbook/domain/repository-management/requirements.md#requirement-subject-state-is-not-set-on-an-archived-devbook]
```

The workflow state of one [Devbook Subject](#devbook-subject) in one repository,
kept by the app rather than by the chapter. It holds the repository's alias, the
subject, the state the subject is in, who set that state and when, and, for a
proposal, the [approval and acceptance](#subject-decision) it stands on.

It exists because devbook's branch-true decision took workflow state out of the
chapter's `meta` block
(`JSdotNet/devbook:.devbook/arc42/adr/chapter-schema.md#branch-true-devbook`). A
branch states what is true of its code. Whether a debt record is planned, or a
proposal approved, is a fact about the work around the chapter. So the host that
runs the workflow keeps it, under the names the `devbook.decision@1` contract
gives a subject. Backlog keeps it in its own store, one file per repository under
the storage folder, and never in a repository or in the devbook database (local
ADR 0044).

It is an aggregate because each subject's state changes as one. Setting a state,
approving and accepting move the state and its decisions together, and a refusal
changes nothing. No other record changes with it. One set of rules,
`DevbookSubjectRules`, decides all four operations for every store. The app
always says who acted and when: the host's own name, the one a remark's author
carries, and the store's clock. A caller never passes either.

It replicates as a whole document through a replica container of its own (local
ADR 0044). Its id is derived from the repository and the subject, so two devices
that set a state on the same subject before either has synced land on one
document. A state is set and never deleted, so it leaves no tombstone. How each
change is stamped, which version wins, which state stays on this device, and
where a state is stored are [the Subject State invariants](domain.invariants.md#subject-state).

A state lives with the app rather than in the repository, so a reader can set a
debt record's state on a branch snapshot nobody can write to. An archived
repository's states are read and never set, as
[Repository Management requires](../repository-management/requirements.md#requirement-subject-state-is-not-set-on-an-archived-devbook).
Where the panels show a state is in
[the requirements of Rendered devbook documents](requirements.md#rendered-devbook-documents).
A subject state holds no text of the chapter, and it is not a [Remark](#remark).

### Devbook Subject

```meta
type: value-object
status: draft
aliases: [DevbookSubject, subject]
```

One thing in a devbook that has workflow state, named the way
`devbook.decision@1` names it: a [kind](#subject-kind) and a key. Equality is by
value.

- A **proposal** is keyed by its change's folder name under `openspec/changes/`.
- A **decision record** is keyed by its repository path and the pull request it
  is decided in. A record decided again in a later pull request is another
  subject, so one review's verdict never stands in for the next.
- A **debt record** is keyed by its repository path.

A decision record is a Markdown file directly in the arc42 folder's `adr/`, and a
debt record one directly in its `tdr/`. The folder's README or index and the
coding guidelines under `adr/guidelines/` are no records. Every other chapter is
no subject: merged, it is in force, and its state is the branch it is on.

A record path is spelled one way: forward slashes, no anchor, and no leading `./`
or `/`. A subject the contract could not name is refused when it is made: a blank
key, a decision record with no pull request, a pull request on another kind, or a
proposal name that is no folder name.

### Subject Kind

```meta
type: enum
status: draft
aliases: [DevbookSubjectKind]
```

Which of the contract's three subjects a subject is, and the states its kind can
be in, in workflow order.

| Kind | States |
| --- | --- |
| `proposal` | `draft`, `approved`, `accepted` |
| `decision` | `proposed`, `decided`, `declined` |
| `debt` | `identified`, `planned`, `in-progress`, `resolved` |

The rows are closed within `devbook.decision@1`. A state outside its kind's row is
refused and never stored.

### Subject Decision

```meta
type: value-object
status: draft
aliases: [DevbookSubjectDecision, approval, acceptance]
```

A person's approval or acceptance of a proposal: who gave it, when, and the
fingerprint of the change it was given at. Only a proposal has them. The
fingerprint is the change's `devbook.chapter.fingerprint@1`, which is `sha256:`
and eight lowercase hex characters, and the caller passes the one it read.

An acceptance stands on an approval at the same fingerprint. A change that moved
after it was approved therefore has to be approved again before anyone can accept
it.

## Instruction Review

```meta
type: domain-service
status: draft
related: [.devbook/domain/devbook/features.md#instruction-optimization, .devbook/domain/repository-management/features.md#repository-devbook-folder-settings]
```

Reviews the agent-facing instruction documents a repository carries, the
working-instructions knowledge area for each tool. It produces findings about what
they cost, where they disagree, and which wording is not doing its job.

None of the review is built yet. Its switch is reserved
([Instruction optimization](context.md#instruction-optimization)), so this chapter
records the intended design and no rule below has a type that enforces it.

It is a service rather than an aggregate because the documents belong to the
repository. They are read through the devbook-folder settings
[Repository Management](../repository-management/features.md#repository-devbook-folder-settings)
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

## Shared Value Objects

```meta
type: shared-value-objects
status: draft
related: [.devbook/domain/devbook/domain.invariants.md#shared-value-objects]
```

> The planning fields any devbook chapter carries in its `meta` block, a
> repository chapter and a [Brain note](../brain/domain.md#knowledge-note) alike:
> an optional [Effort](#effort) and a `roadmap` list of Roadmap Contributions.
> Roadmap Planning reads both when a Roadmap Item gathers and totals the work
> behind it. Devbook registers and owns them, and Roadmap only reads them.

### Roadmap Contribution

```meta
type: value-object
status: draft
aliases: [roadmap contribution, contributes to]
```

A [Roadmap Item](../roadmap/domain.md#roadmap-item) tag this chapter declares it
contributes to, held as a slug in the chapter's `roadmap` list. A chapter may
declare several, or none. Equality is by value.

It **names** a roadmap item rather than **addressing** a chapter, and that
distinction is the one most easily misread. A knowledge cross-reference is a
`<path>#<slug>` address that resolves to a specific node and draws an edge in the
knowledge graph. A Brain note's [Task Link](../brain/domain.md#task-link) names
one task by its id and stays a value on the note's node. A `Roadmap Contribution` is a bare tag slug that resolves to nothing
here. It is the same vocabulary a
[Roadmap Tag](../roadmap/domain.md#roadmap-tag) holds and a Task files under, and
Roadmap Planning reads a chapter's contributions when it gathers work by tag.

Because it names rather than addresses, it behaves like an alias: a chapter
contributing to `sync-service` is not linked to any document called that; see
[its invariant](domain.invariants.md#invariant-a-roadmap-contribution-draws-no-graph-edge).
Nothing validates the slug, and a slug matching no current roadmap item is a
normal, harmless state.

It is also not a [Tag](../brain/domain.md#tag), even though both are loosely "tags". A Tag is the
Brain's own `#keyword` for discovery, and it finds notes there. A Roadmap
Contribution names a slug [Roadmap Planning](../roadmap/domain.md#roadmap-tag)
owns, and only Roadmap Planning reads it. The two are never collapsed.

## Shared Enums

```meta
type: shared-enums
status: draft
```

> Enums used by more than one aggregate in this bounded context.

No enum is shared between Devbook's aggregates. This chapter is reserved for
future cross-aggregate enums.

## Ubiquitous Language

```meta
type: ubiquitous-language
```

### Devbook

```meta
type: term
status: draft
aliases: [Devbook, Knowledge]
related: [.devbook/domain/context-map.md#context-map, .devbook/domain/brain/domain.md#brain]
```

The bounded context, and the module, pane, database and tooling named after it.
*Second Brain* is not an alias of it: that phrase means the [Brain](../brain/domain.md#brain).

`Knowledge` is an alias. The Inbox uses it as informal shorthand when routing
here, and older code named the context `Knowledge*` (`Backlog.Modules.Knowledge`,
`KnowledgePane`, `_meta/knowledge.db`).

What the context reads in a repository keeps the convention's own name,
*devbook folders*: `.devbook/arc42`, `.devbook/domain`, `.devbook/tech`,
`.devbook/design` and `.devbook/ai`.

### Effort

```meta
type: term
status: draft
aliases: [effort, story points, story-point estimate]
related: [.devbook/domain/devbook/domain.md#shared-value-objects, .devbook/domain/tasks/domain.md#effort]
```

The size of a devbook chapter in **story points**, carried in its `meta` block.
It is an optional whole number of zero or more. As with a Task's effort, absent
means "not estimated" and `0` is a real estimate. Unlike a Task's, a negative
value is not refused: a chapter is a file nothing checks on save, so the reader
treats it as unreadable
([An unreadable effort is no estimate](domain.invariants.md#invariant-an-unreadable-effort-is-no-estimate)).
It sizes the knowledge work rather than timing it.
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
spells it, and by the heading's anchor. The sweep owns the report's shape, so the
app keeps its words rather than mapping them onto its own. Which verdict a chapter
shows, and in what words, is in
[the requirements of Sync verdicts beside a chapter](requirements.md#sync-verdicts-beside-a-chapter).
A verdict arrives on the sweep's `Delivery Run` in the
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
writes it, and Devbook only reads it. A review note is not chapter content
([A review note never travels as the chapter's content](requirements.md#requirement-a-review-note-never-travels-as-the-chapters-content)),
and it is never a [Remark](#remark).
