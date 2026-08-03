---
applyTo: ".arc42/**"
description: Structure and authoring rules for the arc42 architecture documentation folder.
---

# Architecture documentation (`.arc42`)

`.arc42` holds arc42-structured architecture documentation for the system:
context, building blocks, runtime views, cross-cutting concerns, and
architecture decisions, at the level of the whole system or a major
deployable unit.

## Relationship to other knowledge folders

- `.domain` describes *what the domain is* (bounded contexts, aggregates,
  ubiquitous language). `.arc42` describes *how the system is built and runs*
  (containers, deployment, quality attributes, decisions).
- `.backlog` tracks *what work is planned or in progress*.
- Architecture Decision Records referenced from arc42 sections should stay
  aligned with ADRs already tracked via `jsdotnet-project-guidelines`; do not
  duplicate ADR content here — link to it instead.

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
```

## Authoring guidance

- For new or updated arc42 content, use the `orch-arc42` orchestration skill
  (fallback: `architecture:architect`) per
  `.github/instructions/workflow-routing.instructions.md`.
- Ground every section in existing ADRs, recommendations, and structures from
  `jsdotnet-project-guidelines` before writing; do not invent architecture
  decisions that conflict with recorded guidance.
- Keep the glossary aligned with the ubiquitous language defined per bounded
  context in `.domain`.
- Prefer diagrams (Mermaid) over long prose for building-block and runtime
  views.
- Each file's top-level chapter, and any independently trackable ## section
  inside it, must carry the metadata block described in
  `.github/instructions/chapter-metadata.instructions.md` (status,
  dependencies, cross-folder tags, GitHub issue link) — required for the
  planned visualization tooling.

## Template

```markdown
# <NN>. <Chapter Name>

\`\`\`meta
id: arc42:<nn>-<chapter-slug>#<chapter-slug>
status: draft
depends-on: []
related: []
issue: null
\`\`\`

Chapter content.

## <Section Name>

\`\`\`meta
id: arc42:<nn>-<chapter-slug>#<section-slug>
status: draft
depends-on: []
related: []
issue: null
\`\`\`

Section content.
```
