# Accessibility

```meta
related: [".devbook/design/color-scheme.md#contrast-rules-wcag-aa-minimum", ".devbook/design/interaction-guidelines.md", ".devbook/design/design-principles.md#keyboard-first"]
```

> Target: **WCAG 2.1 Level AA** as a minimum across every channel: the desktop
> and mobile .NET MAUI Blazor Hybrid apps, and the VS Code and Visual Studio
> webviews. The product is dark-mode-only, keyboard-first, and has no save
> buttons. Adapted from the JSdotNet design style guide
> (`04-motion-and-interaction`, `06-component-patterns`, `07-iconography`,
> `09-interaction-patterns`).

## Target and Scope

```meta
```

| Rule | Requirement |
|---|---|
| Conformance | Every channel MUST meet **WCAG 2.1 AA**. Primary body text targets AAA contrast (see `color-scheme.md`). |
| Platform APIs | Native channels MUST expose correct accessibility semantics via their platform API; webview content uses ARIA. |
| Parity | A feature usable by mouse MUST be usable by keyboard and assistive tech on every channel. |
| Testable | Each rule below is a review acceptance criterion. |

**Platform APIs** map to channels as follows. UIA/`AutomationProperties` covers
native shell chrome (desktop's thin MAUI/WinUI 3 shell, and WPF).
SemanticProperties covers any native MAUI XAML on mobile. ARIA covers
webview-rendered content (the desktop's and the mobile head's Razor UI, and VS
Code / VS webviews).

## Contrast

```meta
related: [".devbook/design/color-scheme.md#contrast-rules-wcag-aa-minimum"]
```

The contrast minimums for every pairing are declared in
`color-scheme.md#contrast-rules-wcag-aa-minimum`, and only there. Icon contrast
is in `#iconography-accessibility`.

- `color-text-disabled` is exempt (signals unavailability).
- Color MUST NOT be the only means of conveying information/state — always pair
  with text, icon, or shape.

## Keyboard Navigation

```meta
related: [".devbook/design/design-principles.md#keyboard-first", ".devbook/design/interaction-guidelines.md#keyboard-accessible-reordering"]
```

| Rule | Requirement |
|---|---|
| Full operability | Every interactive element and command MUST be operable by keyboard alone, with no keyboard traps. |
| Logical order | Tab order MUST follow visual/reading order. |
| Standard keys | Enter/Space activate; Escape closes menus/dialogs and cancels a reorder; arrow keys navigate composite widgets. |
| Reorder without a mouse | Reordering items and chapters MUST be fully possible via keyboard (grab/move model + explicit Move up/down/top/bottom commands) — see `interaction-guidelines.md#keyboard-accessible-reordering`. |
| Editing without a mouse | The Markdown editor, slash menu, raw-Markdown toggle, and all block insertions MUST be keyboard-operable (see `content-editing.md`). |
| Shortcuts discoverable | Keyboard shortcuts MUST be discoverable via the command palette and documented; they MUST NOT clobber platform/assistive-tech shortcuts. |
| Non-native controls | Elements not natively focusable that become interactive MUST get `tabindex="0"` (or platform equivalent) and explicit focus styles. |

Intentional modal focus traps that release on close are the one exception to
*Full operability*. Composite widgets are menus, tabs, lists and the slash menu.

### Sync Direction Arrow Pad

```meta
related: [".devbook/domain/devbook/features.md#sync-verdicts-beside-a-chapter", ".devbook/design/accessibility.md#target-sizes-and-text"]
```

The editable `sync` direction of a devbook block is an arrow pad: four keys
around a centre naming the direction in force, each key where its arrow points.
The pad is one tab stop, roving: Tab reaches the key for what the block states,
or the centre when it states `sync`, `off` or nothing, and leaves the pad on the
next press.

| Key | States | Announced as |
|---|---|---|
| ArrowUp | `report` | "Report (↑)" |
| ArrowLeft | `pull` | "Pull (←)" |
| ArrowRight | `push` | "Push (→)" |
| ArrowDown | nothing — the block inherits | "Inherit (↓)" |
| Enter / Space on the centre | opens a menu: `sync`, `off`, and `inherit` when the block states a direction | — |

- The arrows work from any part of the pad and take the focus, and the tab
  stop, to the key they reached.
- A held arrow states once: repeats are ignored, because each change writes a
  file.
- An arrow held with Alt, Ctrl, Meta or Shift is not the pad's and passes
  through untouched. Alt+ArrowLeft stays the browser's Back.
- Each key carries `aria-keyshortcuts` and `aria-pressed`; the pad is
  `role="group"` named by its label.

### Requirement: Sync direction by arrow key

```meta
type: requirement
```

The sync arrow pad SHALL state a direction from its arrow key once per press,
and SHALL leave a repeated or modified arrow alone.

#### Scenario: An arrow states its direction

- **Given** a block that states no direction and the pad's centre has focus
- **When** the person presses ArrowRight
- **Then** the block states `push`
- **And** the focus and the tab stop move to the right key

#### Scenario: A held arrow states once

- **Given** the pad has focus
- **When** the person holds ArrowUp until it repeats
- **Then** the block states `report` once

#### Scenario: A modified arrow is not the pad's

- **Given** the pad has focus
- **When** the person presses Alt+ArrowLeft
- **Then** the block's direction is unchanged

### Roving Button Groups

```meta
related: [".devbook/domain/devbook/requirements.md#requirement-clusters-opens-on-the-selections-layer", ".devbook/design/accessibility.md#target-sizes-and-text"]
```

An element marked `data-roving-buttons` makes its direct-child buttons one tab
stop. The shared script in `components.js` moves the stop among them, so every
host gets the same keys without writing its own handler. The first host is the
Technology Clusters map: each cluster is one group of its open-layer button and
its dots.

| Key | Moves the focus and the stop to |
|---|---|
| ArrowRight / ArrowDown | the next button, wrapping from the last to the first |
| ArrowLeft / ArrowUp | the previous button, wrapping from the first to the last |
| Home | the first button |
| End | the last button |
| Enter / Space | nothing: the button's own click runs |

- The host renders the starting stop: `tabindex="0"` on one button and `"-1"` on
  the rest. Tab enters the group on that button and leaves it on the next press.
- A disabled button is skipped. A key held with Alt, Ctrl, Meta or Shift is not
  the group's and passes through untouched.
- The script refuses the default of each handled key, so the arrows do not
  scroll the page and Home and End do not jump it to an end. It changes nothing
  else the keys do.
- A group always keeps exactly one stop. When a re-render removes or disables the
  stop's button, the script gives the stop back to the first enabled button. When
  a group ends up with two stops, it keeps the focused one, else the first.

## Screen Reader / Announcements

```meta
related: [".devbook/design/interaction-guidelines.md#save-state-indicator-vocabulary", ".devbook/design/interaction-guidelines.md#drag-and-drop-reordering"]
```

| Rule | Requirement |
|---|---|
| Names & roles | Every control MUST expose an accessible name, role and state to the platform a11y API. |
| Live regions | Transient status MUST be announced via an appropriate live region without moving focus. |

The platform a11y API is ARIA, UIA or SemanticProperties. Icon-only controls MUST
have an explicit label (see `#iconography-accessibility`).

### Save-State Announcements

```meta
```

The save-state indicator is not a button, so it MUST be announced:

| State | Announcement | Politeness |
|---|---|---|
| Saving | "Saving" | polite (`role="status"`) |
| Saved | "All changes saved" | polite |
| Offline | "Offline. Changes are saved locally and will sync when you reconnect." | polite |
| Conflict | "A newer version was saved. Your change was reconciled." | assertive (`role="alert"`) |
| Error | "Couldn't save your last change. Retrying." + focusable retry | assertive (`role="alert"`) |

Routine `Saving` and `Saved` announcements MUST be throttled, so a screen reader
is not flooded while someone types continuously.

### Reorder Announcements

```meta
```

| Event | Announcement |
|---|---|
| Pick up | "Grabbed [item name]. Use arrow keys to move, Space to drop, Escape to cancel." |
| Move | "Moved to position 3 of 8." (position + total) |
| Indent (chapter) | "Now at heading level 3." |
| Drop | "Dropped [item name] at position 3 of 8." |
| Cancel | "Move cancelled. [item name] returned to position 5." |

Reorder announcements use an `aria-live` region (assertive during an active
grab).

### Editor Announcements

```meta
```

| Rule | Requirement |
|---|---|
| Block changes | Applying a block type (heading, list, code) via slash/autoformat SHOULD announce the resulting block type. |
| Validation/errors | Inline errors MUST be linked via `aria-describedby` and announced (see `interaction-guidelines.md#error-states`). |
| Unsupported blocks | Raw passthrough blocks MUST be labelled as raw Markdown so users know they are editing source. |

## Focus Visibility

```meta
related: [".devbook/design/interaction-guidelines.md#focus-and-selection"]
```

| Rule | Requirement |
|---|---|
| Always visible | Keyboard focus MUST always be visible; `outline: none` (or platform equivalent) without a compliant replacement is prohibited. |
| Style | Focus uses a `color-border-focus` outline at `border-width-2` with a 2 px offset. |
| Contrast | The focus indicator MUST meet 3:1 against adjacent surfaces. |
| Restore | Closing a modal/drawer MUST return focus to the triggering control. |
| Distinct states | Focus, hover, and selection MUST be visually distinguishable. |

Use `outline`, not shadow-only, so focus survives Windows High Contrast. After a
keyboard reorder, focus MUST stay on the moved item's handle.

## Reduced Motion

```meta
related: [".devbook/design/interaction-guidelines.md#motion-and-reduced-motion"]
```

| Rule | Requirement |
|---|---|
| Honor preference | The product MUST honor `prefers-reduced-motion` (and the platform equivalent on WinUI/MAUI). |
| Degrade gracefully | Under reduced motion, use instant changes or opacity-only fades. |
| Function preserved | Reduced motion MUST NOT remove functionality — drag autoscroll, reorder, and save feedback still work, just without decorative motion. |

Slide-ins and parallax stop, and so do translate, scale and rotate effects.
Spinners show a static or indeterminate state, and the skeleton pulse is
disabled.

## Target Sizes and Text

```meta
related: [".devbook/design/typography-and-layout.md#iconography"]
```

| Rule | Requirement |
|---|---|
| Touch/click targets | Interactive targets MUST be ≥ 44 × 44 px, even when the visual glyph is smaller. |
| Spacing | Adjacent targets MUST have enough spacing to prevent mis-activation (use the spacing scale). |
| Minimum text | Readable text MUST NOT be smaller than `font-size-sm` (14 px). |
| Respect user scale | Use scale tokens (rem / device-independent units); layouts MUST reflow without loss of content up to 200% zoom. |
| Not meaning by style alone | Do not convey meaning by weight, italic, or color alone — pair with text or icon. |

The target minimum includes icon-only buttons and drag handles. Scale tokens let
OS and browser font-size and zoom settings take effect.

**Exception — the sync direction arrow pad.** Its four keys are 24 × 24 px
(1.5rem, WCAG 2.2's 2.5.8 AA minimum), not 44 × 44 px. Each key's hit area
reaches a further `spacing-sm` outward in the direction of its arrow. It never
reaches inward or sideways, so a loose press on one key cannot land on the
centre or a neighbour.

The exception holds because the pad is an inline control beside a
block's status, and three rows of 44 px keys would stand far taller than the
heading the pad sits on. It is bounded:

- Every direction stays reachable without a pointer, per
  `#sync-direction-arrow-pad`, and `sync` and `off` through the centre's menu.
- The centre is a badge-sized button, and the same exception covers it.
- The exception covers this pad only. Any other control below 44 × 44 px needs
  its own exception here.

**Exception — the dots on the Technology Clusters map.** On All layers each
technology is a dot button that opens its layer on it. A dot's press area is its
own cell in the cluster's grid, and the dot is drawn at 0.8 of that cell. The
cell is at most 2% of the map's width, and smaller in a crowded cluster. Press
areas tile the cluster and never overlap a neighbour's.

The exception holds under WCAG 2.2's 2.5.8 equivalent-control clause, because
the side panel offers the same action at full size. It is bounded:

- The side panel's layer list opens any layer, and its technology list picks any
  technology in the open layer.
- Every dot is reachable without a pointer, through its cluster's
  [roving button group](#roving-button-groups).
- The exception covers these dots only. The cluster circles, pills and
  satellites on the same map are not covered by it.

**Exception — the drag grips.** A grip that is an item's only press origin, as
on a goal card or a No goal row, is 1.5rem (24 px) wide, WCAG 2.2's 2.5.8 AA
minimum, and 2.75rem (44 px) tall. A wider grip would take the row's width from
its title on every row of a dense list. The exception holds because the grip is
the pointer's shortcut for a move the row already offers. It is bounded:

- Every move a grip makes is reachable without it: a goal through **Move up**
  and **Move down**, a No goal row through **Attach…**
  (`interaction-guidelines.md#drag-affordances`).
- A task row's grip needs no exception: the whole row is its press origin.
- The exception covers drag grips only, at no less than 24 × 44 px.

## Iconography Accessibility

```meta
related: [".devbook/design/typography-and-layout.md#iconography"]
```

| Element | Requirement |
|---|---|
| Icon-only button/link | MUST have an accessible label (`aria-label` / AutomationProperties.Name / SemanticProperties.Description). |
| Standalone meaningful icon | Provide a text alternative (`role="img"` + title, or platform equivalent). |
| Decorative icon | MUST be hidden from assistive tech (`aria-hidden="true"` + `focusable="false"`, or platform equivalent). |
| Icon + visible label | Hide the icon from AT so the label is not read twice. |
| Color | Icon color MUST meet 3:1 contrast and MUST NOT be the sole carrier of meaning. |

## Per-Channel Notes

```meta
related: [".devbook/design/component-libraries.md", ".devbook/arc42/04-solution-strategy.md#technology-choices"]
```

| Channel | Accessibility notes |
|---|---|
| Desktop — .NET MAUI Blazor Hybrid | UI is Razor in WebView2 (ADR 0001): use ARIA for the app content, same as the IDE webviews. |
| Mobile — .NET MAUI Blazor Hybrid | UI is Razor in a `BlazorWebView`: use ARIA for the app content, as on desktop. |
| IDE webviews (VS Code / VS) | Use ARIA; announce via `aria-live`; keep focus visible against the webview surface. |

- **Desktop:** `AutomationProperties` still applies to the thin native
  shell/window chrome. Verify with Accessibility Insights, and keep focus visible
  (outline-based) in High Contrast.
- **Mobile:** `SemanticProperties` applies only to native MAUI XAML. Support
  Narrator/VoiceOver/TalkBack and OS text-scaling, and keep single-column
  reorder keyboard/switch accessible.
- **IDE webviews:** respect the host's reduced-motion and high-contrast signals,
  but keep product tokens for content contrast.

`[TODO: clarify]` whether a formal accessibility audit / VPAT is required per
release, and which assistive technologies are in the supported test matrix.

## Materialization

```meta
related: [".devbook/design/README.md#living-reference-the-ui-storybook", ".devbook/design/interaction-guidelines.md#materialization", ".devbook/design/interaction-guidelines.md#task-rows"]
```

The storybook is the review surface. Every component renders there in isolation
with the semantics it ships with. Roles, labels, focus order and focus
visibility can therefore be checked one component at a time.

What holds today:

| Rule | Materialized as |
|---|---|
| Names & roles | Composite widgets declare their roles. |
| Live regions | Each transient status has its live region. |
| Focus visibility | Every interactive component sets its own `:focus-visible` outline. |
| Non-native focus targets | Scrollable regions that take focus say so and show it (`FileView` body, `CodeView` body) |
| Reduced motion | Honored per component. |
| Not by colour alone | Each state carries more than colour. |

**Names & roles** by widget:

- `role="tree"`/`treeitem` (TreeView)
- `role="tablist"`/`tab` (Tabs)
- `role="menu"`/`menuitem` (MenuList)
- `role="switch"` (Toggle, because a checkbox role would announce the wrong
  control)
- `role="separator"` (SplitPane resizer)
- `role="search"` (SearchBox)
- `role="region"` with a label (FileView, CodeView, GraphView)

**Live regions** by component:

- `SaveIndicator` is `role="status"` + `aria-live="polite"`. `ToastHost` is one
  polite live region for the page, with warnings and errors raised to
  `role="alert"` inside it.
- `CodeView`'s copy result is a `role="status"` line rather than a changed
  button label. `CopyButton` draws it as a glyph as well, because a task row and
  the task panel hide the line and keep only its announcement.
- `TaskListView` and `RoadmapTimeline` each carry one for reorder, per
  `#reorder-announcements`.

**Reduced motion** by component: the fold chevron and toggle drop their
transitions, and the spinner stops turning and becomes an opacity-only pulse.
The skeleton stops altogether rather than substituting one, graph cards keep
colour transitions only, and the copy confirmation keeps its cross-fade but
stops scaling.

**Not by colour alone:** save state carries text, not just a dot, and badges
carry their label. The inert-link style is a dotted underline as well as a
colour. An address in a task title, which a pointer follows though it is no
anchor, wears the link colour, the same dotted underline and a pointer cursor.
It keeps them on a finished task, whose title colour it takes.

Gaps, tracked rather than assumed:

- **`color-border` misses the 3:1 this file asks of control boundaries:** it
  measures 2.49:1 against the base surface, inherited from the org guide's
  2.36:1. See `color-scheme.md#surface-and-border-deviation`.
- **Reorder announces, but politely, and only where the gesture exists.** Item
  reorder carries the live region `#reorder-announcements` asks for, in both
  library implementations. Two things still fall short:
  - The regions are `polite`, where the section asks for assertive during an
    active grab.
  - Reordering *chapters within a document* is unbuilt altogether
    (`interaction-guidelines.md#materialization`), so the rule is unmet for want
    of a gesture, not a region. That includes the indent announcement.

  The pointer grips are `aria-hidden` and take no tab stop, deliberately: the
  keyboard route is `Alt+Arrow` on the row itself.
- **The icon set is small.** `src/Core/Backlog.UI.Components/Icons/` holds a set
  of Lucide-idiom outline glyphs, each hidden from assistive technology. There
  are no icon-size tokens, the status icons the save-state vocabulary names are
  not in the set, and some metadata-line glyphs are still emoji. See
  `typography-and-layout.md#materialization`.
- **Target sizes are unverified.** No test asserts the ≥ 44 × 44 px minimum, and
  dense rows are exactly where it tends to fail. The sync arrow pad's 24 px keys,
  the Clusters map's dots and the 24 × 44 px drag grips are the three recorded
  exceptions (`#target-sizes-and-text`), not gaps.
- **Both app channels are web-rendered.** The desktop and the Android head each
  host Razor components in a WebView (the phone's from `Backlog.Mobile.UI` over
  the shared library), so every rule above applies as written and MAUI's
  `SemanticProperties` has nothing to govern. The IDE webviews have no
  implementation to check.
