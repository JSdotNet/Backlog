# Color Scheme

```meta
status: active
related: [".design/design-principles.md#dark-mode-only", ".design/accessibility.md#contrast", ".arc42/04-solution-strategy.md#technology-choices"]
```

> The materialized dark-mode color palette and semantic design tokens for the
> Backlog product. Values were adapted from the JSdotNet design style guide
> `01-color-palette` ("Style Guide: Color Palette"), imported 2026-08-27.
> Because the product is **dark mode only**, only the dark-mode column
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

- **Source:** the JSdotNet design style guide, document `01-color-palette`,
  imported 2026-08-27. This file is authoritative from that date.
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
| `color-success-text` | `#72C086` | Success text and icons — the legible foreground for the `color-success` surface, and for a confirmation line with no surface behind it |
| `color-warning` | `#3D2E00` | Warning banners, caution panels, non-blocking alert surfaces |
| `color-error` | `#3D0A0D` | Error banners, validation summaries, destructive-status surfaces |
| `color-error-text` | `#EC8E97` | Error text, icons and input outlines — the legible foreground for the `color-error` surface, and for error text with no surface behind it |
| `color-info` | `#0A2C31` | Informational notices, neutral announcement surfaces |

`color-error-text` and `color-success-text` are the two deliberate exceptions to
"surfaces only", and they exist because the rule above left a hole. Foreground
text on a semantic panel covers a banner, but a bare status line, a status icon,
or an input outline has no semantic surface behind it and so had no sanctioned
colour at all. Both are **foregrounds only** — neither is ever painted as a
background — and they do not open the door to a second semantic palette, because
they serve the two meanings that needed one. `color-warning` and `color-info` get
no counterpart: no rule paints either as text, and a token nothing uses is a
value nothing measures.

Each is a legibility correction to the colour already in use rather than a new
colour: same hue, same saturation, lightness lifted until the binding pair
passes. The binding pair is `color-background-raised` in both cases — it is the
lightest surface either ink sits on, and a status string lands in a dropdown, a
popover or a badge next.

**`color-error-text`.** Two unsanctioned answers grew up in the gap: a raw
`#E4626F` repeated through the desktop stylesheet, and a `--color-danger` that
product code referenced but nothing ever declared. This token is the missing
answer.

**The value is derived, not picked.** `#E4626F`, the incumbent literal, is
hsl(354, 70.7%, 63.9%) and measures 5.59:1 on `color-background`, 4.86:1 on
`color-background-alt` and **3.65:1** on `color-background-raised`. It fails the
4.5:1 AA this document requires of a foreground on a semantic surface. No error
message sits on a raised surface *today* — dialogs, modals and popovers are all
built on `color-background` — so the alternative was to keep the incumbent and
restrict it to the two surfaces it clears. That was rejected: the restriction
would have to be honoured by the author of every future one-line error rule,
nothing would enforce it, and the table below already states the requirement
unrestricted, for a foreground on *any* semantic surface.
`color-background-raised` is live for dropdowns, popovers and badges, which is
where an error string lands next.

`#EC8E97` is hsl(354, 70.7%, **74.0%**) — the same hue and the same saturation
with only the lightness lifted, so this is a legibility correction to the colour
already in use rather than a redesign of it.

| Pairing | Required | `color-error-text` |
|---|---|---|
| on `color-background` | 4.5:1 | **7.91:1** |
| on `color-background-alt` | 4.5:1 | **6.87:1** |
| on `color-background-raised` | 4.5:1 | **5.17:1** |
| on the `color-error` surface | 4.5:1 | **7.12:1** |
| as a border vs `color-background` | 3:1 | **7.91:1** |
| as a border vs `color-background-alt` | 3:1 | **6.87:1** |
| as a border vs `color-background-raised` | 3:1 | **5.17:1** |

Every ratio is computed with the WCAG 2.1 relative-luminance formula. The
binding pair is the raised surface, as it was for the incumbent. 72% lightness
(`#EA858F`, 4.80:1 on raised) is the lowest lightness that passes at all; 74% was
chosen so the binding pair is not sitting 0.3 above the threshold, where a later
surface adjustment would quietly push it back under.

**`color-success-text`.** The success meaning fell into the same hole and took
the other way out of it. Nothing invented a literal: `.setting__status--ok` in
the desktop stylesheet painted the Settings confirmation line in `color-success`
itself, the surface token used as ink. That is **1.49:1** on `color-background`,
1.29:1 on `color-background-alt` and 1.03:1 on `color-background-raised` — a
near-black green on a near-black page, which is not a weak colour choice but an
invisible line. This token is what that rule should have named.

**The value is derived, not picked**, the same way. `#72C086` is
hsl(135, 38.2%, **60.0%**) against `#1A3A22`'s hsl(135, 38.1%, 16.5%): the hue
and the saturation of the colour already in use, with only the lightness lifted.
It is a legibility correction to `color-success`, not a second green.

| Pairing | Required | `color-success-text` |
|---|---|---|
| on `color-background` | 4.5:1 | **8.55:1** |
| on `color-background-alt` | 4.5:1 | **7.43:1** |
| on `color-background-raised` | 4.5:1 | **5.58:1** |
| on the `color-success` surface | 4.5:1 | **5.74:1** |
| as a border vs `color-background` | 3:1 | **8.55:1** |
| as a border vs `color-background-alt` | 3:1 | **7.43:1** |
| as a border vs `color-background-raised` | 3:1 | **5.58:1** |

Same formula, same binding pair: the raised surface at 5.58:1, with the same
margin above 4.5 that `color-error-text` was tuned for, so a later surface
adjustment does not quietly push it back under.

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
| `color-success-text` | `#72C086` |
| `color-warning` | `#3D2E00` |
| `color-error` | `#3D0A0D` |
| `color-error-text` | `#EC8E97` |
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

The twenty-two tokens above are the whole colour palette. `components.css` declares
exactly these, plus the code-block theme in `#syntax-highlighting-tokens`, the
band identity set in `#band-identity-tokens`, and the derived role tokens in
`#role-tokens`; anything else in product code is a literal and MUST be replaced by
a token.

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

## Band Identity Tokens

```meta
status: active
related: [".design/design-principles.md#dark-mode-only", ".design/accessibility.md#contrast", ".domain/roadmap/features.md#repository-scoped-planning"]
```

A workspace holds several repositories at once, and a reader needs to tell one
project's work from another's at a glance — on a portfolio plan, in a list of
entries, and on the filter that scopes the whole app. That is the second thing in
this product to need more than one hue, and it is the same *kind* of thing as the
first: like the code theme above, this is **a theme, not a second semantic
palette**. Each token names *which repository* something belongs to — an identity —
never what a piece of UI *means*.

The set was introduced for the roadmap band and was restricted to it while the band
was its only consumer. It is now the product's **repository identity**, and the
restriction is a list rather than a single surface. Exactly these may use one:

| Consumer | Mark |
|---|---|
| Roadmap band label and its bars | the band painting below |
| Repository scope filter chip in the app header | the identity edge below |
| An entry row whose area resolves to a repository | the identity edge below |
| An agent session row under such an entry | the identity edge below |
| The repository cell of a row in the Sessions area | the identity edge below |
| The colour picker in Settings → Repositories | a solid swatch of the hue itself |

The picker is the one place a hue is painted as a fill rather than an edge, and that
is not an exception to the rule below — it is the rule's own consequence. A control
whose subject *is* the colour has to show the colour; a swatch reading "sample of
band 3" in a 4px sliver would be a picker you cannot pick from. Every swatch is
labelled and the chosen one is marked, so the fill is still not the sole carrier.

Nothing else may. A new consumer is an addition to that table, argued here, and
never a hue added to the set.

**No new colour value is introduced.** Every value below already appears in this
document: one is `color-primary`, and the other four are the hues the code theme
already spends. That is the difference from [Role Tokens](#role-tokens), whose
values are all *derived* from a palette token — these are not derived, but they are
not new either, so the count of colours this product uses is unchanged and there is
no new value for this file to disagree with.

They are their own token names rather than references to `code-type` and friends
because the code theme's own rule says nothing outside a code block may use one of
its tokens. Reading that as "the names are reserved, the values are the file's" is
the honest reading rather than a loophole: the rule exists so a reader never has to
wonder whether a coloured run is a type name, and a band in a Gantt chart cannot be
mistaken for one.

| Token | Value | Source in this file |
|---|---|---|
| `color-band-1` | `#F2C14E` | the same value as `color-primary` |
| `color-band-2` | `#7FD1C1` | the same value as `code-type` |
| `color-band-3` | `#9BD17F` | the same value as `code-string` |
| `color-band-4` | `#F0A868` | the same value as `code-number` |
| `color-band-5` | `#8595AD` | the same value as `code-comment` |

### Band painting

A band colour is painted three ways: a 4px left border on the band's own label, the
label's surface as `color-mix(in srgb, <token> 24%, color-background)`, and
`color-text-primary` on that surface. A border is a non-text mark and owes 3:1; ink
on a surface owes the 4.5:1 in
[#contrast-rules-wcag-aa-minimum](#contrast-rules-wcag-aa-minimum).

| Token | Border vs `color-background` | Border vs `color-background-alt` | 24% label surface | `color-text-primary` on it |
|---|---|---|---|---|
| `color-band-1` | 11.15:1 | 9.68:1 | `#483C22` | 10.24:1 |
| `color-band-2` | 10.51:1 | 9.13:1 | `#2C403E` | 10.43:1 |
| `color-band-3` | 10.53:1 | 9.15:1 | `#33402E` | 10.41:1 |
| `color-band-4` | 9.36:1 | 8.13:1 | `#473628` | 10.91:1 |
| `color-band-5` | 6.15:1 | 5.34:1 | `#2E3139` | 12.34:1 |

Every ratio is the WCAG 2.1 relative-luminance formula. The binding pair is
`color-band-5` as a border on `color-background-alt` at 5.34:1 — still two and a
half times the 3:1 it owes, because the set was chosen from values already measured
against these surfaces rather than picked for looks.

### The identity edge

Off the roadmap the hue is **additive only**: a 4px rule down the leading edge of a
control that was already there, painted as an inset shadow rather than a border so
the element's own box, padding and alignment are untouched. Nothing is tinted,
nothing is recoloured, and every surface, border and text token the control already
wore stays exactly as it was. A chip that read correctly before the repository had a
colour reads identically after it, with a stripe down its left edge.

That restraint is the whole reason the set may leave the roadmap. A band is a region
of a chart and can afford a 24% wash; a filter chip and an entry row sit inside dense
chrome that already spends its contrast on status, priority and selection. A wash
there would put an identity hue in competition with colours that carry meaning, which
is the one thing the rule above forbids.

The edge is a non-text mark and owes 3:1 against whatever the control is filled with.
The two fills it lands on that the table above does not already cover are
`color-background-raised` — the resting scope chip and a selected entry row — and
`color-primary`, the active scope chip.

| Token | Edge vs `color-background-raised` |
|---|---|
| `color-band-1` | 7.28:1 |
| `color-band-2` | 6.86:1 |
| `color-band-3` | 6.87:1 |
| `color-band-4` | 6.11:1 |
| `color-band-5` | 4.01:1 |

`color-band-5` at 4.01:1 is the binding pair for the edge, a third above the 3:1 it
owes.

**The active scope chip is the one case the ratio cannot answer.** It is filled with
`color-primary`, and `color-band-1` *is* `color-primary` — a band-1 edge on a
selected chip would measure 1:1 and disappear. The edge is therefore separated from
that fill by a 1px seam in `color-background`, which clears 3:1 on both sides
(11.15:1 against `color-band-1`, 11.15:1 against `color-primary`). The seam is drawn
only on the active chip, because it is the only place two sanctioned colours meet.

Rules:

- **Colour here is identity, never meaning.** A band's hue says which repository.
  It carries no severity, no status and no priority. Priority on a roadmap is the
  ordinal shade ramp on the bars, which is one hue and stays that way.
- **Colour is never the sole carrier.** Every band is also labelled with its
  repository alias written down its own side, every bar names its band in its
  accessible name, and the repository filter lists them by full name. The band
  sidebar is `aria-hidden` and so is every identity edge, so a reader who never sees
  a hue loses nothing — on any of the surfaces above, the alias is still written
  there in words.
- **Colour never competes with meaning.** Status, priority, severity and selection
  keep the colours they have. An identity hue is only ever an edge beside them, never
  the fill behind them, so the two can be read at the same time without either
  becoming ambiguous.
- **One choice, one place.** A repository's hue is chosen in Settings and every
  surface reads that one answer, so the same project is the same colour on the
  roadmap, on the filter and on a row. A surface that picked its own would be a
  second identity for the same thing.
- **The layer is opt-in, and off is the default.** Which hue a repository wears is
  chosen in Settings; whether the hues are drawn at all is a switch on the main
  page, off until somebody asks for it. Two controls because they are two
  questions: one is set once and left alone, the other is about the screen in front
  of you. The consumer table above therefore says which surfaces *may* carry an
  identity, not which ones always do — with the layer off, each renders the
  uncoloured presentation it already has for a repository nobody gave a colour to,
  and the Settings picker keeps showing its swatches, because a control whose
  subject is the colour has to show it. That the layer can be off in its entirety
  is also why **colour is never the sole carrier** above is not a formality: with
  the hues gone, the alias written in words is all that is left.
- **Hues are assigned by position** in the configured-repository list and wrap after
  five, so a sixth repository repeats the first hue. An explicit choice in Settings
  overrides the position, and an automatic hue steps over the ones already claimed so
  it never lands on a neighbour's. Two repositories sharing a hue is acceptable
  precisely because the hue is not the identifier — the label is.
- A new consumer wanting categorical colour MUST justify itself against this
  section rather than adding hues, exactly as a new language MUST map onto the code
  theme rather than adding a colour.

Provenance note: the organization's `01-color-palette` guide defines five token
groups — brand, semantic, text, background, border — and has **no categorical or
identity group** to conform to, so there was nothing upstream to adopt. Its
`05-customization-guide` makes colour the one thing a project may customize, which
is the envelope this sits inside; the override contract's "both light and dark
variants" does not bite, because this product is dark-only and every value here is
one the file already carries.

## Role Tokens

```meta
status: active
related: [".design/design-principles.md#dark-mode-only", ".design/accessibility.md#contrast"]
```

Two component families need role names the twenty-two palette tokens do not provide:
a chart needs a series colour, a track, a gridline and a baseline, and an
integration affordance needs a live state, a landed one, a quiet one and the two
surfaces the product acts on. Like the code theme these are **themes, not a
second palette** — with one difference that matters: **every value below is
derived.** Each is a `var()` of a palette token or a `color-mix()` of one against
the surface it is drawn on, so none holds a colour of its own, the palette is
still twenty-two colours, and there is no new value for this file to disagree with.

### Badge and chip tones

A badge is a value with a class on it, and the class is what says which family
the value belongs to. Each family maps its own vocabulary onto **one shared tone
scale**, so a reader learns the scale once and reads it in every family. The
vocabulary is always the caller's; the tone is always this file's.

The families the stylesheet ships are `status`, `priority`, `type`, `area`,
`kind`, `alias`, `feature`, `progress`, `source`, `tool`, `glob`, `gh` and
`integration`. This paragraph named three of them for a long time while the
stylesheet grew to thirteen, so read the list as a count that has drifted before
and may drift again — `components.css` is the authority on which families exist,
and this file is the authority on what any of them may look like.

| Tone | What it means | Palette derivation |
|---|---|---|
| `quiet` | Nothing has happened to it yet — draft, proposed, unset | `color-secondary` ink on `color-background-alt` |
| `live` | In progress, and the product may act on it | `color-primary-light` ink, `color-primary-dark` edge |
| `alert` | Wants attention, but is not a fault | `color-warning` as a **surface** |
| `fault` | Failed, blocked, or contradicted | `color-error` as a **surface** |
| `settled` | Finished, and correct — done, merged, adopted | `color-success` as a **surface** |
| `archived` | Out of force, and spends no colour: a transparent surface inside a border | `color-border-strong` edge only |

Rules:

- A badge MUST take its tone from this scale. A family that needed a sixth tone
  would be a second palette, which `design-principles.md#dark-mode-only` does not
  allow.
- **Filled means the product acts on it.** `alert`, `fault` and `settled` are
  filled surfaces; everything else is outlined, so a filled chip in a list is
  always the one to look at. A state that is merely information MUST NOT be
  filled.
- A filled chip takes `color-border-strong` as its boundary rather than a mix of
  its own surface — the semantic tokens are 1.04:1 to 1.31:1 against the raised
  surface and are invisible as boundaries. See `#integration-roles`, which records
  the same finding for state chips.
- A badge's text is small, so it MUST clear the 4.5:1 that
  `#contrast-rules-wcag-aa-minimum` sets for supporting text. `color-text-disabled`
  is **not** available to a badge: an out-of-force value is information, not a
  disabled control, and takes `archived` instead.
- Colour MUST NOT be the only carrier. Every badge prints its value as text, and
  a badge carrying a glyph keeps the words beside it (see
  `accessibility.md#iconography-accessibility`).
- **A value in no vocabulary is flagged, not styled.** Where the folder or family
  defines a vocabulary and the value is not in it, the badge MUST render the value
  verbatim, wear `archived`, and name the expected values on its `title`. Painting
  an unrecognised value like every other unknown word lets a typo sit in a file
  indefinitely.
- A status that can be changed MUST be drawn as the same badge as one that cannot.
  A reader must never learn from a colour or a shape that an editable status is a
  different kind of status.
- **A badge that can be followed MUST say so at rest.** Where a family draws the
  same value as a link in one place and as plain text in another — an alias a host
  can resolve beside one it cannot — the followable one takes the `live` ink and
  edge, and the other keeps its quiet treatment. Hover and focus are not enough:
  a reader who does not already suspect there is something to click never finds
  out. The fill stays out of it, because filled still means the product acts on
  it, and following a link is the reader acting. `gh` is exempt and is the reason
  the rule is written this way: those badges are already anchors, and their state
  colours are the cue.

Knowledge chapters are the largest consumer of this scale: five folders spell
their lifecycles five different ways, and `README.md#status-vocabulary` maps each
word onto the tones above. Review surface: storybook → *Badges*, and
*Knowledge base* → **State**.

### Chart roles

Measured on `color-background-alt`, which is the card a chart sits on.

| Token | Derivation | Measured |
|---|---|---|
| `chart-surface` | `color-background-alt` | — |
| `chart-series` | `color-primary` | 9.68:1 |
| `chart-ramp-1` | `color-primary` at 45% on the surface | 3.05:1 |
| `chart-ramp-2` | `color-primary` at 60% on the surface | 4.38:1 |
| `chart-ramp-3` | `color-primary` at 80% on the surface | 6.69:1 |
| `chart-ramp-4` | `color-primary` | 9.68:1 |
| `chart-track` | `color-primary` at 20% on the surface | 1.59:1 |
| `chart-grid` | `color-border` | 2.16:1 |
| `chart-axis` | `color-border-strong` | 3.45:1 |
| `chart-ink` | `color-text-primary` | 15.42:1 |
| `chart-ink-muted` | `color-text-secondary` | 10.88:1 |

Rules:

- The palette carries exactly one saturated hue, so charts are **single-hue by
  rule**. Part-to-whole takes the ordinal ramp; two measures or two providers are
  drawn as two charts, never as two series on one plot. This is not contradicted by
  `#band-identity-tokens`: that set colours an *identity* — which repository a row
  belongs to — where this rule is about a *measure*. A second hue on one plot would
  say "this quantity is a different kind of quantity", which is exactly the claim
  one hue exists to avoid making.
- Every ramp step clears the 3:1 a non-text mark owes its background. The track
  is deliberately below it, because it is the absence of data rather than data.
- The baseline takes `chart-axis` rather than `chart-grid`, because a baseline
  carries meaning and a gridline does not.

### Integration roles

Measured on `color-background-alt`, which is what a state chip sits on.

| Token | Derivation | Measured |
|---|---|---|
| `integration-surface` | `color-background-alt` | — |
| `integration-ink` | `color-text-primary` | 15.42:1 |
| `integration-ink-muted` | `color-text-secondary` | 10.88:1 |
| `integration-ink-quiet` | `color-secondary` | 7.83:1 |
| `integration-ink-off` | `color-text-disabled` | exempt |
| `integration-edge` | `color-border` | 2.16:1 |
| `integration-edge-strong` | `color-border-strong` | 3.42:1 |
| `integration-live` | `color-primary-light` | 11.27:1 |
| `integration-live-edge` | `color-primary-dark` | 7.19:1 |
| `integration-alert-surface` | `color-warning` | ink on it: 13.2:1 |
| `integration-fault-surface` | `color-error` | ink on it: 16.8:1 |
| `integration-ai-surface` | `color-primary` at 12% on the surface | ink on it: 12.9:1 |
| `integration-ai-edge` | `color-primary` at 55% on the surface | 4.1:1 |

Rules:

- `color-text-disabled` is **not** the quiet-state ink. It measures 3.43:1 here,
  and a closed pull request is information rather than an unavailable control, so
  quiet states take `color-secondary`. The disabled token stays on disabled
  controls, which is the one place `#contrast-rules-wcag-aa-minimum` exempts it.
- The two filled chips take `integration-edge-strong` as their boundary rather
  than a mix of their own surface: `color-warning` is 1.31:1 and `color-error`
  1.04:1 against this surface, so as boundaries they are invisible. The semantic
  tokens are backgrounds, as this file requires.
- `integration-edge` misses the 3:1 a component boundary owes its surface. That
  is the inherited `color-border` gap recorded in
  `#surface-and-border-deviation`, and it is acceptable in this family only
  because an edge is never a sole carrier: every chip also has a stroked icon at
  or above 3:1 and a text label.
- `integration-ai-edge` is the tint that says a machine wrote something. It is a
  mix rather than a new hue because the palette carries one saturated hue and
  this file forbids inventing a second semantic set to get another.

A new role token MUST be derived from a palette token. A family that needed a
value of its own would be a second palette, which
`design-principles.md#dark-mode-only` does not allow.

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

Materialized: all twenty-two palette tokens, all ten code tokens, and the five
band identity tokens. The band set adds no new colour value — each is a value one
of the other two families already carries — so it moves the count not at all;
`color-success-text` does, because `#72C086` is a value nothing else carries.
The number of distinct colours the product declares is twenty-five.

Not yet materialized: the per-stack mapping above is web-only in practice —
there is no mobile MAUI `ResourceDictionary` and no IDE webview yet, so the CSS
custom properties are currently the whole story.
