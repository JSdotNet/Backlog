# Backlog Import Plan Grammar

Reference for `skills/backlog-import-plan`. Restates the grammar a generated plan must
match — nothing here is invented; it mirrors Backlog's own decision
(`.arc42/adr/0004-import-reuses-the-entry-text-grammar.md`) and its entry-text rules
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

| Sigil | Kind | Example |
|---|---|---|
| *(none)* | type | `` `prompt` `` |
| `!` | status | `` `!ready` `` |
| `*` | priority | `` `*high` `` |
| `@` | area | `` `@repos` `` |
| `#` | tag | `` `#vscode-desktop-rollout` `` |

Named tokens (`name:value`):

| Token | Meaning | Repeats |
|---|---|---|
| `id:<slug>` | local id for this entry, resolved only within this pasted document | no |
| `after:<id-or-real-id>` | waits on another entry; resolves against a same-document `id:` first, else a real backlog item id | yes |
| `repo:<name>` | target repository, resolved by name; Import auto-registers an unrecognized name | yes |
| `due:<YYYY-MM-DD>` | due date | no |

## Worked example

```markdown
# Add the export command

`prompt` `*high` `!ready` `@repos` `#vscode-desktop-rollout` `id:add-command` `repo:backlog-desktop`

Add an export command to the command palette that serializes the current view to Markdown.

## Setup: install the command-palette SDK

## Manual: confirm the export format with design

## Update backlog-desktop's own knowledge docs / devbook once this prompt lands

# Wire the export command into the toolbar

`prompt` `@repos` `#vscode-desktop-rollout` `after:add-command` `repo:backlog-desktop`

Wire the command from the previous prompt into the toolbar as a button.

## Update backlog-desktop's own knowledge docs / devbook once this prompt lands
```

## Sub-item conventions

- `## Setup: ...` — a prerequisite the target repository needs before the entry's own
  instructions make sense (update a plugin, install one, make a related change first).
  Ordered ahead of everything else in the entry.
- `## Manual: ...` — a step only a human can do.
- One further `##` sub-item reminding whoever runs the prompt to update the target
  repository's own knowledge folders or devbook once it is done. Every entry carries one;
  it is never optional.
- `- [ ]` checklist lines are for granular steps inside a sub-item, not a substitute for a
  `##` sub-item.

## Plan identity and re-import

- Every entry generated for one plan shares one `#tag`, a slug derived from the plan's
  subject — this is the plan's whole identity; there is no separate plan-id field.
- Reusing the exact same tag on a later regeneration of the same plan lets Backlog's
  import upsert by `(tag, id:)`: an entry not yet `done`/`archived` is updated in place,
  a `done`/`archived` one is left untouched, and an `id:` not seen before is created new.
- An entry with no `id:` is always created new — it can never be matched by a later
  re-import, because there is nothing to match it against.
- `after:` and `repo:` may each repeat on one entry; order among repeats carries no
  meaning.
