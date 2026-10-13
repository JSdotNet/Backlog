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
related: [.devbook/domain/inbox/features.md#routing, .devbook/domain/devbook/features.md#capture-into-the-brain]
```

Store notes, references, ideas, and learnings as markdown from inbox triage,
manual creation, or import, attaching them to one or more projects, topics, or
tags. Every note is a file in the person's [Brain](domain.md#brain); where it
comes from is [Capture into the Brain](#capture-into-the-brain).

## PARA organization

```meta
type: feature
status: draft
depends-on: [.devbook/domain/devbook/features.md#knowledge-capture]
related: [.devbook/domain/devbook/domain.md#para-category, .devbook/domain/devbook/features.md#brain-folder-setup]
```

Organize notes into the areas the Brain's `CONVENTIONS.md` declares, one top
folder each, shown as one tab each. A Brain that declares none uses the PARA set:
an inbox for what is not filed yet, Projects (active, deadline), Areas
(ongoing), Resources (reference), People, a Journal, and Archive (inactive).
Moving a note to another area is moving its file to another folder; a note may
name its area in its `para` field instead, which wins over the folder.

## Cross-project linking

```meta
type: feature
status: draft
related: [.devbook/domain/devbook/features.md#repo-links-and-backlinks]
```

Reference multiple projects and repos from a single note, and discover notes
across projects by tag. A project note names the repositories it concerns in its
`repos` field; a note that points at one chapter of a repository's devbook does
so with a [repo link](#repo-links-and-backlinks).

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
loosely "tags". A chapter may also declare an [Effort](domain.md#effort); like the roadmap contribution it is registered here and only read by Roadmap Planning.

## Bi-directional linking

```meta
type: feature
status: draft
related: [.devbook/domain/tasks/features.md#search-filter-and-organize]
```

Link from tasks to notes (reference or embed inline) and from notes
back to related tasks or projects, supporting queries that cross both
domains and embedding knowledge context directly in task details.

## Brain scope

```meta
type: feature
status: draft
related: [.devbook/domain/devbook/requirements.md#brain-scope, .devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md, .devbook/domain/tasks/features.md#search-filter-and-organize, .devbook/design/interaction-guidelines.md#shell-header]
```

Pick the Brain the way a repository is picked. The header's repository scope
leads with a **Brain** chip, set off from the repository chips by a divider and
worn in the Brain's teal. It cannot be removed, and it needs no repository, so
the scope works for a person who has configured none. Brain is the scope of
everything filed against no repository: pressed alone, it narrows Tasks, Board,
Calendar, In progress, the Dashboard, Sessions and Pull requests to the tasks
with no repository, and it opens the Brain in the Devbook pane. Ctrl adds it to
the repositories already pressed, and nothing pressed still means everything.

The scope is remembered with Brain in its place, so a fresh shell reopens on it.
It replaces the Tasks filter bar's *No repo* chip, which narrowed the same rows
under a second name and was never remembered. Until a Brain folder is set up the
chip reads **Set up your Brain** and still scopes the tasks.

## Brain folder setup

```meta
type: feature
status: draft
setting: .devbook/domain/devbook/context.md#brain-folder
related: [.devbook/domain/devbook/requirements.md#brain-folder-setup, .devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md, .devbook/domain/devbook/domain.md#para-category]
```

Choose where the Brain lives, once, in Settings › Storage. The person picks a
folder outside every repository; there is no default, and OneDrive is the
expected place. The Brain card says whether the folder is synced, as
information rather than a warning, that its search database is built in the
app's storage and never synced, and whether the folder's `README.md` and
`CONVENTIONS.md` are present.

**Create conventions** writes both files and every area folder they declare from
a template the app ships, the PARA set by default, and never overwrites a file
that is already there. From then on the folder carries its own convention, so a
second machine that reads the synced folder sees the same areas.

The shipped `CONVENTIONS.md` declares its areas as a table under an `## Areas`
heading, one row per area in tab order with its name, its folder and what it
holds; with no rows, the PARA set applies. The area folders Create conventions
makes are the ones the folder's own `CONVENTIONS.md` declares, so a person who
wrote theirs first gets their areas, not the template's. **Browse** opens the
Windows folder picker in the installed app; a host without one, the browser
harness among them, takes a typed path.

## Brain mode of the Devbook pane

```meta
type: feature
status: draft
feature-flag: .devbook/domain/devbook/context.md#devbook-pane
related: [.devbook/domain/devbook/requirements.md#brain-mode-of-the-devbook-pane, .devbook/domain/devbook/features.md#area-selection-and-scope, .devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md]
```

Read and write the Brain in the same pane that reads a repository's devbook.
With the scope's anchor on Brain, or nothing pressed, the pane carries a teal
**Brain** badge after its title, the folder's path where a repository's branch
picker would be, and one tab per area the Brain declares. The tree, the rendered
chapter and the editor are the ones a repository devbook uses, and a Brain note
is always editable. What only a repository has is absent: branch updates, sync
verdicts and scenario marks. So are the repository's atlas and opening the
folder in an editor, until the Brain has its own ([Atlas edges](#atlas-edges)).
Search is offered on the Brain as on a repository, because it is one
[Unified search](#unified-search) wherever it is opened. Ask AI takes no Brain
note as the open chapter.

With Brain and a repository both pressed, a `Brain | <repo>` switch beside the
pane title flips between them without touching the task filter. The switch opens
on the anchor's side. With Brain as the anchor and several repositories pressed,
its other side is the first of them. The switch is not remembered: any change to
the scope puts the pane back on the anchor.

On the Brain, and while the switch shows, the pane names itself *Devbook* at the
start of its tab strip. The switch sits right after the title, so it stays in
place as the badge and the path come and go.

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
status: draft
depends-on: [.devbook/domain/devbook/features.md#brain-mode-of-the-devbook-pane]
related: [.devbook/domain/devbook/requirements.md#repo-links-and-backlinks, .devbook/domain/devbook/domain.md#repo-link, .devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md]
```

Point a Brain note at the repository chapter it is about. A
`repo:<alias>/<devbook path>[#slug]` link, in the note's `related` or its body,
renders as a gold chip that opens that repository's devbook at the chapter. An
alias this machine does not have renders as a dashed chip reading *not on this
machine*, never as an error, because the same Brain is read on machines with
different repositories. A link resolves when its alias names a repository
configured here, as a clone or a branch snapshot, whose devbook shows the
section the path lies in. A configured alias whose path lies in no such section
is dashed too, and says it is not a chapter in that devbook. Following a gold
chip leaves the header scope alone: the pane reads the repository as it does for
a task's reference, and the switch or the scope brings the Brain back.

The reverse is shown, never written. Under a repository chapter a teal **From
your Brain** box lists the Brain notes that link to it, marked *Only you see
this*: it reads the owner's own Brain database, and nothing is written into the
repository, so personal notes never reach shared history. A link to any
heading of the chapter's file counts. Each note is listed once: under the
first chapter it links from, when every link it has sits in a chapter, and on
its own when one is in its `related` or its prose outside every chapter. A chapter no note links to draws no box. A repository devbook has no way
to link into a Brain.

## Unified search

```meta
type: feature
status: draft
depends-on: [.devbook/domain/devbook/features.md#devbook-retrieval]
related: [.devbook/domain/devbook/requirements.md#unified-search, .devbook/arc42/08-crosscutting-concepts.md#devbook-database]
```

Ask one question of the Brain and every configured repository's devbook at once.
The search offers scope chips (**Everywhere**, **Brain**, and one per
repository) and groups its results by where they came from, the Brain's group
first. It searches by words and by meaning, as
[Devbook retrieval](#devbook-retrieval) does for one devbook, over each source's
own database. A source with no database yet is named as not searchable rather
than silently left out.

Search by meaning is additive, as it is for one devbook: it is asked only while
it is switched on, once typing pauses, and the words answer is drawn while it is
on its way. Within a source the two answers are merged by where each put a
chapter, never by their scores, which are on different scales; a chapter both
found is one result. Opening a result from the devbook on screen opens it there.
Opening one from another devbook moves the pane to that devbook and opens it,
without touching the header's scope or the task filter.

## Atlas edges

```meta
type: feature
status: draft
depends-on: [.devbook/domain/devbook/features.md#repo-links-and-backlinks]
related: [.devbook/domain/devbook/requirements.md#atlas-edges, .devbook/design/component-libraries.md]
```

See where the Brain reaches into the repositories. In the Brain's atlas each
resolved repo link is an edge to a node for the repository chapter, labelled
with the repository's alias, so the notes that lean on one repository gather
around it. A link this machine cannot resolve is drawn dashed, like its chip.

## Capture into the Brain

```meta
type: feature
status: draft
depends-on: [.devbook/domain/devbook/features.md#brain-mode-of-the-devbook-pane]
related: [.devbook/domain/devbook/requirements.md#capture-into-the-brain, .devbook/domain/inbox/features.md#route-to-brain, .devbook/domain/inbox/domain.md#itemtriaged]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainCaptureTests, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainNoteWriterTests, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainNoteTextTests]
```

Get a thought into the Brain without leaving what is on screen. In Brain mode
the pane header carries **Capture**: a title and a Markdown body, written as a
new note with its `meta` block in the Brain's inbox area, which is `_inbox`
under the PARA default or the area `CONVENTIONS.md` marks as the inbox. Inbox
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
all of this for Capture and for To Brain alike, and the capture tool an
assistant calls is to use it too.

## MCP Brain scope

```meta
type: feature
status: draft
depends-on: [.devbook/domain/devbook/features.md#capture-into-the-brain]
related: [.devbook/domain/devbook/requirements.md#mcp-brain-scope, .devbook/arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md, .devbook/domain/devbook/features.md#review-notes-in-a-chapter]
```

Let an assistant read and write the Brain through the same tools it uses on a
repository devbook. The knowledge tools the app serves over MCP (listing the
contexts, reading a chapter, listing and resolving the reader's remarks) take
the Brain's reserved scope beside the repository aliases. A capture tool writes
a note into the Brain's inbox area through the same writer the pane's Capture
uses, under the same guard on the text.

## Repository devbook areas

```meta
type: feature
status: draft
feature-flag: .devbook/domain/devbook/context.md#devbook-pane
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
feature-flag: .devbook/domain/devbook/context.md#devbook-pane
related: [.devbook/domain/devbook/features.md#brain-mode-of-the-devbook-pane, .devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md]
```

Switch between areas, and between repositories when more than one is registered,
so the knowledge shown always belongs to a known repository or to the Brain. The
pane reads one devbook at a time even when the backlog beside it is scoped to
several: it follows the scope's anchor, the first chip taken into that scope, so
widening the list never moves the knowledge already being read. An anchor of
Brain, or nothing pressed, shows the [Brain](#brain-mode-of-the-devbook-pane),
because the Brain is always there; a scope holding Brain and a repository offers
a `Brain | <repo>` switch in the pane that flips what it shows and leaves the
task filter alone (local ADR 0027). The folder the backlog is kept in is never
read as a devbook of its own. Every area is opt-in, and each can be
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

Documents appear in the order the convention gives them — each directory's
introducing document first, numbered chapters by their number, a folder's usual
chapters in their usual places, and the rest by name — so a repository never has
to write its reading order down, and a file that tries to is ignored. Inside a
bounded context, the rules a domain page's types enforce are a page of their own
that reads directly after it, and the menu lists them as that page's invariants
— "Invariants", or "Order invariants" beside a page split out for one
aggregate — rather than by their file name. A context that still keeps its rules
in the older single invariants file has them read and listed where they always
were.

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
  actors, and a page of its own that the convention does not name. A
  requirements or invariants page is shown under the title it carries, which
  names what it holds rather than the context. How a
  bounded context ships — as a service of its own or as a module inside a shared
  host — is shown with it, and said out loud when the context map and the
  context's own boundary document disagree about it.
- **Everything else a chapter declares** — the tests that back it, its number
  and place in the reading order, the day it records — is shown as it was
  written, and state another tool keeps on the chapter under its own name is
  carried through untouched.

A bounded context's click demo is shown with the page it belongs to. A demo is
the one HTML file a context holds: a clickable picture of what a person sees. It
pairs with a page in two ways. By name, `demo.html` belongs to the context's
boundary document and `<page>.demo.html` to the page beside it. By field, a
chapter's `demo` metadata names a demo and the places in it that show the
chapter. The context view shows the context's own demo, and every other page
shows the demos paired with it.

Each demo runs in a frame of its own, isolated from the app, and can go full
screen. It keeps its own look, so the pane never forces its dark theme onto it.
Every place a chapter names in the demo is offered as a chip above the frame.
The first place opens as soon as the demo says it is ready, and picking another
chip moves the demo there. Notes on a demo are placed in spec-manager, the
specification review tool, so the pane neither pins notes nor follows where the
reader clicks inside the demo.

A scenario page is listed in the bounded context it lives in, at whatever depth,
like any page beside it. A scenario page is one end-to-end journey that says
`type: scenario` in its file-level metadata, and its parts are the outcomes the
actor reaches. Which pages are journeys comes from the scenario register the
repository's devbook commits. A repository that commits no register is read for
the pages that say so themselves. In the menu each journey carries the scenario
mark and a dot for its last run: passed, failed, stale, or never run. The dot
differs by shape as well as by colour, and a screen reader hears its state.

The run is the one committed beside the scenario folder's copy of the page,
`<scenario folder>/<stem>/run.json`. The app reads both the current run format
and the older one spec-manager's test tooling writes. Whether a run is stale is
worked out, never stored. The app signs the page as it reads now, with devbook's
signature and against devbook's shared test vector. A run that proved a different
signature is stale, and so is an older run that proves no signature at all.

Opening a journey shows its run above the page. The run shows the setup the
journey runs under: where it starts, who is signed in, the profile, the data sets,
and the flag and setting overrides. Under the setup is a summary line, then each
part with its outcome and the screenshots it took. A failed part also shows the
screenshot taken where it failed.

An image written `scenario:<stem>#<label>` in any chapter shows that screenshot
from the journey's last run, with a source line under it naming the journey, the
part, the run's state and the time of the capture. Two such images in one
paragraph sit side by side. A label the journey does not have lists the labels
it does have. A journey that does not exist, or a name two journeys share, says
so. A label the run never captured says that too. A capture older than the last
run says so on its source line.

### Remarks on a chapter

```meta
type: sub-feature
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

### Sync verdicts beside a chapter

```meta
type: sub-feature
related: [.devbook/domain/devbook/domain.md#sync-verdict, .devbook/domain/sessions/features.md#what-started-a-run-and-what-it-found, .devbook/domain/productivity/features.md#drift-at-a-glance]
```

Show beside each chapter heading the last verdict a devbook sync sweep reached on it —
aligned, code ahead, spec ahead, in conflict, or unresolved — and on the root chapter
of a sync unit the unit's own verdict and what the sweep did about it. Each badge
leads to the pull request or drift issue the sweep opened, where the verdict is
argued. Only the latest verdict per chapter is shown, and a sweep replaces only the
chapters it checked, so a chapter last checked a month ago keeps that month-old word
until a sweep checks it again. The words are the sweep's, verbatim; a word outside
its vocabulary is shown as written and flagged rather than coloured like one it is
not. A chapter no sweep has checked carries nothing.

Each verdict also keeps the unit's sync direction as the sweep resolved it, and where
it was inherited from. The dashboard's
[Drift at a glance](../productivity/features.md#drift-at-a-glance) part reads the
same verdicts a unit at a time and groups them by that direction.

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
becomes the knowledge that arrived with it.

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

Two costs are named separately and never traded silently: [Context Load](domain.md#context-load), and the person's own cost of knowing which document to reach for, which is not a number to drive to zero. An instruction
set that got smaller while the agents got worse has failed, and the review reports
that rather than a saving.

### Instruction set inventory

```meta
type: sub-feature
status: draft
related: [.devbook/domain/devbook/domain.md#instruction-set]
```

See every [Instruction Set](domain.md#instruction-set) the repository carries, document by document. Each is shown with what it costs to load and whether
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
Documents in [Instruction Alignment](domain.md#instruction-alignment) are not duplicates, so the finding is a disagreement or an omission and never the fact that both exist. Documents
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

Show every claimed reduction with its [Saving Evidence](domain.md#saving-evidence); a figure with none is not shown.

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

Click demos are indexed beside the chapters, each with the page it pairs with by
name and every chapter `demo` field that points into it. A demo is not a
chapter, so it has no place in the reading order and is not searched. An
assistant that reads a page is told which demos the page has, by path and by the
place each field names, but never receives a demo's HTML. A demo runs to
hundreds of kilobytes, and the assistant can open the file from its own checkout
when it needs one.
