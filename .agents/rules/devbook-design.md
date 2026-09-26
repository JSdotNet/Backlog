---
name: devbook-design
description: Structure and authoring rules for the design devbook folder, holding UX principles, design tokens, interaction guidelines, accessibility rules, and component-library decisions.
---

# Design documentation (`design/`)

`design/` holds the product's design and UX guidelines: principles, design
tokens, typography and layout rules, interaction guidelines, accessibility
requirements, and the component-library recommendation per channel.

It is guideline-level only. Concrete artifacts produced *from* these
guidelines — wireframes, user flows, prototypes, screenshots — are not stored
here.

## Authoritative source

When the repository has an authoritative design source (a design-system MCP
server, a published style guide, or a design-tokens package), that source wins
and `design/` **materializes** it into the repository so the product has a
stable, reviewable, offline copy:

- Token names and values are written out concretely, not linked to only.
- When a chapter restates upstream guidance, keep it short and prescriptive; do
  not copy long-form documentation into the folder.
- If the authoritative source is unavailable, state that it could not be
  verified, mark the affected chapter `status: draft`, and note the gap in the
  chapter itself.

When no upstream source exists, `design/` is itself authoritative and its
chapters carry the rationale directly.

## Context-loading policy

- `design/` is **not** baseline repository context. Load it only for design,
  UX, or UI-implementation tasks, normally after routing through the
  repository's design flow or a UX specialist agent.
- When `design/` is needed as task context, load only the relevant file(s)
  instead of reading the whole folder.
- UI implementation work (feature or bug) consults `design/` when the change
  touches visual design, interaction behavior, editing behavior, or
  accessibility — not by default.

## Relationship to other devbook folders

- `arc42/` describes *how the system is built and runs*; `design/` describes
  *how it looks and behaves for the user*. Channel and stack facts live in
  `arc42/` and `tech/` — `design/` links to them rather than restating them.
- `domain/` describes *what the domain is*. `design/` does not define domain
  concepts; it uses the ubiquitous language from the context's `term` chapters,
  in `.devbook/domain/<context>/domain.md` or in its `domain.md`.
- Work items link to the
  `design/` chapter they realize via `related`.

## Structure

Create files only when a topic has real content — do not scaffold empty
placeholders.

```
.devbook/design/
  README.md                  (index + headline principles)
  design-principles.md
  color-scheme.md            (palette + semantic tokens)
  typography-and-layout.md
  interaction-guidelines.md  (feedback, motion, input affordances)
  accessibility.md
  component-libraries.md     (per-channel recommendation + rationale)
```

Add a file only when a topic genuinely does not belong to an existing one, and
register it in `README.md`'s index in the same change. Reading order needs no
declaration: `README.md` is `design/`'s root document and is read first, then the
files in the order shown in the tree above, with anything extra sorted by
filename after them. See `devbook-chapter-metadata.md`.

## Folder rules

- **Record the product's own standing design constraints as rules here.** A
  constraint that holds for the whole product (for example a single fixed theme,
  an always-on autosave model, or a canonical content format) belongs in the
  relevant chapter as a prescriptive rule, so no guideline, mock, or example can
  quietly contradict it. Ground such a constraint in `arc42/` when it also has
  an architectural cause, and link rather than restate.
- Rules must be prescriptive and testable. Prefer tables, token names, and
  explicit thresholds over prose.
- Design tokens are declared once in `color-scheme.md` and
  `typography-and-layout.md`; other files reference token names instead of
  repeating raw values.
- **Every pointer-only interaction has a keyboard equivalent.** Drag-and-drop,
  hover-revealed affordances, and gesture shortcuts must be fully operable
  without a pointer.
- Cross-channel guidance states the shared rule first, then per-stack mapping —
  it does not fork into unrelated per-stack designs.
- `component-libraries.md` records a recommendation with rationale, a
  comparison table, and known gaps. It does not add or pin dependencies;
  dependency changes go through the repository's package-update workflow, and
  the adopted result is recorded in `tech/`.
- Keep all `design/` content in English.

## Metadata

Every `design/` file and every `##` chapter carries a metadata block per
`devbook-chapter-metadata.md`.

Allowed `status` values in `design/`:

| Status | Meaning |
|---|---|
| `draft` | Written but not yet agreed, or not yet grounded in the authoritative design source. |
| `active` | Agreed and binding for implementation. **Resting value — omit the field.** |
| `deprecated` | Superseded; kept for history, must not be followed. |

There is no `approved` or `accepted` rung here. The two decision rungs are
`domain/`'s alone — see `devbook-domain.md`.

`status` is therefore **optional** here. State it only while a chapter is
`draft` or `deprecated`; an agreed, binding guideline says so by leaving the
field out. A design system is almost entirely agreed and binding — that is what
makes it a system — so writing `active` on every chapter marks nothing, and the
one `draft` colour token stops standing out. Writing `status: active` explicitly
is reported.

`design/` defines no `type` field either, so a settled chapter's block ends up
empty. **Keep the empty `meta` fence** — it is what makes the heading an
addressable chapter, and deleting it drops the chapter out of the derived graph
and out of every reference pointing at it.

`design/` defines no folder-specific relation fields — use `related` (and
`issue` when tracked) only.
