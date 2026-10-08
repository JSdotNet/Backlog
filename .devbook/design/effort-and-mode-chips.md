# Effort and Mode Chips

```meta
related: [".devbook/design/color-scheme.md#badge-and-chip-tones", ".devbook/design/color-scheme.md#contrast-rules-wcag-aa-minimum", ".devbook/design/typography-and-layout.md#border-radius-and-width", ".devbook/design/accessibility.md#contrast", ".devbook/domain/sessions/features.md"]
```

> How a stage of a delivery run shows the effort it ran at, the way it ran, and
> whether it ran as configured. Effort chips rise in emphasis along one gold
> ladder. Mode chips are told apart by shape. Two marks flag a stage that differs
> from its configuration or whose value was inferred.

These chips sit in the stage list of a delivery run in the Sessions area. A stage
row is drawn on `color-background-alt`, and the open run panel around it is drawn
on `color-background`. Every ratio in this file is measured against both surfaces.
The domain side, what a stage, a mode and an effort are, lives in
`.devbook/domain/sessions/features.md`.

## Shared anatomy

```meta
related: [".devbook/design/typography-and-layout.md#type-scale", ".devbook/design/typography-and-layout.md#spacing-scale"]
```

All chips and marks in this file share the type and box tokens below. They are
small, so they take the badge size from the type scale and never a size between
two steps.

| Property | Token |
|---|---|
| Font size | `font-size-xs` |
| Line height | `line-height-tight` |
| Default weight | `font-weight-medium` |
| Outline width | `border-width`, drawn as an inset ring so it adds nothing to the box |
| Vertical padding | `spacing-0` |

Rules:

- A chip MUST use the tokens above. It MUST NOT use `font-size-2xs`, which is
  reserved for the DEV and BETA flags.
- Every colour in this file MUST be a palette token from `color-scheme.md` or a
  `color-mix()` of two of them. A chip MUST NOT carry a literal colour.
- A chip and a mark are information, not controls. Neither is focusable on its
  own, and neither owes the 44 px target size.

## Effort chips

```meta
related: [".devbook/design/color-scheme.md#badge-and-chip-tones", ".devbook/design/color-scheme.md#chart-roles"]
```

The effort chip names the reasoning effort a stage ran at: `low`, `medium`,
`high`, `xhigh` or `max`. It sits after the model name in the stage row. Effort
rises in emphasis along one ladder of the brand gold, so a reader sees at a
glance where a run spent more.

| Effort | Ink | Fill | Outline | Weight |
|---|---|---|---|---|
| `low` | `color-text-secondary` | none | `color-border-strong` | `font-weight-medium` |
| `medium` | `color-text-secondary` | none | `color-border-strong` | `font-weight-medium` |
| `high` | `color-primary-light` | none | `color-primary-dark` | `font-weight-medium` |
| `xhigh` | `color-primary-light` | `color-mix(in srgb, color-primary 18%, color-background)` | `color-primary` | `font-weight-semibold` |
| `max` | `color-text-inverse` | `color-primary` | none, the fill is the boundary | `font-weight-semibold` |

The chip box is `border-radius-sm` with `spacing-xs` horizontal padding.

Rules:

- The chip MUST print the effort word as its text. Low and medium share one
  treatment, so the word is the only thing that tells them apart.
- The ladder MUST stay one hue. A step MUST NOT borrow a semantic surface, because
  `color-warning`, `color-error` and `color-success` mean alert, fault and settled
  on the badge tone scale.
- The xhigh tint MUST be mixed against `color-background` on both surfaces, so the
  chip is one colour wherever it sits.
- An effort value the ladder does not list MUST render verbatim with the low
  treatment and name the expected values on its title. This follows the badge rule
  that a value in no vocabulary is flagged, not styled.

**Why the ladder is not on the badge tone scale.** The tone scale in
`color-scheme.md#badge-and-chip-tones` classes a *state*, and its rule says a
filled chip is one the product acts on. Effort is a *magnitude*, so it takes an
ordinal ramp of the one saturated hue instead. That is the reasoning
`color-scheme.md#chart-roles` gives for single-hue ramps, and the reasoning the
roadmap gives for its priority shade ramp. The solid `max` chip is the top of that
ramp. It is gold rather than a semantic surface, so it cannot be read as alert,
fault or settled. The effort family MUST NOT be added to the stylesheet's badge
families.

**The outline for low and medium is `color-border-strong`, not the drawn
`color-border`.** The agreed canvas drew it in `color-border`, which measures
2.16:1 on `color-background-alt` and 2.49:1 on `color-background`. Both miss the
3:1 a component boundary owes. `color-border-strong` clears it on both surfaces.

### Effort chip contrast

```meta
```

Every ratio uses the WCAG 2.1 relative-luminance formula on the values in
`color-scheme.md`. The xhigh tint resolves to `#3A321E`. Ink owes 4.5:1 and an
outline or fill boundary owes 3:1.

| Effort | Pairing | On `color-background-alt` | On `color-background` | Owes |
|---|---|---|---|---|
| `low`, `medium` | `color-text-secondary` ink | 10.88:1 | 12.52:1 | 4.5:1 |
| `low`, `medium` | `color-border-strong` outline | 3.45:1 | 3.97:1 | 3:1 |
| `high` | `color-primary-light` ink | 11.27:1 | 12.98:1 | 4.5:1 |
| `high` | `color-primary-dark` outline | 7.25:1 | 8.34:1 | 3:1 |
| `xhigh` | `color-primary-light` ink on the tint | 8.80:1 | 8.80:1 | 4.5:1 |
| `xhigh` | `color-primary` outline | 9.68:1 | 11.15:1 | 3:1 |
| `xhigh` | `color-primary` outline vs the tint inside it | 7.56:1 | 7.56:1 | 3:1 |
| `max` | `color-text-inverse` ink on `color-primary` | 9.19:1 | 9.19:1 | 4.5:1 |
| `max` | `color-primary` fill as the boundary | 9.68:1 | 11.15:1 | 3:1 |

The binding pair is the low and medium outline on `color-background-alt`, at
3.45:1. The xhigh tint alone is 1.28:1 against the row, which is why that step
carries an outline.

### Requirement: No chip without a recorded effort

```meta
type: requirement
```

A stage SHALL show an effort chip only when the run recorded an effort for it.

#### Scenario: Effort not recorded

- **Given** a stage whose run recorded no effort
- **When** the stage list renders
- **Then** the stage row shows no effort chip
- **And** it shows no empty chip and no "unknown" chip in its place

#### Scenario: Effort recorded

- **Given** a stage whose run recorded the effort `high`
- **When** the stage list renders
- **Then** the stage row shows one effort chip reading `high` in the `high` treatment

## Mode chips

```meta
related: [".devbook/design/accessibility.md#target-sizes-and-text"]
```

The mode chip names how a stage ran. There are four modes, and each has its own
shape, so a reader who cannot tell the colours apart still tells the modes apart.

| Mode | Meaning | Shape | Ink | Fill | Edge | Radius | Weight |
|---|---|---|---|---|---|---|---|
| `inline` | Ran in the owner session | Plain text, no box | `color-secondary` | none | none | none | `font-weight-normal` |
| `delegate` | Handed to a sub-agent | Solid outline | `color-text-primary` | none | `border-width` solid `color-border-strong` | `border-radius-full` | `font-weight-normal` |
| `fork` | Ran in a fork of the owner session | Dashed outline | `color-text-primary` | none | `border-width` dashed `color-border-strong` | `border-radius-full` | `font-weight-normal` |
| `gate` | Your approval: no model runs it | Filled box | `color-text-primary` | `color-background-raised` | `border-width` solid `color-secondary` | `border-radius-sm` | `font-weight-semibold` |

Delegate and gate take `spacing-sm` horizontal padding. A dashed line cannot be
drawn as an inset ring, so the fork chip draws a real border. It takes
`calc(spacing-sm - border-width)` horizontal padding, so its box is the same size
as the delegate chip.

Rules:

- The four modes MUST differ by shape. Two modes MUST NOT share a shape and
  differ only by colour.
- A mode chip MUST print the mode word as its text.
- Delegate and fork MUST use the same edge token. The line style is what tells
  them apart, and a second colour would make it look like the difference.
- Gate is the only filled mode, because it is the one stage that waits on the
  reader. This keeps the badge rule that a filled chip is the one to look at.

### Mode chip contrast

```meta
```

| Mode | Pairing | On `color-background-alt` | On `color-background` | Owes |
|---|---|---|---|---|
| `inline` | `color-secondary` ink | 7.83:1 | 9.02:1 | 4.5:1 |
| `delegate`, `fork` | `color-text-primary` ink | 15.42:1 | 17.75:1 | 4.5:1 |
| `delegate`, `fork` | `color-border-strong` edge | 3.45:1 | 3.97:1 | 3:1 |
| `gate` | `color-text-primary` ink on `color-background-raised` | 11.58:1 | 11.58:1 | 4.5:1 |
| `gate` | `color-secondary` edge vs the surface | 7.83:1 | 9.02:1 | 3:1 |
| `gate` | `color-secondary` edge vs its own fill | 5.89:1 | 5.89:1 | 3:1 |

The gate fill alone is 1.33:1 against the row and 1.53:1 against the panel. The
`color-secondary` edge is what makes the box perceivable, so the edge MUST NOT be
dropped. The binding pair is the delegate and fork edge on `color-background-alt`,
at 3.45:1.

### Requirement: Mode is told apart by shape

```meta
type: requirement
```

Each of the four mode chips SHALL have a shape no other mode chip has, so a mode
can be read with every colour removed.

#### Scenario: Modes compared without colour

- **Given** a stage list holding one stage of each mode
- **When** the list is rendered in greyscale
- **Then** inline shows as plain text, delegate as a solid outline, fork as a dashed outline, and gate as a filled box

#### Scenario: Mode named for assistive technology

- **Given** a stage that was handed to a sub-agent
- **When** a screen reader reaches its mode chip
- **Then** it announces "delegate" with the meaning "Handed to a sub-agent"

## Stage marks

```meta
related: [".devbook/design/accessibility.md#contrast"]
```

A stage may carry one mark after its effort chip. The mark is a single bold glyph
with no box.

| Mark | Meaning | Ink | Weight |
|---|---|---|---|
| `≠` | The stage ran differently from its configuration: another agent, model or effort | `color-primary-light` | `font-weight-bold` |
| `?` | A value on the stage was inferred, not recorded by the run | `color-secondary` | `font-weight-bold` |

Rules:

- A stage MUST show at most one mark. When both apply, `≠` wins, because a stage
  that ran differently is the one a reader needs to look at.
- A stage that ran as configured, with every value recorded, MUST show no mark.
- The mark is a summary. The open stage MUST state the difference or the inference
  in words.
- `font-weight-bold` is allowed here because the mark is a glyph, not body
  emphasis.

| Mark | Pairing | On `color-background-alt` | On `color-background` | Owes |
|---|---|---|---|---|
| `≠` | `color-primary-light` ink | 11.27:1 | 12.98:1 | 4.5:1 |
| `?` | `color-secondary` ink | 7.83:1 | 9.02:1 | 4.5:1 |

## Accessible names

```meta
related: [".devbook/design/accessibility.md#screen-reader--announcements", ".devbook/design/accessibility.md#iconography-accessibility"]
```

The meaning of a chip or mark MUST NOT depend on colour or shape alone. Each one
carries a title that says what it means, and its accessible name says the same.

| Element | Title and accessible name |
|---|---|
| `inline` | "Ran in the owner session" |
| `delegate` | "Handed to a sub-agent" |
| `fork` | "Ran in a fork of the owner session" |
| `gate` | "Your approval: no model runs it" |
| `≠` | "Not as configured: " followed by each difference, for example "model Opus 5.5 → Sonnet 5.5" |
| `?` | "Inferred, not recorded by the run" |
| Effort chip | The effort word, for example "Effort: high" |

Rules:

- The `≠` title MUST list every difference, joined by " · ". A title that says only
  "not as configured" leaves the reader to open the stage to find out what.
- A glyph mark MUST NOT be announced as its glyph. A screen reader reads the title
  text in its place.

## Legend

```meta
```

A legend MUST accompany every stage list that shows a mode chip or a mark. It
shows the four modes and the two marks, in that order, each drawn exactly as it
is drawn in a row.

| Entry | Drawn as | Label |
|---|---|---|
| Inline | the inline chip | inline |
| Delegate | the delegate chip | delegate |
| Fork | the fork chip | fork |
| Gate | the gate chip | gate |
| Inferred | the `?` mark | inferred |
| Not as configured | the `≠` mark | not as configured |

Rules:

- The legend sits above the stage list, in `font-size-xs` and
  `color-text-secondary`.
- The legend MUST NOT list effort steps. The effort chip prints its own word.

## Materialization

```meta
related: [".devbook/design/README.md#living-reference-the-ui-storybook"]
```

The effort chip is materialized as `EffortChip` in
`src/Core/Backlog.UI.Components/Badges/`, drawn by the `effort-chip` rules in
`components.css`, and shown on the storybook's Session list page (`/session-list`)
beside `StageStrip`, the compact stage strip a session row draws its run with. The
Sessions pane wears it on every row after the model and among the detail panel's
facts.

The mode chips, the stage marks and the legend are not materialized yet: they belong
to the stage list of a delivery run, which is built separately.
