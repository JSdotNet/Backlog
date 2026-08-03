---
applyTo: ".domain/**,.arc42/**,.backlog/**"
description: Common per-chapter metadata convention for .domain, .arc42, and .backlog, so a future visualization tool can parse status, dependencies, and cross-references.
---

# Chapter metadata

`.domain`, `.arc42`, and `.backlog` are intended to be read by a
visualization tool (to be built later), not just by humans. To make that
possible, every **chapter** in these folders carries a small, parseable
metadata block directly under its heading, in a fenced `meta` (YAML) code
block.

A "chapter" here means any heading that these folders' own instructions
already treat as an addressable unit:

- `.domain/<context>/domain.md` — each Aggregate, Domain Service, and each
  Shared Value Objects / Shared Enums chapter. Entity/Value Object/Enum
  sub-chapters inside an Aggregate use the metadata block too if they need
  independent status/dependencies/cross-references; otherwise they can be
  covered by their parent Aggregate's block.
- `.domain/<context>/features.md` — each Feature and Sub-feature.
- `.arc42/<nn>-<name>.md` — the file's top-level chapter, and any ## section
  inside it that is independently trackable.
- `.backlog/<concern-type>-<concern-slug>.md` — each Item and Sub-item.

## Metadata block format

Place the block immediately after the heading, before any prose:

```markdown
## <Chapter Heading>

\`\`\`meta
status: active
depends-on: []
related: []
issue: null
\`\`\`

Prose for this chapter starts here.
```

### Chapter references

Chapters are not given a separate stored id. A chapter is addressed by its
file path (relative to the repository root) plus a GitHub-style anchor slug
of its heading text: `<path>#<heading-slug>`, e.g.
`.domain/order-management/domain.md#aggregate-order`. This is exactly what
renders as the heading's link target, so it stays correct automatically when
read in any Markdown viewer and never needs to be kept in sync by hand.

Use this `<path>#<heading-slug>` form as the entries in `depends-on` and
`related` below.

### Fields

- **status** (required) — lifecycle state of this chapter's content. The
  allowed values depend on which folder the chapter is in:
  - `.domain` — `draft`, `proposed`, `active`, `deprecated`. Domain
    knowledge describes the current (or agreed-future) model, not a task
    queue, so there is no `done`; `active` means "this is the current
    model", `deprecated` means superseded.
  - `.arc42` — `draft`, `proposed`, `active`, `deprecated`. Same rationale
    as `.domain`: architecture documentation describes a standing
    decision/structure, not a task.
  - `.backlog` — `draft`, `ready`, `in-progress`, `done`, `blocked`. Backlog
    items describe work to be executed, so status tracks task progress
    rather than content lifecycle.
- **depends-on** (optional, default `[]`, `.domain/features.md` and
  `.backlog` chapters only) — list of `<path>#<heading-slug>` references
  that this chapter structurally or sequentially depends on (e.g. a backlog
  item that can't start before another finishes, or a feature that requires
  another feature to be delivered first). Not used in `.arc42` or in
  `domain.md` (Aggregates/Domain Services/Shared Value Objects/Shared Enums)
  — those describe standing structure, and their relationships belong in
  `model.md`/`dependencies.md` or the `related` field, not a dependency
  queue.
- **related** (optional, default `[]`) — list of `<path>#<heading-slug>`
  references this chapter points to for context, without a hard dependency
  (e.g. a backlog item linking to the domain aggregate it changes, or an
  arc42 section linking to a domain feature it realizes). This is the
  general-purpose cross-folder tag mechanism.
- **issue** (optional, default `null`) — URL (or `owner/repo#number`
  shorthand) of the GitHub issue tracking this chapter, if one exists. Keep
  this in sync when using `create-github-issue` / `update-github-issue`.

## Authoring guidance

- If a chapter heading is renamed, update every `depends-on`/`related` entry
  elsewhere that references its old `<path>#<heading-slug>` in the same
  change.
- Do not invent additional top-level fields without updating this file
  first — the visualization tool depends on a fixed schema.
- Empty `depends-on`/`related` lists are written as `[]`, not omitted,
  so the metadata block shape stays uniform across chapters.
- `issue: null` (not omitted) when no issue exists yet, for the same reason.
