# Backlog knowledge (`.backlog`)

This folder tracks planned and in-progress work — epics, features, stories,
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
  epics/
    <epic-slug>.md
  features/
    <feature-slug>.md
  stories/
    <story-slug>.md
  bugs/
    <bug-slug>.md
```

Only create the subfolders you actually need; do not pre-create empty
placeholders for work item types that don't yet exist.

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
