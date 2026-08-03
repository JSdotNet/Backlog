---
applyTo: ".backlog/**"
description: Structure and authoring rules for the backlog work-item knowledge folder.
---

# Backlog knowledge (`.backlog`)

`.backlog` tracks planned and in-progress work — epics, features, stories,
and bugs — as durable Markdown artifacts, separate from whatever issue
tracker or project board is in use day to day.

## Relationship to other knowledge folders

- `.domain` and `.arc42` describe stable knowledge (domain model,
  architecture). `.backlog` describes *change* — the work items that move the
  system from its current state toward the target state described there.
- Backlog items should reference the bounded context(s) in `.domain` they
  affect and any arc42 sections or ADRs they touch, instead of restating that
  context inline.

## Structure

```
.backlog/
  <concern-type>-<concern-slug>.md
```

`.backlog` can hold multiple files, split by concern rather than by work
item type. Each file groups the work items for one concern as chapters, with
sub-items nested as sub-chapters within the same file — the same way
`domain.md` nests Entities/Value Objects/Enums under their owning Aggregate.

### Filename convention

`<concern-type>-<concern-slug>.md`, where `concern-type` is one of:

- `domain` — work scoped to one bounded context; `concern-slug` matches the
  `.domain/<bounded-context-name>` folder name
  (e.g. `domain-order-management.md`).
- `feature` — work scoped to a cross-cutting feature that spans bounded
  contexts; `concern-slug` is the feature name
  (e.g. `feature-checkout.md`).
- `architecture` — work scoped to an architectural concern; `concern-slug`
  matches the relevant `.arc42` chapter/topic
  (e.g. `architecture-observability.md`).

Do not sort items into type-named subfolders (`epics/`, `bugs/`, etc.) —
type is a property of the item (see template below), not part of the
filename or folder structure.

```markdown
# <Concern Name>

## <Item Name>

\`\`\`meta
id: backlog:<concern-type>-<concern-slug>#<item-slug>
status: draft
depends-on: []
related: []
issue: null
\`\`\`

Type: epic | feature | story | bug

Description of the item.

### <Sub-item Name>

\`\`\`meta
id: backlog:<concern-type>-<concern-slug>#<item-slug>-<sub-item-slug>
status: draft
depends-on: []
related: []
issue: null
\`\`\`

Description of the sub-item.

### <Next Sub-item Name>

...

## <Next Item Name>

...
```

## Authoring guidance

- Use `write-epic`, `write-story`, and `write-bug` skills to draft new items
  in a consistent format before saving them here.
- Use `create-github-issue` / `update-github-issue` to publish or sync a
  backlog artifact to GitHub Issues once it is ready — do not hand-author
  issue bodies that diverge from the saved artifact.
- Keep item status current in the `meta` block's `status` field so the
  folder reflects real backlog state, not just history.
- For end-to-end feature or bug work spanning planning through
  implementation, route through `orch-feature` / `orch-bug` per
  `.github/instructions/workflow-routing.instructions.md` rather than working
  ad hoc from these files alone.
- Every Item and Sub-item must carry the metadata block described in
  `.github/instructions/chapter-metadata.instructions.md` (status,
  dependencies, cross-folder tags, GitHub issue link) — required for the
  planned visualization tooling.
