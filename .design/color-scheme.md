# Color Scheme

```meta
status: active
related: [".design/design-principles.md#dark-mode-only", ".design/accessibility.md#contrast", ".arc42/04-solution-strategy.md#technology-choices"]
```

> The materialized dark-mode color palette and semantic design tokens for the
> Backlog product. Values are sourced from the organization's
> `jsdotnet-project-design` MCP guide `01-color-palette` ("Style Guide: Color
> Palette"). Because the product is **dark mode only**, only the dark-mode column
> of the org palette is adopted here; the light column is intentionally dropped.
> Brand, semantic and text tokens are the guide's values unchanged. **Surface and
> border tokens are a deliberate project deviation** — see
> [Surface and Border Deviation](#surface-and-border-deviation).
> This file is the single source of token *values*; all other `.design` files and
> product code reference token **names** only.

## Provenance

```meta
status: active
```

- **Source:** `jsdotnet-project-design` MCP server (`JSdotNet.MCP.Design`),
  tool `get_guide`, document id `01-color-palette`.
- **Selection:** dark-mode values only (the product has no light theme).
- **Deviation:** the surface and border ramps are project values, not the
  guide's. Recorded in [Surface and Border Deviation](#surface-and-border-deviation)
  with the measured contrast pairs the override contract requires.
- **Contrast rules, elevation-by-color, and focus-ring rules** are also
  sourced from that guide and from `04-motion-and-interaction`.
- The palette **includes a defined dark mode**, so no draft/invented palette was
  needed; this file is `active`, not `draft`.

## Dark Palette

```meta
status: active
related: [".design/design-principles.md#dark-mode-only"]
```

All tokens below are the **dark-mode** values. These are the only color values
the product uses.

### Brand

| Token | Value (dark) | Usage |
|---|---|---|
| `color-primary` | `#F2C14E` | Primary actions, active links, key highlights |
| `color-primary-light` | `#FFD166` | Hover states, tinted backgrounds |
| `color-primary-dark` | `#D4A72C` | Pressed states, high-contrast variant |
| `color-secondary` | `#ADB5BD` | Secondary buttons, less prominent labels |

### Semantic (soft surface tokens)

Semantic colors are **surface/background tokens first** — render readable
foreground text and icons on top of them; do not treat them as strong
standalone UI colors, and do not invent a second, stronger semantic palette.

| Token | Value (dark) | Usage |
|---|---|---|
| `color-success` | `#1A3A22` | Success banners, confirmation panels, positive status surfaces |
| `color-warning` | `#3D2E00` | Warning banners, caution panels, non-blocking alert surfaces |
| `color-error` | `#3D0A0D` | Error banners, validation summaries, destructive-status surfaces |
| `color-info` | `#0A2C31` | Informational notices, neutral announcement surfaces |

### Neutral / Text

| Token | Value (dark) | Usage |
|---|---|---|
| `color-text-primary` | `#F8F9FA` | Body text, headings, primary labels (content) |
| `color-text-secondary` | `#CED4DA` | Supporting text, metadata, placeholders, chrome |
| `color-text-disabled` | `#6C757D` | Disabled controls, non-interactive text |
| `color-text-inverse` | `#212529` | Text on brand / light-colored fills (e.g. on `color-primary`) |
| `color-text-link` | `#F2C14E` | Hyperlinks (same value as `color-primary`) |

### Background / Surface

| Token | Value (dark) | Usage |
|---|---|---|
| `color-background` | `#121214` | Primary page / panel background (base surface) |
| `color-background-alt` | `#202023` | Sidebar, card surface, alternating rows (surface +1) |
| `color-background-raised` | `#353539` | Elevated surfaces: dialog, popover, dropdown (surface +2) |
| `color-background-overlay` | `rgba(0,0,0,0.60)` | Modal backdrop / scrim |

### Border

| Token | Value (dark) | Usage |
|---|---|---|
| `color-border` | `#545459` | Default dividers, input outlines, card edges |
| `color-border-strong` | `#737379` | Emphasized borders, fallback focus ring |
| `color-border-focus` | `#F2C14E` | Keyboard focus ring (same value as `color-primary`) |

## Surface and Border Deviation

```meta
status: active
related: [".design/accessibility.md#contrast", ".design/design-principles.md#dark-mode-only"]
```

The org guide's `05-customization-guide` permits overriding brand and semantic
colors only, and lists `color-background-*` and `color-border-*` as
non-overridable. Its stated rationale is that those tokens are coupled to
contrast pairs validated for the default palette. **This product overrides them
anyway**, and discharges that rationale by re-validating every affected pair.

**What changed and why.** The guide's dark surfaces are a slate ramp
(`#0F172A` / `#1E293B` / `#334155`, borders `#475569` / `#64748B`). Slate carries
a blue cast that reads as a tinted background rather than a neutral one, and
tints the whole product against a warm gold brand. The replacement is a neutral
grey at a darker base, holding the ramp's rhythm: each surface is ~2.4x the
luminance of the one below, which is the spacing slate had, so elevation-by-colour
still reads at the same strength.

| Token | Guide (dark) | This product |
|---|---|---|
| `color-background` | `#0F172A` | `#121214` |
| `color-background-alt` | `#1E293B` | `#202023` |
| `color-background-raised` | `#334155` | `#353539` |
| `color-border` | `#475569` | `#545459` |
| `color-border-strong` | `#64748B` | `#737379` |

`color-background-overlay` and `color-border-focus` are unchanged.

**Measured pairs.** Every pairing the guide validates, measured against the
values above. Ratios are as reported by the Foundations page of
`Backlog.UI.Storybook`, which computes them from the live tokens.

| Pairing | Required | This product | Guide values |
|---|---|---|---|
| `color-text-primary` on `color-background` | 7:1 | **17.75:1** | 16.94:1 |
| `color-text-primary` on `color-background-alt` | 4.5:1 | **15.42:1** | 13.88:1 |
| `color-text-primary` on `color-background-raised` | 4.5:1 | **11.58:1** | 9.82:1 |
| `color-text-secondary` on `color-background-alt` | 4.5:1 | **10.88:1** | 9.79:1 |
| `color-primary` on `color-background` | 4.5:1 | **11.15:1** | 10.64:1 |
| `color-border-focus` vs `color-background` | 3:1 | **11.15:1** | 10.64:1 |
| `color-border-strong` vs `color-background` | 3:1 | **3.97:1** | 3.75:1 |
| `color-border` vs `color-background` | 3:1 | **2.49:1** ✗ | 2.36:1 ✗ |

Every pair improved on the guide's own values.

**Known gap.** `color-border` does not meet the 3:1 this document requires of UI
component boundaries. It did not meet it under the guide's values either
(2.36:1), so the deviation neither introduced nor resolved the failure — it is
inherited from the org palette. A neutral around `#616166` clears 3:1 against the
current base if the heavier divider weight is judged acceptable.
`[TODO: clarify]` whether to raise this against the org guide or override locally.

## Full Token Reference

```meta
status: active
```

Copy-paste reference of every color token and its single dark value.

| Token | Value |
|---|---|
| `color-primary` | `#F2C14E` |
| `color-primary-light` | `#FFD166` |
| `color-primary-dark` | `#D4A72C` |
| `color-secondary` | `#ADB5BD` |
| `color-success` | `#1A3A22` |
| `color-warning` | `#3D2E00` |
| `color-error` | `#3D0A0D` |
| `color-info` | `#0A2C31` |
| `color-text-primary` | `#F8F9FA` |
| `color-text-secondary` | `#CED4DA` |
| `color-text-disabled` | `#6C757D` |
| `color-text-inverse` | `#212529` |
| `color-text-link` | `#F2C14E` |
| `color-background` | `#121214` |
| `color-background-alt` | `#202023` |
| `color-background-raised` | `#353539` |
| `color-background-overlay` | `rgba(0,0,0,0.60)` |
| `color-border` | `#545459` |
| `color-border-strong` | `#737379` |
| `color-border-focus` | `#F2C14E` |

The twenty tokens above are the whole colour palette. `components.css` declares
exactly these, plus the code-block theme in `#syntax-highlighting-tokens`;
anything else in product code is a literal and MUST be replaced by a token.

## Syntax Highlighting Tokens

```meta
status: active
related: [".design/design-principles.md#dark-mode-only", ".design/content-editing.md#supported-constructs"]
```

`design-principles.md#dark-mode-only` requires syntax-highlight themes to be
authored for dark surfaces rather than borrowed from a light editor, so the code
theme is a token set of its own. It is a **theme, not a second semantic
palette**: each token names what a run of code *is*, never what a piece of UI
*means*, and nothing outside a code block may use one.

| Token | Value | Run |
|---|---|---|
| `code-plain` | `color-text-primary` | Everything unclassified |
| `code-keyword` | `color-primary` | Keywords — the "key highlight" the brand token is named for |
| `code-type` | `#7FD1C1` | Type names |
| `code-string` | `#9BD17F` | String literals |
| `code-number` | `#F0A868` | Numeric literals |
| `code-comment` | `#8595AD` | Comments |
| `code-operator` | `color-text-secondary` | Operators and punctuation |
| `code-tag` | `color-primary-light` | Markup tags |
| `code-attribute` | `#7FD1C1` | Markup attributes |
| `code-line-number` | `#7A8AA3` | Gutter line numbers |

Rules:

- Every value is measured against `color-background`, which is what a code block
  sits on: runs of actual code clear 9.3:1, and the two deliberately quiet ones —
  the comment grey at 6.1:1 and the line-number grey at 5.3:1 — still clear AA.
  These are the figures against the neutral base in
  `#surface-and-border-deviation`; each rose by ~5% when the base got darker, so
  the theme needed no revision, only re-measurement.
- The hues here are ones the product palette does not otherwise spend, so no two
  kinds of run read as the same thing.
- A new language MUST map onto these tokens; it MUST NOT add a colour.

## Contrast Rules (WCAG AA minimum)

```meta
status: active
related: [".design/accessibility.md#contrast"]
```

WCAG 2.1 AA is the **minimum** bar; the org guide targets AAA for primary body
text. Every color pairing MUST be verified against these rules.

| Pairing | Minimum ratio | Notes |
|---|---|---|
| `color-text-primary` on `color-background` | **7:1** (AAA target) | Primary content must be maximally legible. |
| `color-text-secondary` on `color-background` / `color-background-alt` | **4.5:1** (AA) | Supporting text and chrome. |
| Foreground text/icons on any `color-*` semantic surface | **4.5:1** (AA) | Semantic surfaces are backgrounds; pick a passing foreground. |
| Large text (≥ 24 px, or ≥ 18.66 px bold) | **3:1** (AA) | Applies to headings only. |
| Focus ring (`color-border-focus`) vs adjacent background | **3:1** | Non-text contrast for focus visibility. |
| UI component boundaries (`color-border`) vs adjacent surface | **3:1** | Non-text contrast for perceivable controls. |

Rules:

- `color-text-disabled` is **exempt** from contrast requirements — it
  intentionally signals unavailability.
- State MUST NOT be conveyed by color alone; always pair color with text, icon,
  or shape (see `accessibility.md`).
- On `color-primary` fills, use `color-text-inverse` for label text.

## Elevation by Color

```meta
status: active
related: [".design/design-principles.md#dark-mode-only"]
```

In dark mode, elevation is expressed primarily by **lighter surface color**, not
by heavy shadows.

| Elevation level | Surface token | Typical use |
|---|---|---|
| Base (0) | `color-background` | Page / editor canvas |
| Raised (+1) | `color-background-alt` | Cards, sidebars, list rows, panels |
| Overlay (+2) | `color-background-raised` | Dropdowns, popovers, dialogs, command palette |
| Scrim | `color-background-overlay` | Behind modals and side drawers |

Rules:

- Elevated surfaces MUST use the raised surface token as the primary elevation
  cue. Shadows are secondary.
- Shadow tokens (see `typography-and-layout.md#shadows-and-elevation`) MAY be
  layered under overlays but MUST have reduced opacity in dark mode (~30% less)
  so they do not muddy the surface.
- Do not stack more than two surface steps in a single visual context; if more
  depth is needed, reconsider the layout.

## Per-Stack Token Mapping

```meta
status: active
related: [".design/component-libraries.md", ".arc42/04-solution-strategy.md#technology-choices"]
```

The **same logical token set** is the shared layer across all channels. Each
stack maps token names to its native theming mechanism. Names MUST match exactly
(kebab-case here; adapt casing per platform convention but keep the stem).

| Logical token | Desktop (.NET MAUI Blazor Hybrid, CSS custom property) | Mobile — .NET MAUI (Resources) | Web / webview (CSS custom property) |
|---|---|---|---|
| `color-primary` | `--color-primary: #F2C14E;` | `<Color x:Key="ColorPrimary">#F2C14E</Color>` in `Resources/Styles` | `--color-primary: #F2C14E;` |
| `color-background` | `--color-background: #121214;` | `<Color x:Key="ColorBackground">#121214</Color>` | `--color-background: #121214;` |
| `color-text-primary` | `--color-text-primary: #F8F9FA;` | `<Color x:Key="ColorTextPrimary">#F8F9FA</Color>` | `--color-text-primary: #F8F9FA;` |
| `color-border-focus` | `--color-border-focus: #F2C14E;` | `<Color x:Key="ColorBorderFocus">#F2C14E</Color>` | `--color-border-focus: #F2C14E;` |

Rules:

- **One source of truth:** a single token definition file per stack, generated
  from this table — do not scatter literals across components.
- **Dark only:** desktop's CSS custom properties and mobile's MAUI resource
  dictionary MUST define only the dark theme (or a single default dictionary);
  do not ship a `Light` theme.
- **No raw literals in components:** components reference the keyed resource /
  CSS variable, never the hex value.
- **Webview parity:** the VS Code / Visual Studio webview MUST expose the same
  `--color-*` custom properties so shared web components render identically. Where
  a webview also runs inside an IDE theme, product tokens take precedence over
  host theme variables for content surfaces.
- A build-time token pipeline (e.g. a shared JSON/Style Dictionary source
  emitting XAML + CSS) is the recommended mechanism to keep the three stacks in
  sync. `[TODO: clarify]` whether such a pipeline is in scope for the first
  release or tokens are hand-maintained per stack.

## Materialization

```meta
status: active
related: [".design/README.md#living-reference-the-ui-storybook"]
```

| Aspect | Where |
|---|---|
| Declaration | `src/Core/Backlog.UI.Components/wwwroot/components.css`, `:root` — the only place these values exist in code. The desktop's `app.css` links it and adds app-specific values only. |
| Review surface | Storybook → *Foundations* → **Colour** |

The Foundations page does not transcribe this file. It reads the tokens out of
the live document, computes each ratio, and scores it against the thresholds in
`#contrast-rules-wcag-aa-minimum` — so a value edited in `components.css` and not
here shows up as a mismatch rather than passing unnoticed. It is also where the
measured pairs in `#surface-and-border-deviation` come from: the override
contract asks for evidence, and the evidence is generated rather than asserted.

`DesignTokenTests.Every_colour_the_library_declares_matches_the_value_in_dotdesign`
closes the same loop at build time — it fails when a value here and a value in
`components.css` disagree, when this file's own tables disagree with each other,
or when the stylesheet declares a colour this file does not name. The deviation
chapter is excluded from that comparison, because its second column is
deliberately a value the product does not use.

Materialized: all twenty palette tokens and all ten code tokens.

Not yet materialized: the per-stack mapping above is web-only in practice —
there is no mobile MAUI `ResourceDictionary` and no IDE webview yet, so the CSS
custom properties are currently the whole story.
