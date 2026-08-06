---
applyTo: ".domain/**,.arc42/**,.backlog/**,.tech/**"
description: Common per-chapter and per-file metadata convention for .domain, .arc42, .backlog, and .tech, so a future visualization tool can parse status, dependencies, and cross-references.
---

# Chapter and file metadata

`.domain`, `.arc42`, `.backlog`, and `.tech` are intended to be read by a
visualization tool (to be built later), not just by humans. To make that
possible, every **chapter** in these folders carries a small, parseable
metadata block directly under its heading, in a fenced `meta` (YAML) code
block, and every **file** carries an equivalent block directly under its
top-level (`#`) heading describing the document as a whole.

A "chapter" here means any heading that these folders' own instructions
already treat as an addressable unit:

- `.domain/<context>/domain.md` — each Aggregate, Domain Service, Domain Event,
  and each Shared Value Objects / Shared Enums chapter. Entity/Value
  Object/Enum sub-chapters inside an Aggregate use the metadata block too if
  they need independent status/dependencies/cross-references; otherwise they
  can be covered by their parent Aggregate's block.
- `.domain/<context>/features.md` — each Feature and Sub-feature.
- `.domain/<context>/naming.md` — each `Term` chapter.
- `.arc42/<nn>-<name>.md` — the file's top-level chapter, and any ## section
  inside it that is independently trackable.
- `.backlog/<concern-type>-<concern-slug>.md` — each Item and Sub-item.
- `.tech/<layer>.md` — each `## <Technology Name>` chapter (one graph node per
  chapter).

- `.domain` `context-map.md`, `model.md`, `flow.md`, and `dependencies.md` and
  `.tech` `technology-graph.md` are strategic/structural artifacts; their `##`
  sections do **not** carry per-chapter metadata blocks.

## Chapter metadata block format

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

## File-level metadata block

In addition to per-chapter blocks, every file in `.domain`, `.arc42`,
`.backlog`, and `.tech` carries one file-level metadata block describing the document as
a whole. This gives the visualization tool a status/relations rollup for the
file itself, distinct from the status of any individual chapter inside it —
useful for files such as `.domain` `context-map.md`, `model.md`, `flow.md`,
and `dependencies.md`, whose `##` sections don't carry their own per-chapter
blocks.

Place the block immediately after the file's top-level (`#`) heading, before
any blockquote summary, prose, or first chapter:

```markdown
# <File Title>

\`\`\`meta
status: active
\`\`\`

> Optional blockquote summary, if the file has one.

Prose or the first chapter starts here.
```

The file-level block uses the same fields as a chapter block (`status`
required; `related` and `issue` optional) and the same omit-when-empty rule.
Folder-specific fields defined for chapters (`depends-on`, `implements`,
`aliases`, `kind`, `version`, `alternatives`) are chapter-scoped and are not used at file level —
a file's overall relationships are expressed through `related` only.

In `.arc42`, the file's top-level chapter heading (e.g. `# 01. Introduction
and Goals`) already carries a chapter metadata block as described above; for
these files that same block also serves as the file-level block, since an
`.arc42` file is always exactly one top-level chapter — no separate,
duplicate block is added.

Some folders define additional relation fields beyond `related` (e.g.
`depends-on`, `implements`) — see that folder's own instructions file for
which extra fields apply and what they mean. Most such fields use the same
reference format described below, but not every folder-specific field is a
reference field: in `.domain`, `aliases` (defined in
`.github/instructions/domain-knowledge.instructions.md`) is a list of
plain-string surface names, not `<path>#<heading-slug>` references, and in
`.tech`, `alternatives` (defined in
`.github/instructions/tech-knowledge.instructions.md`) is likewise a
plain-string list.

### Chapter and file references

Chapters are not given a separate stored id. A chapter is addressed by its
file path (relative to the repository root) plus a GitHub-style anchor slug
of its heading text: `<path>#<heading-slug>`, e.g.
`.domain/order-management/domain.md#aggregate-order`. This is exactly what
renders as the heading's link target, so it stays correct automatically when
read in any Markdown viewer and never needs to be kept in sync by hand.

A file, addressed at the file-level metadata block, is referenced the same
way but without a heading slug: `<path>`, e.g.
`.domain/order-management/dependencies.md`. Use this bare-path form when a
`related` entry points at a file as a whole rather than one of its chapters.

Use the `<path>#<heading-slug>` (chapter) or `<path>` (file) form as the
entries in `related` and in any folder-specific relation field (`depends-on`,
`implements`, etc.).

### Fields

- **status** (required) — lifecycle state of this chapter's or file's
  content. The allowed values are folder-specific; see the `status` section
  in `.github/instructions/domain-knowledge.instructions.md`,
  `.github/instructions/arc42-knowledge.instructions.md`,
  `.github/instructions/backlog-knowledge.instructions.md`, or
  `.github/instructions/tech-knowledge.instructions.md` for the value set
  that applies to the folder you're editing. A file-level `status` reflects
  the document as a whole and is set independently of its chapters' own
  `status` values (e.g. a file can be `active` overall while one chapter
  inside it is still `draft`).
- **related** (optional) — list of `<path>#<heading-slug>` or `<path>`
  references this chapter or file points to for context, without a hard
  dependency (e.g. a backlog item linking to the domain aggregate it
  changes, or an arc42 section linking to a domain feature it realizes).
  This is the general-purpose cross-folder tag mechanism, available in every
  folder. Omit the field entirely when there are no references.
- **issue** (optional) — URL (or `owner/repo#number`
  shorthand) of the GitHub issue tracking this chapter or file, if one
  exists. Keep this in sync when using `create-github-issue` /
  `update-github-issue`. Omit the field entirely when no issue exists.

Folder-specific fields (e.g. `depends-on` on features/backlog/tech chapters,
`implements` on backlog chapters, `kind`/`version`/`alternatives` on tech
chapters) are documented in that folder's
own instructions file, not here — this file only defines the fields common
to every folder.

## Authoring guidance

- If a chapter heading is renamed, update every relation field entry
  elsewhere (`related` or any folder-specific field) that references its
  old `<path>#<heading-slug>` in the same change.
- If a file is renamed or moved, update every relation field entry elsewhere
  that references its old bare `<path>` in the same change.
- Every new or edited file must have its file-level metadata block; every new
  or edited chapter must have its chapter metadata block. Do not add one
  without the other when creating a new file.
- Do not invent additional top-level fields without updating either this
  file (for a universal field) or the relevant folder's instructions file
  (for a folder-specific field) first — the visualization tool depends on a
  fixed schema.
- Optional fields are included only when they carry a value. Empty list-valued
  fields (`related: []`, `depends-on: []`) and null values (`issue: null`) are
  omitted rather than written out, so a chapter or file with no relations and
  no issue shows only `status`.
