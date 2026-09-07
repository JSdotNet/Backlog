# Backlog Import Plan Grammar

Reference for `skills/backlog-import-plan`. Restates the grammar a generated plan must
match — nothing here is invented; it mirrors Backlog's own decision
(`.arc42/adr/0007-import-reuses-the-entry-text-grammar.md`) and its entry-text rules
(`.design/content-editing.md#scheduling-and-dependency-tokens`) in the Backlog product
repository. A plan is not a file format of its own — it is the same Backlog Entry text
grammar, with more than one entry in the document.

## Document shape

One Markdown document. No wrapper heading, no front matter, no plan-level metadata: a
second top-level `# Title` starts a new entry, exactly as pasting several hand-typed
entries at once already does.

Each entry, in this order:

1. `# <Title>` — the entry title.
2. One backtick-quoted metadata line.
3. Body prose — the entry's instructions, and the primary content.
4. `##` sub-item headings and/or `- [ ]` checklist lines.

## Metadata line

Sigils (no colon; order sigils before named tokens):

| Sigil | Kind | Values |
|---|---|---|
| *(none)* | type | `prompt`, `task`, `idea` |
| `!` | status | `!draft`, `!ready`, `!in-progress`, `!done`, `!archived` — an entry stating none is imported at `ready`; write `!draft` to hold one back |
| `*` | priority | `*low`, `*medium`, `*high`, `*critical` |
| `@` | area | any slug, e.g. `@repos` |
| `#` | tag | any slug, e.g. `#vscode-desktop-rollout` |

Named tokens (`name:value`):

| Token | Meaning | Repeats |
|---|---|---|
| `id:<slug>` | local id for this entry, resolved only within this pasted document | no |
| `after:<id-or-real-id>` | waits on another entry; resolves against a same-document `id:` first, else a real backlog item id | yes |
| `repo:<name>` | target repository, resolved by name; Import auto-registers an unrecognized name | yes |
| `due:<YYYY-MM-DD>` | due date | no |
| `effort:<points>` | size in story points; a non-negative whole number, and the app's picker offers `1`, `2`, `3`, `5`, `8`, `13`, `21` | no |

A plan states `!ready` and an `effort:` on every entry. The order of the work is carried by
`after:` alone, so a later entry in the chain is emitted `!ready` like the first rather than
held at `!draft`; `!draft` is for an entry still being shaped, which a generated plan has
none of. `effort:` sits after `repo:` on the line, which is where Backlog itself writes it
back.

## Worked example

```markdown
# Add the export command

`prompt` `*high` `!ready` `@repos` `#vscode-desktop-rollout` `id:add-command` `repo:backlog-desktop` `effort:5`

Add the plan name `vscode-desktop-rollout` to this session's title before you start.

Add an export command to the command palette that serializes the current view to Markdown.

## Setup: install the command-palette SDK

## Manual: confirm the export format with design

## Update backlog-desktop's own knowledge docs / devbook once this prompt lands

# Wire the export command into the toolbar

`prompt` `!ready` `@repos` `#vscode-desktop-rollout` `id:wire-toolbar` `after:add-command` `repo:backlog-desktop` `effort:2`

Add the plan name `vscode-desktop-rollout` to this session's title before you start.

Wire the command from the previous prompt into the toolbar as a button.

## Update backlog-desktop's own knowledge docs / devbook once this prompt lands

# Review the VS Code desktop rollout plan for anything missed

`prompt` `!ready` `@repos` `#vscode-desktop-rollout` `id:review-plan` `after:wire-toolbar` `repo:backlog-desktop` `effort:2`

Add the plan name `vscode-desktop-rollout` to this session's title before you start.

Read the material this plan came from against what actually landed: every entry done, and
nothing dropped, deferred or left half-finished on the way. Write up anything still
outstanding as a new entry.

## Manual: sign off that the plan is complete
```

## Sub-item conventions

- `## Setup: ...` — a prerequisite the target repository needs before the entry's own
  instructions make sense (update a plugin, install one, make a related change first).
  Ordered ahead of everything else in the entry.
- `## Manual: ...` — a step only a human can do.
- One further `##` sub-item reminding whoever runs the prompt to update the target
  repository's own knowledge folders or devbook once it is done. Every entry carries one;
  it is never optional. The plan's closing review entry is the single exception — it
  changes no repository of its own, and carries a `Manual: ...` sign-off instead.
- `- [ ]` checklist lines are for granular steps inside a sub-item, not a substitute for a
  `##` sub-item.

## Plan identity and re-import

- Every entry generated for one plan shares one `#tag`, a slug derived from the plan's
  subject — this is the plan's whole identity; there is no separate plan-id field.
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
