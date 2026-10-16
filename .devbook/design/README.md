# Design Knowledge (`.devbook/design`)

```meta
related: [".devbook/arc42/02-constraints.md#technical-constraints", ".devbook/arc42/04-solution-strategy.md#technology-choices", ".devbook/arc42/08-crosscutting-concepts.md#storage-and-sync"]
```

> `.devbook/design` is the durable, checked-in record of the UX and visual design
> guidelines for the Backlog product. It is the authoritative local guide for
> *how the product should look and behave* across every channel: desktop,
> mobile and IDE extensions. It complements `.devbook/arc42` (architecture) and
> `.devbook/domain` (domain model). Design tokens and UX guidance were adapted from the
> JSdotNet design style guide. This folder holds the relevant, product-specific
> rules and **is the authoritative source for them**; nothing is fetched from
> elsewhere at read time.

This folder holds **guidelines only**. Wireframes, user flows and implementation
code live elsewhere.

## Purpose

```meta
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
```

Two product-level decisions override defaults everywhere and must be honored by
every channel and component:

| # | Principle | What it means | Detailed in |
|---|---|---|---|
| 1 | **Dark mode only** | There is no light theme and no theme toggle. All palette, contrast, and elevation rules are authored for dark surfaces only. | `color-scheme.md`, `design-principles.md#dark-mode-only` |
| 2 | **No save buttons** | Every edit auto-saves, and a save-state indicator shows the state. | `design-principles.md#no-save-buttons--auto-save-everywhere`, `interaction-guidelines.md#auto-save-no-save-buttons` |

## How to Use This Folder

```meta
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
- Any decision the style guide did not settle is marked `[TODO: clarify]` and
  must be resolved before that area ships.

## Living Reference: The UI Storybook

```meta
related: [".devbook/design/color-scheme.md", ".devbook/design/typography-and-layout.md", ".devbook/design/component-libraries.md"]
```

These files are the written rules; the **storybook** is where they are visible
and runnable. It renders every component of the shared Razor library on its own,
against the same stylesheet the desktop app links, so it is the review surface
for anything specified here.

The division of labour goes both ways. A storybook page says how a component is
*used*: its parameters, its callbacks, and what the host owns. A **Design rules**
block in its header links the chapters here that govern how it may *look and
behave*. The page does not restate a rule, because a rule written in two places
can disagree with itself. Anything normative belongs here; anything about the API
belongs there.

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
| `design-principles.md` | *Foundations* (one dark palette, no light column); *Entry edit* (edits auto-save, no save button); *Integrations* → **AI in the document** (AI attribution) |
| `color-scheme.md` | *Foundations* → **Colour**, which measures each token's contrast in the live document against the thresholds in `#contrast-rules-wcag-aa-minimum`; `#badge-and-chip-tones` → *Badges*, *Devbook* → **State**, *Task detail* and *Focus*; `#chart-roles` → *Usage metrics*, *Productivity over time*; `#integration-roles` → the *Integrations* pages |
| `typography-and-layout.md` | *Foundations* → **Typography**, **Spacing**, **Radius and elevation**, **Motion**; `#metadata-lines` → *Task list*, *Integrations* → **References** |
| `interaction-guidelines.md` | *Feedback* (SaveIndicator, Toast, Alert, EmptyState, Spinner, Skeleton); *Overlays*; *Capture* (the phone's capture sheet and its Inbox row); *Entry edit* (debounced text save, immediate task-toggle save); `#task-rows` → *Task list*, *Today*, *Task detail*, *Focus*; `#optimistic-ui` → *Today*, *Capture*; `#auto-save-no-save-buttons` → *Notes* (a note that saves a second after typing stops, and its Saved / Waiting to sync line); `#action-density-and-overflow` → *Integrations* → **Density and overflow**; `#focus-and-selection` → *Selection bar*; `#combobox` → *Inputs*; `#timelines-and-roadmaps` → *Roadmap* |
| `content-editing.md` | *Markdown*, *Markdown document*, *Rich text editing*, *File view*, *Code*, *Entry edit*, *Section comparison*; `#ai-proposals-in-a-document` → *Integrations* → **AI in the document** |
| `accessibility.md` | every page — components carry their own roles, labels and focus styles; *Foundations* reports contrast |
| `component-libraries.md` | *Diagrams*, *Diagrams in markdown*, *Graph explorer* and *Graph atlas* (Mermaid, the first-party canvas renderer), plus the library itself |
| `effort-and-mode-chips.md` | `#effort-chips` → *Session list* (the effort ladder, an effort nobody recorded, and the stage strip beside it); `#mode-chips`, `#stage-marks` and `#legend` → *Badges* |
| `settings-pages.md` | *Settings frame* (panel, headline, Saved as you go, the phone-width grouped list), *Settings menu* (groups, badges, matched cards), *Settings card*. Those pages do not yet link back to it, as its *Materialization* chapter says. |

The storybook's own order is a rule of its own: **nothing is shown before its
parts.** Its groups run in this order:

1. Parts: input and action, structure, feedback, content, knowledge metadata.
2. Subjects that are a convention rather than a component: Integrations, Task
   list, Roadmap, Dashboard.
3. *Combined usage*, where whole surfaces are composed out of everything above:
   a document, a file, a folder, an editor, a comparison.

A reviewer reading top to bottom therefore meets every component before the
thing built out of it.

Rules:

- The storybook MUST NOT restyle a library component; it adds page chrome only,
  so what it shows is what the app shows.
- A storybook page MUST NOT state a design or UX rule. It explains how a component
  is used and links the chapter here that governs it. A page whose prose argues
  for a tone, a hierarchy or a contrast ratio keeps a second copy of one of these
  files.
- Every component page MUST carry at least one link back into this folder. A
  rule with no review surface and a component with no governing rule then both
  show as omissions.
- A storybook page MUST show the one subject it is named for. A sample that is
  really about another page's subject belongs on that page, linked rather than
  drawn a second time. A page that demonstrates five other chapters to make its
  own point buries the component a reviewer opened it for, and keeps five copies
  that can drift. Where the reading order forces a borrow, `StorybookIndex`
  records the borrow against that page, so a bent rule reads as a bend rather
  than as the convention.
- A subject that carries a convention of its own MUST get its own page rather
  than a row in a larger group. Examples are how a task row states what it is
  waiting on, and how a bar states which quarter it lands in. A convention read
  as one of nine sibling links is a convention nobody reads.
- Every sample MUST be its own section, with its own name, anchor and test id.
  With fifty-odd samples, "the third one down on the layout page" is no longer a
  way to point at one in review. A page that runs its examples together leaves a
  reviewer nothing to link to and a test nothing to address.
- A sample's explanation is collapsed when it holds more than one paragraph, or
  one paragraph longer than two lines (220 characters at this measure). Below
  that it stays open, because a fold trigger costs more to read than the sentence
  or two it would hide.
- Every sample MUST carry the text behind it in one collapsed disclosure: the
  usage that produced the sample and, when the sample draws content, the content
  it was handed. A call and the text it was given are one thing to read, and two
  triggers would make two clicks out of one question. A sample therefore has at
  most two folds: the explanation's, above it, and this disclosure, below it.
  Both start closed; the sample between them is always shown.
- `components.css` is the single declaration of tokens in code. The desktop's
  `app.css` links it and declares only app-specific values on top. A token value
  changed in one place MUST be changed in the other and here.
- Where a rule in these files is **not yet materialized** in the library, the
  file says so in a *Materialization* chapter rather than letting the two
  disagree silently.

## Table of Contents

```meta
```

| File | Scope |
|---|---|
| [`design-principles.md`](design-principles.md) | Product-level UX principles: local-first/offline UX, keyboard-first, low-chrome, AI-first surfaces, dark-mode-only. |
| [`color-scheme.md`](color-scheme.md) | MCP-sourced dark palette, semantic token table, badge and chip tones, chart and integration roles, contrast rules, elevation-by-color, per-stack token mapping. |
| [`typography-and-layout.md`](typography-and-layout.md) | Type scale, font choices, spacing scale, density, grid/layout rules, iconography, metadata lines. |
| [`interaction-guidelines.md`](interaction-guidelines.md) | Auto-save, drag-and-drop reordering of items and chapters, keyboard alternatives, feedback, motion, empty/loading/error states, task rows, the shell header, workspace panes, group shapes, the combobox, action density and overflow, timelines. |
| [`content-editing.md`](content-editing.md) | Direct Markdown editing where Markdown is canonical, and what AI may propose against a document. |
| [`accessibility.md`](accessibility.md) | WCAG AA target, keyboard nav, screen-reader announcements, focus visibility, reduced motion, target sizes. |
| [`component-libraries.md`](component-libraries.md) | Per-channel component-library research and recommendation. |
| [`effort-and-mode-chips.md`](effort-and-mode-chips.md) | The effort ladder, the four mode shapes, the not-as-configured and inferred marks, and the legend of a delivery run's stage list. |
| [`settings-pages.md`](settings-pages.md) | The Settings screen pattern a registered page follows: panel and headline, menu groups, badges and the Off state, declared card titles and search, the settings card and figure tiles, lists that expand in place, phone width, and `settings/{page}` addresses. |

## Provenance

```meta
```

The visual design tokens and UX patterns in this folder were adapted from the
JSdotNet design style guide (`JSdotNet/Project-Guidelines-MCP`, `design/`) and
imported into this repository on **2026-08-27**, from which date this folder is
authoritative. The specific upstream guides each file drew on are named in that
file. The org palette **includes a defined dark mode**, so the "dark mode only"
requirement was met by selecting the dark column of the org tokens, and no draft
palette was invented. There is no sync back to the upstream guide: a change here
is a change to the product's design language.

## Status Vocabulary

```meta
```

The `status` values a design file or chapter may carry, and what each means, are
defined in `.agents/rules/devbook-design.md#metadata`.
Metadata blocks follow that rule and `.agents/rules/devbook-chapter-metadata.md`.

Write `status` only for `draft` or `deprecated`. `active` is the resting value,
so a current guideline omits the field. Absence therefore means `active` here; it
never means "nobody has decided". The Devbook panels offer **No status** as an
option wherever absence means `active`. Removing the field leaves the `meta`
fence in place even when nothing else is left inside it, because the fence is
what marks a heading as an addressable chapter for the index generator.

The same reading applies in `.devbook/arc42` and `.devbook/domain`, and not in
`.devbook/tech` or `.devbook/ai`. In those two the field is a rating on an
adoption ladder, so it is required: an absent value could not be told apart from
`candidate`. `.devbook/ai` rates a way of working on the same ladder
`.devbook/tech` rates a technology on, so one adoption vocabulary reads in both
folders.
