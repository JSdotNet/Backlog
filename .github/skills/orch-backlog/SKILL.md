---
name: orch-backlog
description: 'Orchestrate changes to .backlog/ (durable work-item artifacts grouped by concern) for this repository. Use for any create/update of a .backlog/<concern-type>-<concern-slug>.md file, its Items, or Sub-items. Drafts content via write-epic/write-story/write-bug, enforces backlog.instructions.md structure and chapter-metadata.instructions.md metadata blocks, and hands off to create-github-issue/update-github-issue when publishing.'
---

# Orchestrate Backlog Knowledge (`.backlog/`)

Route every `.backlog/` change through this skill instead of hand-authoring
items directly, so drafted work items stay consistent in quality, structure,
and metadata, and so GitHub Issue sync stays in lockstep with the saved
artifact.

## Input Expectations

- Target concern file: `<concern-type>-<concern-slug>.md`, where
  `concern-type` is `domain` (matches a `.domain/<context>` folder), `feature`
  (cross-cutting, spans contexts), or `architecture` (matches an `.arc42`
  chapter/topic).
- Whether this is a new Item, a new Sub-item under an existing Item, or a
  status/content update to an existing one.
- Whether the item should be published/synced to a GitHub Issue.
- Any known relations: which `.domain` feature/aggregate or `.arc42` chapter
  this item implements, and which other backlog items it depends on.

## Workflow Stages

> Agent transitions require explicit user approval before switching.

### Stage 1: Context Loading
- Load `.github/instructions/backlog.instructions.md` and
  `.github/instructions/chapter-metadata.instructions.md` (task-scoped, not
  baseline context).
- Load only the target concern file (create it from the template if it does
  not exist yet) — not the whole `.backlog/` folder.
- If the item will carry `implements`, load only the referenced `.domain` or
  `.arc42` chapter to confirm the target heading/path is correct.

**Agents:** none (context loading only)

### Stage 2: Item Drafting
- For a new Item or Sub-item, draft its content with the matching skill for
  quality/shape: `write-epic`, `write-story`, or `write-bug`. These skills
  shape content quality, not a stored type label — the saved artifact still
  uses the plain Item/Sub-item chapter structure from
  `backlog.instructions.md`.
- Nest Sub-items under their parent Item in the same concern file, matching
  the `domain.md` Aggregate/Entity nesting pattern.
- For a content/status update to an existing item, edit in place without
  re-running the drafting skill unless the description materially changes.

**Skills Used:** `write-epic`, `write-story`, `write-bug`

### Stage 3: Metadata & Cross-Reference Enforcement
- Add or update the chapter metadata block on every new/edited Item and
  Sub-item: `status` required (`draft`, `ready`, `in-progress`, `done`,
  `blocked`); `depends-on`, `implements`, `related`, `issue` optional and
  omitted when empty.
- Add or update the file-level metadata block directly under the concern
  file's `# <Concern Name>` heading.
- Use `depends-on` only for sequencing against other backlog items/sub-items;
  use `implements` for the `.domain` Feature/Sub-feature, Aggregate/Domain
  Service, or `.arc42` chapter this item realizes; use `related` for any
  other general cross-folder tag.
- If a chapter heading or file was renamed/moved, update every `related`,
  `depends-on`, or `implements` entry elsewhere that references its old
  `<path>#<heading-slug>` or `<path>`.

**Agents:** none (structural enforcement)

### Stage 4: Publish / Sync (optional)
- If the item is ready to track in GitHub, hand off to `create-github-issue`
  (new item) or `update-github-issue` (existing item) so the issue body
  matches the saved artifact rather than diverging.
- Record the returned issue reference in the item's `issue` metadata field.
- For end-to-end delivery spanning planning through implementation, note
  that the user should continue with `orch-feature` or `orch-bug` rather than
  working ad hoc from the backlog file alone.

**Skills Used:** `create-github-issue`, `update-github-issue`

## Usage Pattern

```text
Invoke: orch-backlog
- Concern: feature-checkout
- Item: "Support partial refunds" (new Item, type: story)
- Implements: .domain/order-management/features.md#feature-refunds
- Publish: yes
```

## Output Expectations

- `.backlog/<concern-type>-<concern-slug>.md` updated following the exact
  structure in `backlog.instructions.md`.
- Every touched Item/Sub-item and the file itself carry a correct metadata
  block per `chapter-metadata.instructions.md`.
- `depends-on`/`implements`/`related` correctly distinguished and kept in
  sync with any file/heading renames.
- GitHub Issue created or updated and its reference recorded, when
  publishing was requested.

## Reference

- `.github/instructions/backlog.instructions.md`
- `.github/instructions/chapter-metadata.instructions.md`
- `.github/instructions/workflow-routing.instructions.md`
