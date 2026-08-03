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
  <item-slug>.md
```

Each file is one top-level work item (epic, feature, story, or bug), written
as a chapter. Break the item into sub-items as sub-chapters within the same
file, the same way `domain.md` nests Entities/Value Objects/Enums under
their owning Aggregate — do not split sub-items into separate files or sort
items into type-named subfolders.

```markdown
# <Item Name>

Type: epic | feature | story | bug
Status: draft | ready | in progress | done

Description of the item.

## <Sub-item Name>

Description of the sub-item.

## <Next Sub-item Name>

...
```

## Authoring guidance

- Use `write-epic`, `write-story`, and `write-bug` skills to draft new items
  in a consistent format before saving them here.
- Use `create-github-issue` / `update-github-issue` to publish or sync a
  backlog artifact to GitHub Issues once it is ready — do not hand-author
  issue bodies that diverge from the saved artifact.
- Keep item status current (e.g. a short status line: draft / ready / in
  progress / done) so the folder reflects real backlog state, not just
  history.
- For end-to-end feature or bug work spanning planning through
  implementation, route through `orch-feature` / `orch-bug` per
  `.github/instructions/workflow-routing.instructions.md` rather than working
  ad hoc from these files alone.
