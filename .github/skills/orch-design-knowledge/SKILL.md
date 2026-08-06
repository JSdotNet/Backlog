---
name: orch-design-knowledge
description: 'Orchestrate changes to .design/ (UX principles, dark-mode color tokens, typography/layout, interaction guidelines, content editing, accessibility, component libraries) for this repository. Use for any create/update/refresh of .design/README.md, design-principles.md, color-scheme.md, typography-and-layout.md, interaction-guidelines.md, content-editing.md, accessibility.md, or component-libraries.md. Grounds guidance in the jsdotnet-project-design MCP server and enforces design-knowledge.instructions.md structure and chapter-metadata.instructions.md metadata blocks before saving.'
---

# Orchestrate Design Knowledge (`.design/`)

Route every `.design/` change through this skill instead of editing the folder
directly, so UX guidance stays grounded in `jsdotnet-project-design`, consistent
with `ux-design:ux-designer`'s expertise, and aligned with this repository's
structure and metadata conventions.

## Input Expectations

- Target scope: which `.design/` file(s) are in scope.
- Change goal (e.g. refresh the palette from the design MCP, add an interaction
  rule, re-evaluate a component library for a channel).
- Whether the change is a new guideline or a refinement of an existing one.

## Non-Goals

- Wireframes, user flows, prototypes, and UI reviews — route those to
  `ux-design:ux-designer` directly (`ux-wireframe`, `ux-user-flow`,
  `ux-design-review`).
- UI implementation — route to `orch-feature` / `orch-bug`, which *consult*
  `.design/`.
- Adding or pinning UI dependencies — route to `orch-update-packages`.

## Workflow Stages

> Agent transitions require explicit user approval before switching. If
> `ux-design:ux-designer` is not installed, perform the design step directly
> using the same instructions files and continue.

### Stage 1: Context Loading
- Load `.github/instructions/design-knowledge.instructions.md` and
  `.github/instructions/chapter-metadata.instructions.md` (task-scoped, not
  baseline context).
- Load only the relevant `.design/` file(s), not the whole folder.
- Load `.arc42/` chapters only when the change depends on a documented
  constraint or stack decision — typically
  `.arc42/02-constraints.md#technical-constraints` and
  `.arc42/04-solution-strategy.md#technology-choices`.

**Agents:** none (context loading only)

### Stage 2: Authoritative Grounding
- Query the `jsdotnet-project-design` MCP server for the guidance in scope,
  and for the color scheme / design tokens whenever `color-scheme.md` or
  `typography-and-layout.md` is touched.
- Materialize MCP values into the repository as concrete tokens; do not leave a
  bare link to the MCP server.
- If the MCP server is unavailable, say so explicitly, mark the affected
  chapters `status: draft`, and record the gap in the chapter.

**Agents:** none (retrieval only)

### Stage 3: Design Authoring
- Hand off to `ux-design:ux-designer` for the actual design decisions.
- Draft or refresh content following the structure and folder rules in
  `design-knowledge.instructions.md`.
- Enforce the standing product rules on every edit: dark mode only, no save
  buttons (auto-save everywhere), Markdown canonical behind the rich text
  editor, drag-and-drop reordering of both files and chapters with a keyboard
  equivalent.
- Keep rules prescriptive and testable; prefer tables and token names over
  prose, and reference tokens instead of repeating raw values.

**Agents:** `ux-design:ux-designer`

### Stage 4: Metadata & Cross-Reference Enforcement
- Add or update the file-level metadata block on every touched file and the
  chapter metadata block on every new/edited `##` chapter.
- Set `status` from this folder's allowed values: `draft`, `active`,
  `deprecated`.
- Keep `related` entries pointing at valid `<path>#<heading-slug>` or `<path>`
  targets, and omit empty optional fields per the omit-when-empty rule.
- If a chapter heading or file was renamed/moved, update every `related` entry
  elsewhere (including in `.arc42/` and `.backlog/`) that references its old
  reference.

**Agents:** `ux-design:ux-designer`

### Stage 5: Consistency Review
- Confirm no light-mode guidance, theme toggle, or save affordance was
  introduced.
- Confirm every drag-and-drop rule has a documented keyboard alternative and
  an announced state change in `accessibility.md`.
- Confirm tokens are declared once and referenced elsewhere, and that
  per-stack mapping guidance did not fork into divergent designs.
- Confirm no new top-level metadata field was invented without updating
  `chapter-metadata.instructions.md` or `design-knowledge.instructions.md`
  first.
- Summarize changed files/chapters for the user.

**Agents:** `ux-design:ux-designer`

## Usage Pattern

```text
Invoke: orch-design-knowledge
- Files: color-scheme.md, interaction-guidelines.md
- Goal: refresh the dark palette from the design MCP and add the
  drag-and-drop chapter-reorder rules
```

## Output Expectations

- `.design/` files updated following `design-knowledge.instructions.md`.
- Color and typography tokens traceable to `jsdotnet-project-design`, or
  explicitly marked `draft` when the MCP server could not be reached.
- Every touched chapter and file carries a correct metadata block per
  `chapter-metadata.instructions.md`.
- Cross-references kept in sync across the changed and any dependent files.
- Changed paths summarized for the user.

## Reference

- `.github/instructions/design-knowledge.instructions.md`
- `.github/instructions/chapter-metadata.instructions.md`
- `.github/instructions/mcp-usage.instructions.md`
- `.github/instructions/workflow-routing.instructions.md`
