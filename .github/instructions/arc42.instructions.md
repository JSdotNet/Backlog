---
applyTo: ".arc42/**"
description: Structure and authoring rules for the arc42 architecture documentation folder.
---

# Architecture documentation (`.arc42`)

`.arc42` holds arc42-structured architecture documentation for the system:
context, building blocks, runtime views, cross-cutting concerns, and
architecture decisions, at the level of the whole system or a major
deployable unit.

## Context-loading policy

- `.arc42` is **not** baseline repository context. Load it only for architecture,
  ADR, blueprint, TDR, or explicit arc42 tasks, normally after routing through
  `orch-arc42`, `orch-architecture`, `orch-blueprint`, `orch-adr`, `orch-tdr`, or
  `architecture:architect`.
- When `.arc42` is needed as task context, load only the relevant chapter(s) or
  sections instead of reading the whole folder by default.
- For non-architecture implementation or documentation tasks, consult `.arc42`
  only when the user asks for architecture context or when the work depends on a
  specific documented constraint, decision, runtime view, deployment view, or
  glossary entry.

## Relationship to other knowledge folders

- `.domain` describes *what the domain is* (bounded contexts, aggregates,
  ubiquitous language). `.arc42` describes *how the system is built and runs*
  (containers, deployment, quality attributes, decisions).
- `.backlog` tracks *what work is planned or in progress*.
- `.design` describes *how the product looks and behaves for the user* (UX
  principles, design tokens, interaction and accessibility rules). Channel and
  stack facts stay in `.arc42`; `.design` links to them.
- Architecture Decision Records referenced from arc42 sections should stay
  aligned with ADRs already tracked via `jsdotnet-project-guidelines`; do not
  duplicate ADR content here — link to it instead.
- Local ADRs and TDRs (decisions/debt specific to this system, not covered by
  an org-level ADR) live under `.arc42/adr/` and `.arc42/tdr/` respectively,
  and are linked from `09-architecture-decisions.md` /
  `11-risks-and-technical-debt.md` rather than restated there.

## Structure

Use the standard arc42 chapter set as individual files (create files only
when a chapter has real content — do not scaffold empty placeholders):

```
.arc42/
  01-introduction-and-goals.md
  02-constraints.md
  03-context-and-scope.md
  04-solution-strategy.md
  05-building-block-view.md
  06-runtime-view.md
  07-deployment-view.md
  08-crosscutting-concepts.md
  09-architecture-decisions.md   (links out to ADRs, doesn't restate them)
  10-quality-requirements.md
  11-risks-and-technical-debt.md (links out to TDRs)
  12-glossary.md
  adr/                           (Architecture Decision Records)
  tdr/                           (Technical Debt Records)
```

## Folder rules

These rules describe the persisted shape of `.arc42` assets only. Authoring
workflow, routing, and cross-document governance are handled by separate
instructions.

- Keep the glossary aligned with the ubiquitous language defined per bounded
  context in `.domain`.
- Prefer diagrams (Mermaid) over long prose for building-block and runtime
  views.
- Each file's top-level chapter, and any independently trackable ## section
  inside it, must carry the metadata block described in
  `.github/instructions/chapter-metadata.instructions.md` (status,
  cross-folder tags, GitHub issue link) — required for the planned
  visualization tooling. There is no `depends-on` field in `.arc42` —
  architecture chapters describe standing structure, not sequenced work;
  cross-references use `related` instead.
- Because an `.arc42` file is always exactly one top-level chapter, that
  chapter's metadata block also serves as the file's file-level metadata
  block described in `.github/instructions/chapter-metadata.instructions.md`
  — do not add a second, duplicate block for the file.
- The metadata block's `status` field uses `draft`, `proposed`, `active`, or
  `deprecated` in this folder. Architecture documentation describes a
  standing decision/structure, not a task, so there is no `done`.

## Template

```markdown
# <NN>. <Chapter Name>

\`\`\`meta
status: draft
related: []
issue: null
\`\`\`

Chapter content.

## <Section Name>

\`\`\`meta
status: draft
related: []
issue: null
\`\`\`

Section content.
```
