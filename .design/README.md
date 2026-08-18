# Design Knowledge (`.design`)

```meta
status: active
order: ["design-principles.md", "color-scheme.md", "typography-and-layout.md", "interaction-guidelines.md", "content-editing.md", "accessibility.md", "component-libraries.md"]
related: [".arc42/02-constraints.md#technical-constraints", ".arc42/04-solution-strategy.md#technology-choices", ".arc42/08-crosscutting-concepts.md#storage-and-sync"]
```

> `.design` is the durable, checked-in record of the UX and visual design
> guidelines for the Backlog product. It is the authoritative local guide for
> *how the product should look and behave* across every channel (desktop,
> mobile, IDE extensions), complementary to `.arc42` (architecture) and
> `.domain` (domain model). Design tokens and UX guidance are grounded in the
> organization's `jsdotnet-project-design` MCP style guide; this folder
> materializes the relevant, product-specific rules so they are reviewable and
> version-controlled in the repository.

This folder holds **guidelines only** — no wireframes, no user flows, no
implementation code.

## Purpose

```meta
status: active
```

The Backlog product spans several UI channels that share one canonical data
format (Markdown) and one design language. These files establish the binding,
testable rules that keep those channels consistent:

- one shared set of **design tokens** (materialized in `color-scheme.md`),
- consistent **typography, spacing, and layout**,
- consistent **interaction behavior** (auto-save, drag-and-drop reorder,
  keyboard alternatives),
- consistent **content-editing** behavior (direct Markdown WYSIWYG editing),
- a common **accessibility** target (WCAG 2.1 AA),
- a per-channel **component-library** recommendation.

## Headline Principles

```meta
status: active
```

Two product-level decisions override defaults everywhere and must be honored by
every channel and component:

| # | Principle | What it means | Detailed in |
|---|---|---|---|
| 1 | **Dark mode only** | There is no light theme and no theme toggle. All palette, contrast, and elevation rules are authored for dark surfaces only. | `color-scheme.md`, `design-principles.md#dark-mode-only` |
| 2 | **No save buttons** | Every edit auto-saves. There is no "Save" affordance anywhere in the product. State is communicated through a save-state indicator, not a button. | `interaction-guidelines.md#auto-save-no-save-buttons` |

## How to Use This Folder

```meta
status: active
```

- Treat every rule marked with **MUST** / **MUST NOT** as testable acceptance
  criteria for design review.
- When building a screen or component, read `color-scheme.md`,
  `typography-and-layout.md`, and `interaction-guidelines.md` first; they cover
  the majority of decisions.
- Token names are canonical. Reference tokens by name (e.g. `color-primary`),
  never by raw hex value, in product code. `color-scheme.md` and
  `typography-and-layout.md` are the only files that declare token values;
  every other file references token names.
- Any decision that could not be sourced from the design MCP is marked
  `[TODO: clarify]` and must be resolved before that area ships.

## Living Reference: The UI Storybook

```meta
status: active
related: [".design/color-scheme.md", ".design/typography-and-layout.md", ".design/component-libraries.md"]
```

These files are the written rules; the **storybook** is where they are visible
and runnable. It renders every component of the shared Razor library on its own,
against the same stylesheet the desktop app links, so it is the review surface
for anything specified here.

| What | Where |
|---|---|
| Storybook host | `src/Harness/Backlog.UI.Storybook` |
| Component library it renders | `src/Core/Backlog.UI.Components` |
| Token declarations — the code side of `color-scheme.md` and `typography-and-layout.md` | `src/Core/Backlog.UI.Components/wwwroot/components.css` (`:root`) |

Run it standalone, or as the `ui-storybook` Aspire resource:

```bash
dotnet run --project src/Harness/Backlog.UI.Storybook
```

Page map — each file here and the storybook pages that show it:

| File | Storybook pages |
|---|---|
| `design-principles.md` | *Foundations* (one dark palette, no light column); *Markdown* (edits auto-save, no save button) |
| `color-scheme.md` | *Foundations* → **Colour**, which measures each token's contrast in the live document against the thresholds in `#contrast-rules-wcag-aa-minimum` |
| `typography-and-layout.md` | *Foundations* → **Typography**, **Spacing**, **Radius and elevation**, **Motion** |
| `interaction-guidelines.md` | *Feedback* (SaveIndicator, Toast, Alert, EmptyState, Spinner); *Overlays*; *Markdown* (debounced text save, immediate task-toggle save) |
| `content-editing.md` | *Markdown*, *File view*, *Code* |
| `accessibility.md` | every page — components carry their own roles, labels and focus styles; *Foundations* reports contrast |
| `component-libraries.md` | *Diagrams* and *Graph explorer* (Mermaid, AntV G6), plus the library itself |

Rules:

- The storybook MUST NOT restyle a library component; it adds page chrome only,
  so what it shows is what the app shows.
- `components.css` is the single declaration of tokens in code — the desktop's
  `app.css` links it and declares only app-specific values on top. A token value
  changed in one place MUST be changed in the other and here.
- Where a rule in these files is **not yet materialized** in the library, the
  file says so in a *Materialization* chapter rather than letting the two
  disagree silently.

## Table of Contents

```meta
status: active
```

| File | Scope |
|---|---|
| [`design-principles.md`](design-principles.md) | Product-level UX principles: local-first/offline UX, keyboard-first, low-chrome, AI-first surfaces, dark-mode-only. |
| [`color-scheme.md`](color-scheme.md) | MCP-sourced dark palette, semantic token table, contrast rules, elevation-by-color, per-stack token mapping. |
| [`typography-and-layout.md`](typography-and-layout.md) | Type scale, font choices, spacing scale, density, grid/layout rules, iconography. |
| [`interaction-guidelines.md`](interaction-guidelines.md) | Auto-save, drag-and-drop reordering of items and chapters, keyboard alternatives, feedback, motion, empty/loading/error states. |
| [`content-editing.md`](content-editing.md) | Direct Markdown editing via a rich text (WYSIWYG) editor where Markdown is canonical. |
| [`accessibility.md`](accessibility.md) | WCAG AA target, keyboard nav, screen-reader announcements, focus visibility, reduced motion, target sizes. |
| [`component-libraries.md`](component-libraries.md) | Per-channel component-library research and recommendation. |

## Provenance

```meta
status: active
```

The visual design tokens and UX patterns in this folder are sourced from the
organization's `jsdotnet-project-design` MCP server (server identity
`JSdotNet.MCP.Design`). The specific guides consumed are listed per file. The
org palette **includes a defined dark mode**, so the "dark mode only"
requirement is satisfied by selecting the dark column of the org tokens — no
draft palette was invented.

## Status Vocabulary

```meta
status: active
```

Files and chapters in `.design` use `status: draft | active | deprecated`
(`draft` = proposed/unverified, `active` = current binding guidance,
`deprecated` = superseded). Metadata blocks follow
`knowledge-chapter-metadata.instructions.md` and
`knowledge-design.instructions.md` from the `knowledge-base` plugin: only
`status` is required; `related` and `issue` are optional and omitted when empty.
