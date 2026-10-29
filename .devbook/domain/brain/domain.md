# Domain

```meta
type: domain
status: draft
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context.

## Knowledge Note

```meta
type: aggregate
status: draft
related: [.devbook/domain/tasks/domain.md#task, .devbook/arc42/08-crosscutting-concepts.md#shared-data-types, .devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md, .devbook/domain/brain/domain.md#brain, .devbook/domain/brain/domain.invariants.md#knowledge-note]
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

The file is the note. What the app derives from it is kept out of the folder
([Nothing derived lands in the folder](requirements.md#requirement-nothing-derived-lands-in-the-folder)).
The folder's own sync carries the note between machines rather than the task
feed, which tells it apart from an Inbox [Note](../inbox/domain.md#note) (local
ADR 0025). An Inbox Note waits for triage, and triaging it **To Brain** makes a
Knowledge Note of it.

As any devbook chapter, a note may also carry the two
[planning fields](../devbook/domain.md#shared-value-objects), an Effort and a
`roadmap` list. Devbook registers and owns them; the Brain only writes them.

The Knowledge Note aggregate has no independently identified child entities;
`Project Ref`, `Tag`, and `Task Link` are value objects owned by the root.

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

A link from a note to one Task, by the task's stored id. A note writes it as one
value of its `tasks` field, and the id is a GUID. How a value is read is
[A task link is the task's id](requirements.md#requirement-a-task-link-is-the-tasks-id).
A value that is no GUID links no task, and the note lists it as written.

A Task Link is not an address and not an edge. The Brain's database keeps a
note's `tasks` values on the note's own node as they are written, and the app
reads them from there both ways: the tasks a note names, and the notes that name
a task. A task's record carries no note.

### Tag

```meta
type: value-object
status: draft
```

A `#keyword` for cross-cutting discovery (e.g. `#architecture`, `#performance`).
Equality is by canonical `name`.

It is **this context's own** vocabulary: a person invents a `#keyword` to find
notes across projects later, and it means whatever they use it to mean. It is not
a [Roadmap Contribution](../devbook/domain.md#roadmap-contribution).

### PARA Category

```meta
type: value-object
status: draft
aliases: [PARACategory, Brain area, para]
related: [.devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md, .devbook/domain/brain/features.md#brain-folder-setup]
```

The area a note is organized under: one top folder of the [Brain](#brain), shown
as one tab. The list is open, and it has two parts:

- the **declared areas**: the ones the Brain's `CONVENTIONS.md` declares, each
  with its name, its folder and its place in the tab order, or the PARA set when
  it declares none;
- the **discovered areas**: every other top folder that holds at least one note,
  anywhere below it. A discovered area is named after its folder and has no
  `Holds`.

A folder `CONVENTIONS.md` lists under `## Ignore` is never an area, and neither
is a dot-folder. A note's area is the top folder it sits in, unless its `para`
field overrides it. Equality is by folder name. Local ADR 0026 records why the
folders on disk decide the areas and `CONVENTIONS.md` only adds to them.

`CONVENTIONS.md` declares the areas under a heading named `Areas`, at any level.
It uses either a table with a `Folder` column, plus optional `Area` (or `Name`), `Holds`
and `Order` columns, or a list whose items name the folder in backticks. Which
declarations count is
[An area is one plain folder, declared once](domain.invariants.md#invariant-an-area-is-one-plain-folder-declared-once),
and their order is
[Declared areas keep the order written](domain.invariants.md#invariant-declared-areas-keep-the-order-written).
A declared area keeps its tab while its folder is empty or missing, so a capture
always has a target.

The discovered areas follow the declared ones, by folder name
([Discovered areas follow the declared ones](domain.invariants.md#invariant-discovered-areas-follow-the-declared-ones)).
A top folder with no note in it is not an area until it gets one.

`CONVENTIONS.md` lists the folders that are never areas under a heading named
`Ignore`, as list items that name the folder in backticks or bare, or as a table
with a `Folder` column, the shapes `Areas` takes. An ignored
folder stays out even when `Areas` declares it
([An ignored folder is never an area](domain.invariants.md#invariant-an-ignored-folder-is-never-an-area)).
Markdown files at the Brain's root, such as `README.md` and `CONVENTIONS.md`,
belong to no area.

When `CONVENTIONS.md` declares no areas, the PARA set applies
([The areas are the declared ones, then the discovered ones](requirements.md#requirement-the-areas-are-the-declared-ones-then-the-discovered-ones)).
In its order, it is:

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

## Ubiquitous Language

```meta
type: ubiquitous-language
```

### Brain

```meta
type: term
status: draft
aliases: [Brain, Second Brain, SecondBrain, brain scope, Brain scope]
related: [.devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md, .devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md, .devbook/arc42/12-glossary.md#terms, .devbook/domain/brain/features.md#brain-scope]
```

The person's own notes: one devbook-shaped folder per user, outside every
repository, whose [Knowledge Notes](#knowledge-note) are organized under its
[areas](#para-category): the ones its `CONVENTIONS.md` declares, then every other
top folder that holds a note. The app builds its database into its own
storage, never into the folder. It names its own bounded context (local ADR
0040). *Second Brain* is an alias: until 2026-09-15 it named the whole Devbook
context, and now it means only the person's own notes.

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
related: [.devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md, .devbook/domain/brain/features.md#repo-links-and-backlinks]
```

A link from a Brain note into a repository devbook, written
`repo:<alias>/<devbook path>[#slug]` in the note's `related` or in its body. It
runs one way: a repository devbook never links into a Brain. Its reverse is the
**From your Brain** list, the Brain notes a repository chapter shows as linking to
it. Who sees that list, and what reading it writes, is
[Backlinks reach only the owner and write nothing](requirements.md#requirement-backlinks-reach-only-the-owner-and-write-nothing).

