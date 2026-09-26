---
name: devbook-arc42
description: Structure and authoring rules for the arc42 architecture documentation folder.
---

# Architecture documentation (`arc42/`)

`arc42/` holds arc42-structured architecture documentation for the system:
context, building blocks, runtime views, cross-cutting concerns, and
architecture decisions, at the level of the whole system or a major
deployable unit.

## Context-loading policy

- `arc42/` is **not** baseline repository context. Load it only for architecture,
  ADR, blueprint, TDR, or explicit arc42 tasks, normally after routing through the
  repository's architecture flow or an architecture specialist agent.
- When `arc42/` is needed as task context, load only the relevant chapter(s) or
  sections instead of reading the whole folder by default.
- For non-architecture implementation or documentation tasks, consult `arc42/`
  only when the user asks for architecture context or when the work depends on a
  specific documented constraint, decision, runtime view, deployment view, or
  glossary entry.

## Relationship to other devbook folders

- `domain/` describes *what the domain is* (bounded contexts, aggregates,
  ubiquitous language). `arc42/` describes *how the system is built and runs*
  (containers, deployment, quality attributes, decisions).
- `design/` describes *how the product looks and behaves for the user* (UX
  principles, design tokens, interaction and accessibility rules). Channel and
  stack facts stay in `arc42/`; `design/` links to them.
- Decision records and debt records live under `arc42/adr/` and `arc42/tdr/`
  and are linked from `09-architecture-decisions.md` and
  `11-risks-and-technical-debt.md`, never restated there. A decision an
  organization already records elsewhere is linked, not copied.

## Structure

Use the standard arc42 chapter set as individual files (create files only
when a chapter has real content — do not scaffold empty placeholders):

```
.devbook/arc42/
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
  adr/                           (decision records — one per technical concern)
  tdr/                           (technical debt records — one per item)
  building-blocks/               (one whitebox per building block that outgrows chapter 5)
```

## Folder rules

These rules describe the persisted shape of `arc42/` assets only. Authoring
workflow, routing, and cross-document governance are handled by separate
instructions.

- Keep the glossary aligned with the ubiquitous language defined per bounded
  context in `domain/`.
- Prefer diagrams (Mermaid) over long prose for building-block and runtime
  views.
- Each file's top-level chapter, and any independently trackable ## section
  inside it, must carry the metadata block described in
  `devbook-chapter-metadata.md` (status — optional here, see
  below — cross-folder tags, issue link) — required for the derived index and
  graph tooling. There is no `depends-on` field in `arc42/` —
  architecture chapters describe standing structure, not sequenced work;
  cross-references use `related` instead.
- Because an `arc42/` file is always exactly one top-level chapter, that
  chapter's metadata block also serves as the file's file-level metadata
  block described in `devbook-chapter-metadata.md`
  — do not add a second, duplicate block for the file.
- The metadata block's `status` field uses `draft`, `proposed`, `active`, or
  `deprecated` in this folder. Architecture documentation describes a
  standing decision/structure, not a task, so there is no `done`.
- **There is no `approved` or `accepted` rung here.** The two decision rungs
  are `domain/`'s alone — see `devbook-domain.md`. An architecture chapter
  records a standing structure, and the question those rungs answer is asked of
  the model, not of it.
- **`active` is this folder's resting value, so it is written by omitting the
  field.** State `status` only while the chapter is in transition (`draft`,
  `proposed`) or carries a standing warning (`deprecated`). A standing structure
  is settled by definition, so `active` is where nearly every chapter here ends
  up permanently, and stating it turns the field into noise the few moving
  chapters hide behind. Writing `status: active` explicitly is reported.
- **Chapters are ordered by their number, not by their filename string.** The
  `<NN>-` prefix supplies it, so `10-quality-requirements.md` sorts after
  `09-architecture-decisions.md` rather than after `01-…`. Only add an explicit
  `number` field when a file's name cannot carry the number.
- **Debt records are numbered with a date.** Number them in the filename
  (`7-no-retry-budget.md`) or, where the filename is a plain slug, in a
  `number` field — either way the number is what orders the folder, so an
  unpadded 10 still follows 7. Give each one a `date` for the day the debt was
  logged; it is content, not a modification timestamp. Decision records are not
  numbered; the next section says why.
- **Give `adr/`, `tdr/`, and `building-blocks/` an index document.** None of
  the three has a root document by convention, so mark the one that introduces
  the set — usually `README.md` — with `index: root` and it sorts first. Without
  it the folder is a bare list.

## Decision records (`adr/`)

A decision record holds a choice whose reversal would cost a **migration** — of
code, of data, or of the model's language: a storage engine, an API style, a
messaging or integration protocol, a hosting or deployment model, a runtime or
framework, how packages are managed and built, and equally an aggregate
boundary, a snapshot-versus-reference choice, a consistency boundary, or a
contested term the model now depends on. The test is what reversing it would
cost. A choice a reader could reverse with a find-and-replace is not a record.
Naming, folder layout, process rules, who owns what, and how a document is
written are not architecture: the rule, chapter, or bounded context that states
them carries the reason in a sentence, and no record is opened.

**One record per concern, not per decision.** The file is the concern —
`adr/storage.md`, `adr/api.md`, `adr/package-management.md` — a kebab-case slug
with no number, and it always describes the standing choice. A new decision on
a concern that already has a record changes that record: the choice is
rewritten, the reasons and rejected alternatives are brought in line, and a row
is added to its history. Open a new file only for a concern no record covers.
Two concerns that are always decided together are one record; a record that
keeps growing a second subject is two. A record for a concern the system no
longer has is marked `status: deprecated` and says what happened; it is never
deleted, because the history is the part a reader most needs.

Each record, in this order:

- The standing choice, in one or two sentences under the title, before any
  section.
- `## Why` — the reasons that carry it, as they stand today.
- `## Rejected` — each alternative considered and the reason it lost. Someone
  arriving to propose one of them should find it here.
- `## History` — a table, newest first, one row per decision: the date, what
  changed, and why in a clause. The bottom row is the concern's first choice.
  The `date` field carries the newest row's date; it is content, not a
  modification timestamp.

A proposed change to a concern lives in that record under `status: proposed`,
in a `## Proposed` section stating the candidate choice against the standing
one. Decided, it folds into the choice and a history row and the section goes;
declined, it becomes a history row saying so. A proposal never sits as a loose
document beside the set.

The `adr/` index lists every concern with its standing choice in one line, and
`09-architecture-decisions.md` links to the index and restates nothing.

```markdown
# Storage

\`\`\`meta
date: 2026-09-17
\`\`\`

PostgreSQL 16 through EF Core, one database per bounded context.

## Why

\`\`\`meta
\`\`\`

## Rejected

\`\`\`meta
\`\`\`

## History

\`\`\`meta
\`\`\`

| Date | Change |
| --- | --- |
| 2026-09-17 | SQL Server to PostgreSQL: the licence no longer covered the features in use. |
| 2026-03-01 | SQL Server, because the team knew it. |
```

## Building block records (`building-blocks/`)

Chapter 5 holds what the whole system shares: the level 1 landscape, the shapes
every block has in common, and the diagram of the edges between them. A block
whose whitebox — its responsibility, the interfaces it exposes, its internal
structure, and its dependencies — is read on its own more often than beside the
landscape gets a file of its own under `building-blocks/`, for the same reason
a concern gets one under `adr/`: a reader opens the block they work in, and a
sync pass narrows to one file. Chapter 5 then links to the folder's index and
restates nothing about a block that has a file.

One file per block, `building-blocks/<slug>.md`, the slug being the block's
name as the code spells it — a plugin folder, a project, a service. Each file,
in this order:

- The block's responsibility, in prose under the title, before any section:
  what it owns, what is inside it, what is outside it and where that is answered.
- `## Interfaces` — how the block is reached: the skills, agents, commands,
  routes, contracts, or public API it exposes, one `###` per interface where
  each needs its own words.
- `## Structure` — the parts inside it that carry a distinct responsibility,
  with the invariants each one guarantees where the block has any, and a Mermaid
  diagram where the parts relate.
- `## Runtime` — optional: the flows that cross the block, as diagrams. A
  runtime scenario that crosses several blocks belongs in
  `06-runtime-view.md`, which links here.
- `## Dependencies` — what the block depends on and what depends on it, in
  their actual direction, one table each, with the mechanism and the contract.

The rules of the folder apply unchanged: no `type`, no `depends-on`, `related`
for every cross-reference, and the top-level block doubling as the file-level
one. The folder's index (`README.md`, `index: root`) lists every block with
its responsibility in one line.

## Template

```markdown
# <NN>. <Chapter Name>

\`\`\`meta
status: draft
\`\`\`

Chapter content.

## <Section Name>

\`\`\`meta
status: draft
\`\`\`

Section content.
```

`related` and `issue` are omitted above rather than written empty, per the
omit-when-empty rule; add them when they carry a value. Once a chapter settles,
its `status: draft` line comes out too and the fence is left empty — keep the
fence, it is what makes the heading an addressable chapter.
