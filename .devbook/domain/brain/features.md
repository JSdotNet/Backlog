# Features

```meta
type: features
status: draft
```

> What the Brain lets a person do, in business language.

## Knowledge capture

```meta
type: feature
related: [.devbook/domain/inbox/features.md#routing, .devbook/domain/brain/features.md#capture-into-the-brain]
```

Store notes, references, ideas, and learnings as markdown from inbox triage,
manual creation, or import, attaching them to one or more projects, topics, or
tags. Every note is a file in the person's [Brain](domain.md#brain); where it
comes from is [Capture into the Brain](#capture-into-the-brain).

## PARA organization

```meta
type: feature
depends-on: [.devbook/domain/brain/features.md#knowledge-capture]
related: [.devbook/domain/brain/domain.md#para-category, .devbook/domain/brain/features.md#brain-folder-setup]
```

Organize notes into areas, one top folder each, shown as one tab each. The
areas the Brain's `CONVENTIONS.md` declares come first. A Brain that declares
none uses the PARA set: an inbox for what is not filed yet, Projects (active,
deadline), Areas (ongoing), Resources (reference), People, a Journal, and
Archive (inactive). Every other top folder that holds a note follows as an area
named after its folder, so a note filed into a new folder shows at once. A
folder `CONVENTIONS.md` lists under `## Ignore` never becomes a tab.
A dated area lists newest first: the `journal` folder, or any area whose
`Holds` line in `CONVENTIONS.md` says *dated*. Its entries are named by date, so
the name descending is the date descending, at every level of the folder; every
other area lists by name.
Moving a note to another area is moving its file to another folder; a note may
name its area in its `para` field instead, which wins over the folder.

## Cross-project linking

```meta
type: feature
related: [.devbook/domain/brain/features.md#repo-links-and-backlinks]
```

Reference multiple projects and repos from a single note, and discover notes
across projects by tag. A project note names the repositories it concerns in its
`repos` field; a note that points at one chapter of a repository's devbook does
so with a [repo link](#repo-links-and-backlinks).

## Topic and tag grouping

```meta
type: feature
related: [.devbook/domain/brain/domain.md#tag, .devbook/domain/devbook/domain.md#roadmap-contribution]
```

Group notes by topic (not just project), support cross-cutting tags, and search
across all knowledge content via the tag index.

These discovery tags are the context's own `#keyword`s. They are not a chapter's
[Roadmap Contribution](../devbook/domain.md#roadmap-contribution). A chapter may also declare
an [Effort](../devbook/domain.md#effort), which Devbook registers and Roadmap Planning only
reads.

## Bi-directional linking

```meta
type: feature
related: [.devbook/domain/brain/requirements.md#bi-directional-linking, .devbook/domain/brain/domain.md#task-link, .devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md, .devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md, .devbook/domain/tasks/features.md#search-filter-and-organize]
```

Link a Brain note to the tasks it is about, and see the link from both ends. A
note names its tasks in its `tasks` field, one [Task Link](domain.md#task-link)
per task, by the task's stored id. The Brain's database carries the field, and
the app reads it both ways from there:

- **From the note.** Under its links into devbooks, the note shows a **Tasks**
  section: each task this backlog holds is a gold chip reading its title, and
  pressing it opens the task in the task list. An id this backlog does not hold is
  a dashed chip reading *not in this backlog*, never an error — one Brain is read
  beside more than one backlog.
- **From the task.** A task row, in the Brain scope and in a repository scope,
  carries a teal **◆ n note** badge counting the Brain notes that name it. With one
  note, pressing it gives the Devbook the screen on the Brain side, at that note;
  with several, it lists them to pick from. Where the Devbook is switched off or
  has no Brain to read, the press shows a *not available* toast and opens
  nothing.

The reverse is read, never written: a task's record carries no note, and the badge
is drawn only for the Brain's owner, from their own Brain (local ADR 0027 §6), so
another reader of the same backlog sees none. Embedding a note's content in a task's
details is not built.

## Brain scope

```meta
type: feature
related: [.devbook/domain/brain/requirements.md#brain-scope, .devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md, .devbook/domain/tasks/features.md#search-filter-and-organize, .devbook/design/interaction-guidelines.md#shell-header]
```

Pick the Brain the way a repository is picked. The header's repository scope
leads with a **Brain** chip, set off from the repository chips by a divider and
worn in the Brain's teal. It cannot be removed, and it needs no repository, so
the scope works for a person who has configured none.

Brain is the scope of everything filed against no repository. Pressed alone, it
narrows Tasks, Board, Calendar, In progress, the Dashboard and Sessions to the
tasks with no repository. It also shows the Brain when the Devbook is opened. Pull
requests and the Roadmap read Brain alone as nothing pressed: every pull request
belongs to a repository, and the Roadmap reads one anchor. Ctrl adds it to the repositories already pressed, and nothing pressed still
means everything.

The scope is remembered with Brain in its place, so a fresh shell reopens on it.
The Tasks filter bar has no *No repo* chip: Brain is the one name for those rows.
With the Brain and repositories pressed together, each row's repository badge
says where the task is filed: **Brain** for no repository, else the alias.
Until a Brain folder is set up, the chip reads **Set up your Brain**, still
scopes the tasks, and its tooltip points to Settings › Storage. The Devbook on the
Brain side then shows a **Set up your Brain** button instead of areas. It opens
Settings › Storage at the Brain folder card, inside the app rather than in the
system browser.

## Brain folder setup

```meta
type: feature
setting: .devbook/domain/brain/context.md#brain-folder
related: [.devbook/domain/brain/requirements.md#brain-folder-setup, .devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md, .devbook/domain/brain/domain.md#para-category]
```

Choose where the Brain lives, once, in Settings › Storage. The person picks a
folder outside every repository. There is no default, and OneDrive is the
expected place. What the field accepts is in
[Brain folder](context.md#brain-folder). The Brain card says three things:

- whether the folder is synced, as information rather than a warning;
- that its search database is built in the app's storage and never synced;
- whether the folder's `README.md` and `CONVENTIONS.md` are present.

**Create conventions** writes both files and every area folder they declare from
a template the app ships, the PARA set by default, and never overwrites a file
that is already there. From then on the folder carries its own convention, so a
second machine that reads the synced folder sees the same areas.

The shipped `CONVENTIONS.md` declares its areas as a table under an `## Areas`
heading. It has one row per area in tab order, with the area's name, its folder
and what it holds. With no rows, the PARA set applies. Its `## Ignore` section
lists `_templates`, the folder note templates usually live in, so templates never
become a tab. Create conventions makes the
area folders the folder's own `CONVENTIONS.md` declares, so a person who wrote
theirs first gets their areas, not the template's.

**Browse** opens the Windows folder picker in the installed app. A host without
one, the browser harness among them, takes a typed path.

## Brain mode of the Devbook pane

```meta
type: feature
feature-flag: .devbook/domain/devbook/context.md#devbook-pane
related: [.devbook/domain/brain/requirements.md#brain-mode-of-the-devbook-pane, .devbook/domain/devbook/features.md#area-selection-and-scope, .devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md, .devbook/design/interaction-guidelines.md#shell-header]
```

Read and write the Brain in the same pane that reads a repository's devbook.
Which scope shows the Brain is in
[Area selection and scope](../devbook/features.md#area-selection-and-scope). On the Brain, the pane
carries a teal **Brain** badge after its title. The folder's path sits where a
repository's branch picker would be, and there is one tab per area of the Brain:
the declared areas, then the discovered ones.

**Show hidden** in the menu brings back what the Brain hides: every folder
`CONVENTIONS.md` lists under `## Ignore` and every dot-folder gets a tab after the
rest, marked hidden, and an area's menu lists its dot-entries too. It is off by
default, and the app remembers it per device in its own settings, never in the
folder. It changes only what the pane shows: search, the atlas and the Brain
database still leave hidden folders out.

The tree, the rendered chapter and the editor are the ones a repository devbook
uses, and a Brain note is always editable. The reader leaves a remark on a
note's block as on a chapter's, and edits, resolves or deletes it there. A Brain
remark is stored under the Brain's scope, by the note's path from the Brain's
root, so the agent lists and resolves it on `#brain`. What only a repository
has is absent: branch updates, sync verdicts and scenario marks. So is opening
the folder in an editor. Search is offered on the Brain as on a repository, because it is one
[Unified search](../devbook/features.md#unified-search) wherever it is opened. The **Atlas** button
opens the Brain's own map instead of a repository's
([Atlas edges](#atlas-edges)). Ask AI takes no Brain note as the open chapter.

The `Brain | <repo>` switch flips the pane between the Brain and a repository
without touching the task filter. It is not remembered: any change to the scope
puts the pane back on the anchor.

The header's toggle for the pane names what it shows: **Brain**, in teal, while
the pane shows the Brain, and **Devbook** otherwise. It is right before the pane
opens, so a closed toggle already says which one a press will open, and it flips
with the switch.

On the Brain, and while the switch shows, the pane names itself *Devbook* at the
start of its tab strip. The switch sits right after the title, so it stays in
place as the badge and the path come and go.

The pane's own width decides its layout, not the window's. Narrower than a menu
beside a readable column, as in a narrow window, the menu sits above the
note, capped so the note still starts on screen, and a note's title wraps above
its path rather than ending in an ellipsis.

With no folder set up yet, the pane on the Brain asks for one. A folder that is
set but missing, or is not a path, says so in words and shows no tabs. A
declared area whose folder does not exist yet still has its tab, and the tab
says the Brain has no such folder yet.

When the folder holds a OneDrive conflict copy, the pane says so and changes
neither file: two edits to one note are the person's to merge. A conflict copy
is `<name>-<MACHINE>.md` in the same folder as `<name>.md`. MACHINE is the
computer name OneDrive writes: 1 to 15 capitals, digits and hyphens, with at
least one capital. So `plan-2.md`, `plan-draft.md` and `my-plan.md` stay
ordinary notes. The warning names every pair.

## Repo links and backlinks

```meta
type: feature
depends-on: [.devbook/domain/brain/features.md#brain-mode-of-the-devbook-pane]
related: [.devbook/domain/brain/requirements.md#repo-links-and-backlinks, .devbook/domain/brain/domain.md#repo-link, .devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md]
```

Point a Brain note at the repository chapter it is about. A
[repo link](domain.md#repo-link), `repo:<alias>/<devbook path>[#slug]` in the
note's `related` or its body, renders as a chip naming the repository's alias
and the chapter:

| Link state | Chip | It adds |
| --- | --- | --- |
| The alias names a repository configured here, as a clone or a branch snapshot, and its devbook shows the section the path lies in | Gold; opens that repository's devbook at the chapter | Nothing |
| The alias is not configured on this machine | Dashed | *not on this machine* |
| The alias is configured, but the path lies in no section its devbook shows | Dashed | *not a chapter in its devbook* |

The chip's tooltip is the raw link in every state. An unresolved link is never an
error, because the same Brain is read on machines with different repositories.
Following a gold chip leaves the header scope alone. The pane reads the
repository as it does for a task's reference, and the switch or the scope brings
the Brain back.

The reverse is shown, never written. Under a repository chapter, a teal **From
your Brain** box lists the Brain notes that link to it, marked *Only you see
this*. It reads the owner's own Brain database, and nothing is written into the
repository, so personal notes never reach shared history. A link to any heading
of the chapter's file counts. A note is listed once. If all its links sit inside
chapters, it is listed under the first. If any link is in `related` or outside
every chapter, it is listed on its own. A chapter no note links to draws no box.
Each note in the box is a link: following it puts the note on the Brain side of
the pane, the way a Brain search result does, and leaves the header's scope and
the task filter alone.
A repository devbook has no way to link into a Brain.

## Atlas edges

```meta
type: feature
depends-on: [.devbook/domain/brain/features.md#repo-links-and-backlinks]
related: [.devbook/domain/brain/requirements.md#atlas-edges, .devbook/design/component-libraries.md]
```

See where the Brain reaches into the repositories. On the Brain, the pane's
**Atlas** button maps the whole Brain: its notes, grouped by area, and the links
between them, read from the Brain's own database. Each repo link, in a note's
`related` or its prose, is an edge to a node for the repository chapter it
names. The edge is labelled with the repository's alias, and the chapters of one
repository gather in one group named for that alias. One chapter is one node,
whichever devbook layout or alias case a note wrote it in. A link this machine
can follow is a solid edge to a filled node. One it cannot is a dashed edge to a
hollow node, like its chip. The atlas reads no repository: what a repository
node shows is the link's own words. An edge's label sits at the middle of its
curve unless that overlaps a node, a node's label, or another edge's label; it
then steps along the curve or just above or below it, and an edge with no free
seat goes unlabelled, because the node labels are what the reader navigates by.

Selecting a repository node shows the link's chip. A gold chip opens that
repository's devbook at the chapter, as a chip in a note does. A dashed one says
why it cannot be followed. Selecting a note offers to open it in the pane. Both
close the map. With no Brain index built yet, the atlas says so rather than
drawing an empty map.

## Capture into the Brain

```meta
type: feature
depends-on: [.devbook/domain/brain/features.md#brain-mode-of-the-devbook-pane]
related: [.devbook/domain/brain/requirements.md#capture-into-the-brain, .devbook/domain/inbox/features.md#route-to-brain, .devbook/domain/inbox/domain.md#itemtriaged]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainCaptureTests, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainNoteWriterTests, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainNoteTextTests, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.BrainScopeToolsTests.A_capture_lands_in_the_inbox_area_and_reads_back_as_written]
```

Get a thought into the Brain without leaving what is on screen. In Brain mode
the pane header carries **Capture**: a title and a Markdown body, written as a
new note with its `meta` block in the Brain's inbox area. That area is `_inbox`
under the PARA default, or the area `CONVENTIONS.md` marks as the inbox. Inbox
triage writes the same kind of note through its **To Brain** route, from a
capture or from an Inbox note, keeping the title and the body.

Whatever the person typed stays text: a heading, a fence or a meta block typed
into a capture is written so that it cannot become the note's structure. Each
such line gets a zero-width space just before its marker: invisible when the
note is read, and enough that no reader takes the line for structure.

The note's file is named after its title (`Ideas for the sync` is
`ideas-for-the-sync.md`), and a name already taken gets `-2`, `-3` and on:
nothing in the Brain is ever overwritten. Its `meta` block says `type: note`
and its [source](domain.md#note-source), `manual` from Capture and `inbox` from
To Brain. Once written, the pane opens it. One writer, `IBrainNoteWriter`, does
all of this for Capture and for To Brain alike, and so does the capture tool
an assistant calls over MCP ([MCP Brain scope](#mcp-brain-scope)).

## MCP Brain scope

```meta
type: feature
depends-on: [.devbook/domain/brain/features.md#capture-into-the-brain]
related: [.devbook/domain/brain/requirements.md#mcp-brain-scope, .devbook/arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md, .devbook/domain/devbook/features.md#review-notes-in-a-chapter]
tests: [unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.BrainScopeToolsTests, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.McpToolCreationTests.A_host_without_the_brain_ports_still_builds_the_devbook_tools]
```

Let an assistant read and write the Brain through the same tools it uses on a
repository devbook. The knowledge tools the app serves over MCP take the Brain as
`#brain` in the place of a repository's `owner/name`. They list the contexts, read
a chapter, and list and resolve the reader's [remarks](../devbook/domain.md#remark).

The spelling is not the empty string the shell stores for the Brain (local
ADR 0027 §5). An empty repository argument is already refused as missing, and a
session that forgot it must never be handed the person's own notes. On these
tools that refusal names both things the argument can be: an `owner/name`, or
`#brain` for the Brain. It is not
`brain` either, which a repository could be called. A GitHub owner or name cannot
hold a `#`, and a configured repository line that starts with one is a comment.
So `#brain` matches no repository, and it is matched before any repository is
looked up.

A Brain note is named by its path from the Brain's root (`projects/plan.md`),
never under a dot-folder and never through a link out of the folder. The
remarks on it are the ones the reader left on the Brain, and a remark is
resolved only under the scope it belongs to.

The capture tool, `capture_note`, writes a new note into the Brain's inbox
area through the same writer the pane's Capture uses, under the same guard on
the text and with source `manual`. A second capture with the same title is a
second note, never an overwrite. The tool answers the note's path from the
Brain's root, which the read tool takes as it is. Where no Brain is set, or the
host has none, the tools say so in words rather than failing.

