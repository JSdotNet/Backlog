---
applyTo: ".design/**"
description: Structure and authoring rules for the design knowledge folder, holding UX principles, design tokens, interaction guidelines, accessibility rules, and component-library decisions.
---

# Design knowledge (`.design`)

`.design` holds the product's design and UX guidelines: principles, the
dark-mode design tokens, typography and layout rules, interaction guidelines,
content-editing rules, accessibility requirements, and the component-library
recommendation per channel.

It is guideline-level only. Concrete artifacts produced *from* these
guidelines — wireframes, user flows, prototypes, screenshots — are not stored
here.

## Authoritative source

`jsdotnet-project-design` (the design MCP server) is the authoritative source
for design and UX guidance, and specifically for the color scheme / design
tokens. `.design` **materializes** that guidance into the repository so the
product has a stable, reviewable, offline copy:

- Token names and values are written out concretely, not linked to only.
- When a chapter restates MCP guidance, keep it short and prescriptive; do not
  copy long-form MCP documentation into the folder.
- If the MCP server is unavailable, state that authoritative guidance could not
  be verified, mark the affected chapter `status: draft`, and note the gap in
  the chapter itself.

## Context-loading policy

- `.design` is **not** baseline repository context. Load it only for design,
  UX, or UI-implementation tasks, normally after routing through
  `orch-design-knowledge` or `ux-design:ux-designer`.
- When `.design` is needed as task context, load only the relevant file(s)
  instead of reading the whole folder.
- UI implementation work (feature or bug) consults `.design` when the change
  touches visual design, interaction behavior, editing behavior, or
  accessibility — not by default.

## Relationship to other knowledge folders

- `.arc42` describes *how the system is built and runs*; `.design` describes
  *how it looks and behaves for the user*. Channel/stack facts (WinUI 3, MAUI,
  VS Code webview, cloud) live in `.arc42` — `.design` links to them rather
  than restating them.
- `.domain` describes *what the domain is*. `.design` does not define domain
  concepts; it uses the ubiquitous language from `.domain/<context>/naming.md`.
- `.backlog` tracks *what work is planned*. Backlog items link to the
  `.design` chapter they realize via `related`.

## Structure

Create files only when a topic has real content — do not scaffold empty
placeholders.

```
.design/
  README.md                  (index + headline principles)
  design-principles.md
  color-scheme.md            (MCP-sourced dark palette + semantic tokens)
  typography-and-layout.md
  interaction-guidelines.md  (auto-save, drag-and-drop reordering, feedback, motion)
  content-editing.md         (direct Markdown editing via a rich text editor)
  accessibility.md
  component-libraries.md     (per-channel recommendation + rationale)
```

## Folder rules

- **Dark mode only.** The product ships a single dark theme. Never author a
  light palette, a theme toggle, or "in light mode…" guidance. Contrast and
  elevation rules are written for dark surfaces.
- **No save buttons.** Everything auto-saves. Never introduce an explicit
  save/commit affordance in a guideline, mock, or example.
- **Markdown is canonical.** Editing guidance must preserve the constraint in
  `.arc42/02-constraints.md#technical-constraints`: the rich text editor is a
  presentation layer over Markdown, and unsupported syntax is preserved, never
  destroyed.
- **Every drag-and-drop rule has a keyboard equivalent.** Reordering files and
  chapters must be fully operable without a pointer.
- Rules must be prescriptive and testable. Prefer tables, token names, and
  explicit thresholds over prose.
- Design tokens are declared once in `color-scheme.md` and
  `typography-and-layout.md`; other files reference token names instead of
  repeating raw values.
- Cross-channel guidance states the shared rule first, then per-stack mapping
  (WinUI `ResourceDictionary`, MAUI resources, CSS custom properties) — it does
  not fork into unrelated per-stack designs.
- `component-libraries.md` records a recommendation with rationale, a
  comparison table, and known gaps. It does not add or pin dependencies;
  dependency changes go through `orch-update-packages`.
- Keep all `.design` content in English.

## Metadata

Every `.design` file and every `##` chapter carries a metadata block per
`.github/instructions/chapter-metadata.instructions.md`.

Allowed `status` values in `.design`:

| Status | Meaning |
|---|---|
| `draft` | Written but not yet agreed, or not yet grounded in the design MCP. |
| `active` | Agreed and binding for implementation. |
| `deprecated` | Superseded; kept for history, must not be followed. |

`.design` defines no folder-specific relation fields — use `related` (and
`issue` when tracked) only.
