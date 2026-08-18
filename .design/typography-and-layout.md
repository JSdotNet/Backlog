# Typography and Layout

```meta
status: active
related: [".design/color-scheme.md", ".design/design-principles.md#low-chrome-content-first", ".design/accessibility.md"]
```

> Type scale, font choices, spacing scale, density, grid/layout, elevation, and
> iconography tokens for the Backlog product. Sourced from the
> `jsdotnet-project-design` MCP guides `02-typography`, `03-spacing-and-layout`,
> `04-motion-and-interaction`, and `07-iconography`. This file declares the
> non-color token *values*; color values live in `color-scheme.md`. All other
> files reference token **names**.

## Font Families

```meta
status: active
```

| Token | Value | Usage |
|---|---|---|
| `font-family-base` | `'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif` | Body text, labels, inputs, all default prose |
| `font-family-heading` | `'Poppins', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif` | H1–H4 headings, display text |
| `font-family-mono` | `'Fira Code', 'Courier New', monospace` | Code blocks, inline code, raw-Markdown view, terminal output |

Rules:

- All families MUST include the system-font fallback chain so text stays legible
  if a web/embedded font fails to load.
- The raw-Markdown escape hatch (see `content-editing.md#raw-markdown-escape-hatch`)
  MUST use `font-family-mono`.
- On .NET MAUI (desktop and mobile), bundle the fonts as app resources; do not
  rely on OS availability.

## Type Scale

```meta
status: active
related: [".design/accessibility.md#target-sizes-and-text"]
```

Modular scale on a 16 px (1 rem) base. Use `rem` on web and on desktop's
Razor/WebView2 surface; use the equivalent device-independent point value on
mobile's native .NET MAUI. MUST NOT introduce sizes outside this scale.

| Token | rem | px | Usage |
|---|---|---|---|
| `font-size-xs` | `0.75rem` | 12 | Helper text, badges, fine print |
| `font-size-sm` | `0.875rem` | 14 | Secondary body, table cells, form hints (minimum readable size) |
| `font-size-base` | `1rem` | 16 | Default body / editor text |
| `font-size-lg` | `1.125rem` | 18 | Lead paragraphs, emphasized body |
| `font-size-xl` | `1.25rem` | 20 | Card titles, section sub-labels |
| `font-size-2xl` | `1.5rem` | 24 | H4 |
| `font-size-3xl` | `1.875rem` | 30 | H3 |
| `font-size-4xl` | `2.25rem` | 36 | H2 |
| `font-size-5xl` | `3rem` | 48 | H1, display |

### Weights, Line Heights, Letter Spacing

| Weight token | Value | Line-height token | Value | Letter-spacing token | Value |
|---|---|---|---|---|---|
| `font-weight-light` | 300 | `line-height-none` | 1 | `letter-spacing-tight` | -0.025em |
| `font-weight-normal` | 400 | `line-height-tight` | 1.25 | `letter-spacing-normal` | 0 |
| `font-weight-medium` | 500 | `line-height-normal` | 1.5 | `letter-spacing-wide` | 0.05em |
| `font-weight-semibold` | 600 | `line-height-relaxed` | 1.75 | `letter-spacing-widest` | 0.1em |
| `font-weight-bold` | 700 | | | | |

Rules:

- Body text MUST NOT go below `font-size-sm` (14 px).
- `font-weight-light` (300) is permitted only at `font-size-3xl` and above.
- Do not use `font-weight-bold` (700) for body emphasis — use
  `font-weight-semibold` (600).
- Body text uses `line-height-normal` or `line-height-relaxed`; headings use
  `line-height-tight`.

### Heading Defaults

| Element | Size | Weight | Line height | Family |
|---|---|---|---|---|
| H1 | `font-size-5xl` | `font-weight-bold` | `line-height-tight` | `font-family-heading` |
| H2 | `font-size-4xl` | `font-weight-bold` | `line-height-tight` | `font-family-heading` |
| H3 | `font-size-3xl` | `font-weight-semibold` | `line-height-tight` | `font-family-heading` |
| H4 | `font-size-2xl` | `font-weight-semibold` | `line-height-tight` | `font-family-heading` |
| H5 | `font-size-xl` | `font-weight-semibold` | `line-height-normal` | `font-family-base` |
| H6 | `font-size-lg` | `font-weight-semibold` | `line-height-normal` | `font-family-base` |
| Body | `font-size-base` | `font-weight-normal` | `line-height-normal` | `font-family-base` |
| Small | `font-size-sm` | `font-weight-normal` | `line-height-normal` | `font-family-base` |
| Code | `font-size-sm` | `font-weight-normal` | `line-height-relaxed` | `font-family-mono` |

These heading defaults are the **document/display** ramp: they apply to page and
section headings and to a Markdown document rendered as a document.

They are **not** what `MarkdownView` currently renders. Inside a list row or an
entry card, a body is rendered on a deliberately compact ramp (`#` at
`font-size-lg` down to `font-size-sm`, with `####`–`######` as uppercase
secondary labels) because a 48 px `H1` inside a card is a layout, not a heading.
Both ramps are legitimate; which one applies is a function of the surface, and a
document surface MUST use the table above. See
`content-editing.md#materialization`.

## Spacing Scale

```meta
status: active
```

4 px (0.25 rem) base, geometric progression. MUST always use a scale token;
never one-off values like `13px`.

| Token | rem | px | Common usage |
|---|---|---|---|
| `spacing-0` | 0 | 0 | Explicit zero |
| `spacing-xs` | 0.25rem | 4 | Icon–label micro-gap, badge padding |
| `spacing-sm` | 0.5rem | 8 | Input/button vertical padding, inline gaps, toast gap |
| `spacing-md` | 1rem | 16 | Default component padding, form field spacing (default unit) |
| `spacing-lg` | 1.5rem | 24 | Small section padding, card header/footer |
| `spacing-xl` | 2rem | 32 | Medium section padding, modal padding |
| `spacing-2xl` | 3rem | 48 | Large section padding, page vertical rhythm |
| `spacing-3xl` | 4rem | 64 | Hero sections, large-screen whitespace |
| `spacing-4xl` | 6rem | 96 | Wide-viewport page margins |

## Density

```meta
status: active
related: [".design/design-principles.md#low-chrome-content-first"]
```

The product is an authoring tool used in long sessions; default to a
**comfortable-but-compact** density.

| Rule | Requirement |
|---|---|
| Default component padding | `spacing-md` for component internals; `spacing-lg`/`spacing-xl` for outer sections. |
| List rows | Backlog/knowledge list rows use `spacing-sm` vertical padding; MUST keep a ≥ 44 px pointer target (see `accessibility.md#target-sizes-and-text`). |
| Editor line length | Long-form editor content SHOULD cap measure at ~72–90 characters for readability, centered in the canvas. |
| No cramped controls | Interactive controls MUST retain minimum touch/click targets even at compact density. |

## Layout Grid and Breakpoints

```meta
status: active
```

| Concept | Value |
|---|---|
| Column count | 12 |
| Default gutter | `spacing-md` (16 px) |
| Narrow container max-width | 640 px |
| Default container max-width | 1280 px |
| Wide container max-width | 1536 px |
| Breakpoints | `sm` ≥ 640 · `md` ≥ 768 · `lg` ≥ 1024 · `xl` ≥ 1280 · `2xl` ≥ 1536 |

Channel guidance:

- **Desktop (.NET MAUI Blazor Hybrid, Razor/WebView2)** and **IDE webviews**
  target the `lg`+ range and a multi-pane layout (navigation rail + list +
  editor).
- **Mobile (.NET MAUI, native)** targets `sm`; panes collapse to a single-column,
  navigation-drawer layout. Reorder and editing MUST remain fully usable in the
  single-column layout.

## Border Radius and Width

```meta
status: active
```

| Radius token | Value | Usage | Width token | Value |
|---|---|---|---|---|
| `border-radius-none` | 0 | Tables, code blocks | `border-width` | 1px (default) |
| `border-radius-sm` | 0.25rem (4px) | Badges, chips, tags | `border-width-2` | 2px (focus, selected) |
| `border-radius-md` | 0.5rem (8px) | Inputs, buttons, cards (default) | `border-width-4` | 4px (accent stripes, progress) |
| `border-radius-lg` | 1rem (16px) | Large cards, panels, modals | | |
| `border-radius-xl` | 1.5rem (24px) | Feature cards | | |
| `border-radius-full` | 9999px | Pills, avatars | | |

Rules: interactive controls use `border-radius-md`; focus rings and selected
states use `border-width-2`.

## Shadows and Elevation

```meta
status: active
related: [".design/color-scheme.md#elevation-by-color"]
```

In dark mode, elevation is primarily **color-based** (see
`color-scheme.md#elevation-by-color`); shadows are secondary and MUST have
reduced opacity (~30% less than the raw token) in dark mode.

| Token | Value | Usage |
|---|---|---|
| `shadow-none` | none | Flat surfaces |
| `shadow-sm` | 0 1px 2px rgba(0,0,0,0.05) | Subtle lift: inputs, inline chips |
| `shadow-md` | 0 4px 6px rgba(0,0,0,0.10) | Default card elevation |
| `shadow-lg` | 0 10px 15px rgba(0,0,0,0.15) | Dropdowns, popovers |
| `shadow-xl` | 0 20px 25px rgba(0,0,0,0.20) | Modals, drawers, toasts |
| `shadow-inner` | inset 0 2px 4px rgba(0,0,0,0.06) | Pressed / active states, inset inputs |

### Z-Index Scale

| Token | Value | Layer |
|---|---|---|
| `z-index-base` | 0 | Document flow |
| `z-index-raised` | 10 | Sticky headers, floating action buttons |
| `z-index-dropdown` | 100 | Dropdown menus, slash-command popovers |
| `z-index-overlay` | 200 | Side drawers, slide-over panels |
| `z-index-modal` | 300 | Modal dialogs |
| `z-index-toast` | 400 | Toast / save-state notifications |
| `z-index-tooltip` | 500 | Tooltips, popovers (always on top) |

MUST NOT use arbitrary z-index values (e.g. `9999`); use only the scale.

## Iconography

```meta
status: active
related: [".design/accessibility.md#target-sizes-and-text"]
```

Icon library: **Lucide** (outline, 2 px stroke, 24×24 base grid). Substitutions
allowed only if stroke-based SVG at ~2 px weight, added to a project-local
registry.

| Token | Size | Usage |
|---|---|---|
| `icon-xs` | 12px | Inline badge indicator |
| `icon-sm` | 16px | Inline text icons, dense table/list actions, stale-data spinner |
| `icon-md` | 20px | Default in buttons, inputs, nav items, drag handle (`grip-vertical`) |
| `icon-base` | 24px | Standalone icon on the base grid |
| `icon-lg` | 32px | Section headers |
| `icon-xl` | 48px | Empty-state illustrations |
| `icon-2xl` | 64px | Hero/splash only |

Rules:

- Icons inherit color via `currentColor`; MUST NOT hard-code `fill`/`stroke` in
  product code. Default icon color follows `color-text-primary`; supporting icons
  use `color-text-secondary`.
- Never scale icons via `font-size`; use explicit width/height.
- Icon color MUST meet 3:1 contrast against its background and MUST NOT be the
  sole carrier of meaning (see `accessibility.md`).
- Icon-only controls require an `aria-label`/automation name and a ≥ 44 × 44 px
  target (see `accessibility.md#target-sizes-and-text`).
- Standard status icons: `check-circle` (success), `alert-triangle` (warning),
  `x-circle` (error), `info` (info), `loader-2` (loading), `grip-vertical` (drag
  handle).

## Materialization

```meta
status: active
related: [".design/README.md#living-reference-the-ui-storybook", ".design/color-scheme.md#materialization"]
```

Declared in `src/Core/Backlog.UI.Components/wwwroot/components.css`; shown in the
storybook's *Foundations* page, which measures each token in the live document
rather than transcribing it.

| Scale | Materialized | Not yet declared in code |
|---|---|---|
| Font families | all three | — |
| Font sizes | `xs`–`2xl`, `4xl` | `3xl`, `5xl` |
| Line heights | `tight`, `normal` | `none`, `relaxed` |
| Weights, letter spacing | — (set inline where needed) | the whole set |
| Spacing | `xs`–`xl` | `0`, `2xl`, `3xl`, `4xl` |
| Border radius | `none`, `sm`, `md`, `lg`, `full` | `xl` |
| Border width | all three | — |
| Shadows | `sm`, `md`, `lg` | `none`, `xl`, `inner` |
| Motion | `fast`, `base`, `slow` (duration and easing bundled into one token each) | `instant`, `page`; easing is not separately tokenized |
| Z-index | — | the whole scale |
| Icon sizes | — | the whole scale |

Rules and known gaps:

- A scale being **partly** declared is not a violation — the rule is that no size
  outside the scale may be introduced. A component needing `font-size-3xl` adds
  the token from the table above; it MUST NOT invent a value.
- **Fonts are declared but not loaded.** Nothing in the repository ships Inter,
  Poppins or Fira Code: there is no `@font-face` rule and no webfont link in any
  host, so text falls through to the system stack and the type tokens describe an
  intention rather than a fact. The storybook's **Typography** story measures this
  and says so on the page. Closing it is a product decision — either the `woff2`
  files ship from the library's own `wwwroot` (the shipping heads are
  offline-capable MAUI apps, so a CDN is not an option), or these tokens are
  rewritten to name the system stack actually in use. `[TODO: clarify]`
- **The z-index scale is not materialized.** `components.css` uses small local
  values (1–30) per stacking context instead. Those are not arbitrary in the
  `9999` sense the rule was written against, but they are also not the scale:
  until the tokens are declared and adopted, the "use only the scale" rule is
  aspirational for the web surfaces.
- **Shadow values in code are the dark-reduced ones.** `shadow-md` is declared at
  `0.07` and `shadow-lg` at `0.11` — the table above lists the raw values and the
  ~30% dark-mode reduction is already applied in the stylesheet. `shadow-sm` is
  declared unreduced, being subtle enough that the reduction is not visible.
- One layout token exists in code that is not a design-system token:
  `--pane-min-width` (22 rem), how little room a pane resizer must leave the
  content beside it. It is a component measurement, named for the split rather
  than for what a host puts in it.
