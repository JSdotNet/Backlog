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
- `.domain/<context>/naming.md` — each `Term` chapter.
- `.arc42/<nn>-<name>.md` — the file's top-level chapter, and any ## section
  inside it that is independently trackable.
- `.backlog/<concern-type>-<concern-slug>.md` — each Item and Sub-item.

- `.domain` `model.md`, `flow.md`, and `dependencies.md` are structural/diagram
  files; their `##` sections do **not** carry per-chapter metadata blocks.

## Metadata block format

Place the block immediately after the heading, before any prose:

```markdown
## <Chapter Heading>

\`\`\`meta
status: active
\`\`\`

Prose for this chapter starts here.
```

Only `status` is required, so a chapter with no relations and no issue carries
just that one field. Optional fields (`related`, `issue`, and folder-specific
fields such as `depends-on`) are included only when they have a value; empty
collections and null values are omitted rather than written out.

Some folders define additional relation fields beyond `related` (e.g.
`depends-on`, `implements`) — see that folder's own instructions file for
which extra fields apply and what they mean. Most such fields use the same
reference format described below, but not every folder-specific field is a
reference field: in `.domain`, `aliases` (defined in
`.github/instructions/domain-knowledge.instructions.md`) is a list of
plain-string surface names, not `<path>#<heading-slug>` references.

### Chapter references

Chapters are not given a separate stored id. A chapter is addressed by its
file path (relative to the repository root) plus a GitHub-style anchor slug
of its heading text: `<path>#<heading-slug>`, e.g.
`.domain/order-management/domain.md#aggregate-order`. This is exactly what
renders as the heading's link target, so it stays correct automatically when
read in any Markdown viewer and never needs to be kept in sync by hand.

Use this `<path>#<heading-slug>` form as the entries in `related` and in any
folder-specific relation field (`depends-on`, `implements`, etc.).

### Fields

- **status** (required) — lifecycle state of this chapter's content. The
  allowed values are folder-specific; see the `status` section in
  `.github/instructions/domain-knowledge.instructions.md`,
  `.github/instructions/arc42-knowledge.instructions.md`, or
  `.github/instructions/backlog-knowledge.instructions.md` for the value set
  that applies to the folder you're editing.
- **related** (optional) — list of `<path>#<heading-slug>`
  references this chapter points to for context, without a hard dependency
  (e.g. a backlog item linking to the domain aggregate it changes, or an
  arc42 section linking to a domain feature it realizes). This is the
  general-purpose cross-folder tag mechanism, available in every folder. Omit
  the field entirely when there are no references.
- **issue** (optional) — URL (or `owner/repo#number`
  shorthand) of the GitHub issue tracking this chapter, if one exists. Keep
  this in sync when using `create-github-issue` / `update-github-issue`. Omit
  the field entirely when no issue exists.

Folder-specific relation fields (e.g. `depends-on` on features/backlog
chapters, `implements` on backlog chapters) are documented in that folder's
own instructions file, not here — this file only defines the fields common
to every folder.

## Authoring guidance

- If a chapter heading is renamed, update every relation field entry
  elsewhere (`related` or any folder-specific field) that references its
  old `<path>#<heading-slug>` in the same change.
- Do not invent additional top-level fields without updating either this
  file (for a universal field) or the relevant folder's instructions file
  (for a folder-specific field) first — the visualization tool depends on a
  fixed schema.
- Optional fields are included only when they carry a value. Empty list-valued
  fields (`related: []`, `depends-on: []`) and null values (`issue: null`) are
  omitted rather than written out, so a chapter with no relations and no issue
  shows only `status`.
