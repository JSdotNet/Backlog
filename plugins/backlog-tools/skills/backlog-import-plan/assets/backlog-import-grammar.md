# Backlog Import Plan Grammar

Reference for `skills/backlog-import-plan`, which writes plans, and
`skills/backlog-run-plan-item`, which runs one entry of a plan pasted back out of Backlog.
Restates the grammar a generated plan must match — nothing here is invented; it mirrors
Backlog's own decision (`.devbook/arc42/adr/0007-import-reuses-the-entry-text-grammar.md`) and its
entry-text rules (`.devbook/design/content-editing.md#scheduling-and-dependency-tokens`) in the
Backlog product repository. A plan is not a file format of its own — it is the same
Backlog Entry text grammar, with more than one entry in the document, at up to two levels:
`plan` entries that become Roadmap Items and the step entries that become tasks
(`.devbook/arc42/adr/0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md`). The one thing this
file adds on top of that grammar is the [plan item marker](#plan-item-marker): a body-prose
convention of these two skills, not a token Backlog parses. The [entry marker](#entry-marker)
beside it is the app's own, documented here because the run skill reads both.

## Document shape

One Markdown document. No wrapper heading and no front matter: a second top-level
`# Title` starts a new entry, exactly as pasting several hand-typed entries at once already
does. What a plan says about itself as a whole is a [`plan` entry](#two-levels), which is an
entry like any other.

A generated plan is one of two shapes:

- **Step-level** (the default): one `plan` entry first, then the step entries, closing with
  the review prompt and the sign-off task.
- **Roadmap-level**: `plan` entries only, one per feature, and nothing else.

Each entry, in this order:

1. `# <Title>` — the entry title.
2. One backtick-quoted metadata line.
3. Body prose — the entry's instructions, and the primary content.
4. `##` sub-item headings and/or `- [ ]` checklist lines.

## Metadata line

Sigils (no colon; order sigils before named tokens):

| Sigil | Kind | Values |
|---|---|---|
| *(none)* | type | `plan`, `prompt`, `task`, `test`, `idea` |
| `!` | status | `!draft`, `!ready`, `!in-progress`, `!done`, `!archived` — an entry stating none is imported at `ready`; write `!draft` to hold one back |
| `*` | priority | `*low`, `*medium`, `*high`, `*critical` |
| `@` | area | any slug, e.g. `@repos` |
| `+` | plan tag | the plan's slug, e.g. `+vscode-desktop-rollout` — stored with its sigil; this is what identifies the plan |
| `#` | tag | any other slug, e.g. `#deploy` — a general tag, never the plan's identity |

Named tokens (`name:value`):

| Token | Meaning | Repeats |
|---|---|---|
| `id:<slug>` | local id for this entry, resolved only within this pasted document | no |
| `after:<id-or-real-id>` | waits on another entry; resolves against a same-document `id:` first, else a real backlog item id | yes |
| `repo:<name>` | target repository, resolved by name; Import auto-registers an unrecognized name | yes |
| `due:<YYYY-MM-DD>` | due date | no |
| `effort:<points>` | size in story points; a non-negative whole number, and the app's picker offers `1`, `2`, `3`, `5`, `8`, `13`, `21` | no |

A plan states `!ready` and an `effort:` on every step entry; a `plan` entry states
neither (see [Two levels](#two-levels)). The order of the work is carried by
`after:` alone, so a later entry in the chain is emitted `!ready` like the first rather than
held at `!draft`; `!draft` is for an entry still being shaped, which a generated plan has
none of. `effort:` sits after `repo:` on the line, which is where Backlog itself writes it
back.

## Entry kinds

Backlog accepts five type words; a generated plan writes four of them. `plan` is the
roadmap level ([Two levels](#two-levels)); the other three are steps, and the type says who
does the work:

- **`prompt`** — an AI session runs it. Opens with the [plan item marker](#plan-item-marker)
  and the session-name line, states `repo:`, and may carry `## Setup:` sub-items and the
  knowledge/devbook reminder.
- **`task`** — only the user does it: a decision, a sign-off, an action in an account or on a
  machine an AI session cannot reach. A plain entry addressed to the user — title, metadata
  line, body saying what to do and what done looks like, `- [ ]` lines for the user's own
  steps. No marker, no session-name line, no `Setup:` or knowledge sub-item, and `repo:`
  only when the step is done in or to that repository.
- **`test`** — the user checks by hand that work already landed behaves as agreed: an
  acceptance or exploratory pass in the running app. Shaped exactly like a `task` — no
  marker, no session-name line, no `Setup:` or knowledge sub-item — with a body naming what
  to exercise and what a pass looks like, `- [ ]` lines for the checks, and `after:` on
  every prompt whose work it checks. Automated tests are never a `test` entry: they belong
  inside the prompt that writes the code.

A `prompt` never mixes with the other two. A manual step or check is never a sub-item,
checklist line or instruction inside a `prompt`, and a `task` or `test` never carries
instructions for an AI. Work that needs both is split: the manual step is its own `task`
(or `test`), and the prompts that need it done wait on it with `after:`. `idea` is a type
Backlog holds, not one a plan emits.

## Two levels

A `plan` entry is a Roadmap Item written in the same grammar, discriminated by the bare type
word `plan`. Import creates the item; it never becomes a task. On the metadata line it
carries:

- the **plan tag**, `+slug` — **required, exactly one**; a `plan` entry without one, or with
  two, is reported and skipped;
- `repo:` once per repository the plan targets (each becomes the item's repository scope;
  an unknown name is held as written, not registered);
- `*priority` only when the source implies one (absent reads as `medium`);
- `due:` only when the source states a date — it is the item's end; there is no start
  token, the importer places the start;
- `after:` on other `plan` entries, by their `id:`. `id:` defaults to the tag, so a `plan`
  entry need not write one.

It carries **no** `effort:` (an item's size is the effort its steps gather; the token is
reported and ignored), no `!status`, `@area` or `#tag` (ignored), no marker, no
session-name line and no sub-items. Its body, if any, becomes the item's notes.

The combination rules:

- **One document, both levels.** `plan` entries and step entries may share a document, or
  a document may hold only one kind. The steps are imported first, then the `plan` entries,
  so an item is placed against the effort its steps just registered.
- **The tag ties them.** A step does not inherit the `plan` entry's tag: every step writes
  the same `+slug` itself, and the item gathers its steps by that tag.
- **`after:` is scoped per level.** A step's `after:` resolves against step ids (then real
  backlog item ids); a `plan` entry's against sibling `plan` entries' ids (then the tag or
  id of an item already on the roadmap). An `after:` that names the other level is dropped
  and reported. A cycle between `plan` entries refuses the whole import.
- **Roadmap first and tasks first are both valid.** A roadmap-level document creates the
  items; a later step-level import of each plan fills its item by tag. Or the steps come
  first, and their tag waits on the roadmap's shelf until a `plan` entry, or a person,
  creates the item.
- **What a re-import changes.** On the step side, exactly what
  [Plan identity and re-import](#plan-identity-and-re-import) says: entries not yet started
  are replaced. On the roadmap side, an item matched by tag has its title, repository
  scope, priority, dependencies and notes replaced from the new `plan` entry, and its window
  re-placed only while the importer still owns it (a window a person moved is kept). An
  item is never deleted by a re-import. A step-level re-import changes nothing on the item
  but, while its window is still sized by effort, its length.

## Worked example

```markdown
# VS Code desktop rollout

`plan` `*high` `+vscode-desktop-rollout` `repo:backlog-desktop`

# Confirm the export format with design

`task` `!ready` `@repos` `+vscode-desktop-rollout` `id:confirm-format` `effort:1`

Agree with design whether the export is plain Markdown or Markdown with front matter, and
note the answer on this entry. Done when the format is written down here.

# Add the export command

`prompt` `*high` `!ready` `@repos` `+vscode-desktop-rollout` `id:add-command` `after:confirm-format` `repo:backlog-desktop` `effort:5`

Backlog plan item `add-command` of plan `vscode-desktop-rollout` for `backlog-desktop`, after `confirm-format` — run it with the `backlog-run-plan-item` skill.

Add the plan name `vscode-desktop-rollout` to this session's title before you start.

Add an export command to the command palette that serializes the current view to Markdown,
in the format agreed on the `confirm-format` entry.

## Setup: install the command-palette SDK

## Update backlog-desktop's own knowledge docs / devbook once this prompt lands

# Wire the export command into the toolbar

`prompt` `!ready` `@repos` `+vscode-desktop-rollout` `id:wire-toolbar` `after:add-command` `repo:backlog-desktop` `effort:2`

Backlog plan item `wire-toolbar` of plan `vscode-desktop-rollout` for `backlog-desktop`, after `add-command` — run it with the `backlog-run-plan-item` skill.

Add the plan name `vscode-desktop-rollout` to this session's title before you start.

Wire the command from the previous prompt into the toolbar as a button.

## Update backlog-desktop's own knowledge docs / devbook once this prompt lands

# Review the VS Code desktop rollout plan for anything missed

`prompt` `!ready` `@repos` `+vscode-desktop-rollout` `id:review-plan` `after:wire-toolbar` `repo:backlog-desktop` `effort:2`

Backlog plan item `review-plan` of plan `vscode-desktop-rollout` for `backlog-desktop`, after `wire-toolbar` — run it with the `backlog-run-plan-item` skill.

Add the plan name `vscode-desktop-rollout` to this session's title before you start.

Read the material this plan came from against what actually landed: every entry done, and
nothing dropped, deferred or left half-finished on the way. Write up anything still
outstanding as a new entry.

# Sign off the VS Code desktop rollout plan

`task` `!ready` `@repos` `+vscode-desktop-rollout` `id:sign-off-plan` `after:review-plan` `effort:1`

Read the review's outcome. Confirm the plan is complete, or pick up the follow-up entries
it wrote.
```

A roadmap-level document is `plan` entries alone, one per feature, ordered by `after:`:

```markdown
# VS Code desktop rollout

`plan` `*high` `+vscode-desktop-rollout` `repo:backlog-desktop`

# VS Code marketplace listing

`plan` `+vscode-marketplace-listing` `repo:backlog-desktop` `after:vscode-desktop-rollout` `due:2026-12-01`
```

## Plan item marker

The first body line of every `prompt` entry — a `task` entry has none, because nobody
pastes it into a session to run. It exists because of what Backlog's copy button hands
over: its own [entry marker](#entry-marker), then the entry's title, a blank line, and
its body — never the metadata line, which Backlog treats as its own bookkeeping. An entry
copied out of the app and pasted into a chat has therefore lost its `id:`, `+tag`, `repo:`
and `after:` unless the body restates them, and the marker is that restatement — the
plan's own slugs, which the app's marker does not carry — in a shape
`backlog-run-plan-item` recognizes:

```
Backlog plan item `<id>` of plan `<tag>` for `<repo>`[, `<repo>`…][, after `<id>`[, `<id>`…]] — run it with the `backlog-run-plan-item` skill.
```

- Clauses in exactly that order; the `after` clause is omitted when the entry waits on
  nothing. Every value is the same slug the metadata line carries, so `(tag, id)` in the
  marker is the pair Backlog itself matches an entry by across plan versions.
- The tag is written bare, not as `+tag` or `#tag`: a sigil in body prose is scanned as a
  tag by the parser, and the entry already carries the plan tag on its metadata line.
- The line is prose with backtick values in it, never backtick tokens alone. A body line
  made only of backtick tokens would be read as a metadata line the moment a copied entry
  — title, blank, body — is pasted back into Backlog.
- It goes before the session-name line, which stays: the marker is what a tool keys on,
  the session-name line is an instruction a person can follow without one.

## Entry marker

The line the Backlog app itself puts ahead of every entry its copy button hands over —
`task` entries included, since the app copies what it has and the skill is what declines to
run one. `backlog-import-plan` never writes it: it exists only once an entry is stored, and
a plan is written before that. It is a slash command rather than prose, so a paste that
opens with it invokes `backlog-run-plan-item` directly, with everything under it — the
title on the very next line, then the body — as the command's content:

```
/backlog-tools:backlog-run-plan-item entry `<id>`:
<Title>

<body>
```

- `<id>` is the entry's **stored** id — the one a `backlog` MCP server is asked for — never
  the local `id:` slug, and it is the only value the line carries. The plan, the
  repositories and the dependencies are the connector's to answer; without one, an
  imported prompt still has them in its plan-item marker, which is body prose and comes
  along under the title.
- The colon closes the command; the title follows on the next line with no blank line
  between, so the command and its content paste as one message.

## Sub-item conventions

These sub-items belong to `prompt` entries; a `task` entry carries none of them.

- `## Setup: ...` — a prerequisite the target repository needs before the entry's own
  instructions make sense (update a plugin, install one, make a related change first).
  Ordered ahead of everything else in the entry.
- One further `##` sub-item reminding whoever runs the prompt to update the target
  repository's own knowledge folders or devbook once it is done. Every prompt carries one;
  it is never optional. The plan's closing review prompt is the single exception — it
  changes no repository of its own.
- There is no manual sub-item. A step only a person can do is a `task` entry of its own
  (see [Entry kinds](#entry-kinds)), never a `## Manual:` heading inside a prompt.
- `- [ ]` checklist lines are for granular steps inside a sub-item, not a substitute for a
  `##` sub-item.

## Plan identity and re-import

- Every entry generated for one plan shares one `+tag`, a slug derived from the plan's
  subject and written with the `+` sigil — this is the plan's whole identity; there is no
  separate plan-id field. Backlog stores the value sigil and all, and its tag picker offers
  the same `+slug`, so a plan written `#slug` would be a different plan from the one the
  app shows.
- Reusing the exact same tag on a later regeneration is what makes it a new version of
  that plan rather than a second plan: Backlog clears every entry of that tag still
  waiting to be picked up (`draft`/`ready`) and writes the new version in their place, so
  a plan brought in twice cannot leave duplicates behind.
- An entry already `in-progress` is not cleared — the later version is applied to it in
  place, matched by `(tag, id:)`.
- A stored `done`/`archived` entry stays untouched whatever status a later plan version
  states for it — restating a status leaves finished work finished.
- Give **every** entry an `id:`, not only the ones another entry depends on. It is how an
  entry already under way or already finished is recognized across versions; one without
  an id is created new beside the entry it was meant to be.
- `after:` and `repo:` may each repeat on one entry; order among repeats carries no
  meaning.
- A `plan` entry is matched by its tag, not by an `id:`, and a re-import never deletes the
  item — see [Two levels](#two-levels).
