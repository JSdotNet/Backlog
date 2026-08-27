# Design Principles

```meta
status: active
related: [".arc42/04-solution-strategy.md#local-first-architecture", ".arc42/02-constraints.md#technical-constraints", ".design/interaction-guidelines.md", ".design/accessibility.md"]
```

> Product-level UX principles for the Backlog product. These are the high-level
> rules every screen and channel must honor; the other `.design` files turn them
> into concrete tokens and interaction specs. Adapted from the JSdotNet design
> style guide (`04-motion-and-interaction`, `09-interaction-patterns`) and the
> local-first architecture in
> `.arc42/04-solution-strategy.md`.

Each principle lists **testable rules**. Treat MUST / MUST NOT items as design
review acceptance criteria.

## Dark Mode Only

```meta
status: active
related: [".design/color-scheme.md#dark-palette"]
```

The product ships a single dark theme. There is no light theme and no theme
toggle.

| Rule | Requirement |
|---|---|
| Single theme | The product MUST render only the dark palette defined in `color-scheme.md`. |
| No toggle | There MUST NOT be a light/dark theme switch anywhere in settings or chrome. |
| No light assumptions | Components MUST NOT hard-code light-mode colors, white fills, or black text; they MUST reference dark-palette token names. |
| Media/embeds | Images, diagrams, and syntax-highlight themes MUST be authored or filtered for dark surfaces (no blinding white blocks). |
| Elevation by color | Elevation MUST be expressed primarily via raised surface tokens, not heavy shadows (see `color-scheme.md#elevation-by-color`). |

Rationale: the product is a focused, long-session authoring tool; a single dark
surface reduces eye strain and removes an entire class of theming bugs across
the MAUI (mobile-native), Razor/webview (desktop, IDE), and webview channels.

## Local-First, Offline-First UX

```meta
status: active
related: [".arc42/04-solution-strategy.md#local-first-architecture", ".arc42/08-crosscutting-concepts.md#storage-and-sync"]
```

The desktop owns the canonical data and all core workflows run without
connectivity; the cloud is additive only.

| Rule | Requirement |
|---|---|
| Never gate on network | Core flows (capture, triage, backlog, knowledge editing, reorder) MUST remain fully usable offline. |
| Offline is a first-class state | Offline MUST be shown as a calm, persistent status — not an error modal. See `interaction-guidelines.md#save-state-indicator-vocabulary`. |
| Optimistic UI | Edits MUST reflect immediately in the UI and persist locally first; sync happens in the background. |
| No data loss on disconnect | Writes made offline MUST be preserved locally and reconciled on reconnect per the last-write-wins rule in `.arc42/08-crosscutting-concepts.md#storage-and-sync`. |
| Sync is background | Sync progress MUST NOT block editing; it is surfaced through the save-state indicator, never a blocking spinner over content. |

## No Save Buttons — Auto-Save Everywhere

```meta
status: active
related: [".design/interaction-guidelines.md#auto-save-no-save-buttons"]
```

Every change is persisted automatically. There is no manual save.

| Rule | Requirement |
|---|---|
| No save affordance | There MUST NOT be a "Save" button, menu item, or keyboard-only save gesture as the means of persistence anywhere in the product. |
| Continuous persistence | All edits MUST auto-save; full behavior (debounce, indicator vocabulary, conflict handling) is specified in `interaction-guidelines.md#auto-save-no-save-buttons`. |
| Visible save state | The current save state (saving / saved / offline / conflict) MUST be visible without user action. |

## Keyboard-First

```meta
status: active
related: [".design/accessibility.md#keyboard-navigation", ".design/interaction-guidelines.md#keyboard-accessible-reordering"]
```

The product is optimized for keyboard-driven authoring and navigation; the
pointer is an accelerator, not a requirement.

| Rule | Requirement |
|---|---|
| Everything reachable | Every interactive element and every command MUST be operable from the keyboard alone. |
| DnD has a keyboard path | Every drag-and-drop action MUST have a documented keyboard equivalent (see `interaction-guidelines.md#keyboard-accessible-reordering`). |
| Command surface | Primary actions SHOULD be reachable through a command palette / slash commands rather than requiring chrome buttons. |
| Visible focus | Keyboard focus MUST always be visible using `color-border-focus` at `border-width-2` (see `accessibility.md#focus-visibility`). |
| Logical order | Tab order MUST follow reading order; focus traps are only for modals/drawers. |

## Low-Chrome, Content-First

```meta
status: active
```

Chrome recedes so the user's Markdown content is the primary surface.

| Rule | Requirement |
|---|---|
| Minimize persistent chrome | Toolbars and rails MUST be minimal; prefer contextual and on-demand affordances (hover handles, slash commands) over always-visible button rows. |
| No redundant controls | Because there are no save buttons, toolbars MUST NOT reintroduce save/commit affordances. |
| Content contrast priority | The highest-contrast text token (`color-text-primary`) is reserved for content; chrome uses `color-text-secondary`. |
| Progressive disclosure | Advanced actions SHOULD be revealed on focus/hover or via the command surface, not shown permanently. |
| Density | Default to a comfortable-but-compact density (see `typography-and-layout.md#density`). |

## AI-First Surfaces

```meta
status: active
```

AI assistance (capture, summarization, routing suggestions, Copilot session
context) is a designed, first-class surface — not a bolt-on.

| Rule | Requirement |
|---|---|
| Suggestions are non-blocking | AI suggestions MUST appear as dismissible, optimistic surfaces that never block manual editing. |
| Clearly attributed | AI-generated or AI-suggested content MUST be visually distinguishable from user content (e.g. a distinct chip/accent) and MUST be labelled for assistive tech. |
| User stays in control | AI actions that modify content MUST be reversible via undo/history (see `interaction-guidelines.md#undo-and-history`). |
| Consistent invocation | AI SHOULD be invoked through the same command/slash surface as other commands, for a single mental model. |
| Local-first honored | AI surfaces MUST degrade gracefully offline and MUST NOT imply that local editing depends on them. |

## Consistency Across Channels

```meta
status: active
related: [".design/component-libraries.md"]
```

The desktop (.NET MAUI Blazor Hybrid, Razor in WebView2), mobile (.NET MAUI),
and IDE (VS Code / Visual Studio) channels share one design language even
though they use different component libraries.

| Rule | Requirement |
|---|---|
| Shared tokens | All channels MUST consume the same logical token set (`color-scheme.md`, `typography-and-layout.md`); tokens are the shared layer, not components. |
| Same vocabulary | Save-state, reorder, and editing vocabulary MUST be identical across channels (same words, same states). |
| Platform-native input | Each channel MAY use platform-native controls, but MUST map them to the shared tokens and honor the principles above. |
| No per-channel drift | A pattern defined here MUST NOT be re-invented differently per channel; deviations require an explicit, recorded decision. |

## Materialization

```meta
status: active
related: [".design/README.md#living-reference-the-ui-storybook"]
```

The storybook (`src/Harness/Backlog.UI.Storybook`) is where these principles are
checked rather than asserted. It links the library stylesheet and adds page
chrome only, so a principle that has quietly stopped holding shows up there
first.

| Principle | Standing |
|---|---|
| Dark mode only | **Held.** One `:root`, one palette, no light column and no toggle anywhere in the library, the desktop app, or the storybook. The syntax theme is authored for dark surfaces rather than borrowed (`color-scheme.md#syntax-highlighting-tokens`). |
| No save buttons | **Held.** No save affordance exists; the storybook's *Entry edit* page runs the real save sequence, and its *Feedback* page shows the indicator on its own. |
| Local-first | **Held in the product; broken in one place** — Mermaid and G6 are fetched from a CDN (`component-libraries.md#materialization`). |
| Keyboard-first | **Partly held.** Components are keyboard-operable and reorder has arrow-key equivalents, but there is no command palette and no slash surface, so "primary actions reachable through a command surface" has nothing behind it. |
| Low-chrome, content-first | **Held in layout, inverted in colour** — the Markdown read view currently uses `color-text-secondary` for body text (`content-editing.md#materialization`). Scrollbars are treated as chrome and dressed in the border tokens by the library itself, so every host gets them rather than only the desktop. |
| AI-first surfaces | **Partly held.** The desktop integrates the Copilot CLI, but there is no shared command surface to invoke it through, and AI-attributed content has no distinct rendering yet. |
| Consistency across channels | **Untested.** Only the web-rendered channel exists; mobile MAUI and the IDE extensions have no UI to diverge yet. |
