# Accessibility

```meta
status: active
related: [".design/color-scheme.md#contrast-rules-wcag-aa-minimum", ".design/interaction-guidelines.md", ".design/design-principles.md#keyboard-first"]
```

> Accessibility rules for the Backlog product. Target: **WCAG 2.1 Level AA** as a
> minimum across every channel (desktop — .NET MAUI Blazor Hybrid/WebView2,
> mobile — .NET MAUI native, VS Code / Visual Studio webviews). Because the
> product is dark-mode-only, keyboard-first, and has no save buttons,
> accessibility here focuses heavily on keyboard operation, save-
> state and reorder announcements, focus visibility, and reduced motion. Adapted
> from the JSdotNet design style guide (`04-motion-and-interaction`,
> `06-component-patterns`, `07-iconography`, `09-interaction-patterns`).

## Target and Scope

```meta
status: active
```

| Rule | Requirement |
|---|---|
| Conformance | Every channel MUST meet **WCAG 2.1 AA**. Primary body text targets AAA contrast (see `color-scheme.md`). |
| Platform APIs | Native channels MUST expose correct accessibility semantics via their platform API — UIA/`AutomationProperties` for native shell chrome (desktop's thin MAUI/WinUI 3 shell, WPF) and SemanticProperties (mobile MAUI native XAML); ARIA for webview-rendered content (desktop's Razor/WebView2 UI, VS Code / VS webviews). |
| Parity | Accessibility parity across channels is required; a feature usable by mouse MUST be usable by keyboard and assistive tech on every channel. |
| Testable | Each rule below is a review acceptance criterion. |

## Contrast

```meta
status: active
related: [".design/color-scheme.md#contrast-rules-wcag-aa-minimum"]
```

Contrast values and pairings are declared in
`color-scheme.md#contrast-rules-wcag-aa-minimum`. Summary of the binding minimums:

| Content | Minimum |
|---|---|
| Primary body text (`color-text-primary` on `color-background`) | 7:1 (AAA target) |
| Secondary text (`color-text-secondary`) | 4.5:1 (AA) |
| Foreground on semantic surfaces | 4.5:1 (AA) |
| Large text (≥ 24 px / ≥ 18.66 px bold) | 3:1 |
| Focus ring, control borders, icons | 3:1 (non-text) |

- `color-text-disabled` is exempt (signals unavailability).
- Color MUST NOT be the only means of conveying information/state — always pair
  with text, icon, or shape.

## Keyboard Navigation

```meta
status: active
related: [".design/design-principles.md#keyboard-first", ".design/interaction-guidelines.md#keyboard-accessible-reordering"]
```

| Rule | Requirement |
|---|---|
| Full operability | Every interactive element and command MUST be operable by keyboard alone, with no keyboard traps (except intentional modal focus traps that release on close). |
| Logical order | Tab order MUST follow visual/reading order. |
| Standard keys | Activation via Enter/Space; Escape closes menus/dialogs and cancels an in-progress reorder; arrow keys navigate within composite widgets (menus, tabs, lists, slash menu). |
| Reorder without a mouse | Reordering items and chapters MUST be fully possible via keyboard (grab/move model + explicit Move up/down/top/bottom commands) — see `interaction-guidelines.md#keyboard-accessible-reordering`. |
| Editing without a mouse | The Markdown editor, slash menu, raw-Markdown toggle, and all block insertions MUST be keyboard-operable (see `content-editing.md`). |
| Shortcuts discoverable | Keyboard shortcuts MUST be discoverable via the command palette and documented; they MUST NOT clobber platform/assistive-tech shortcuts. |
| Non-native controls | Elements not natively focusable that become interactive MUST get `tabindex="0"` (or platform equivalent) and explicit focus styles. |

## Screen Reader / Announcements

```meta
status: active
related: [".design/interaction-guidelines.md#save-state-indicator-vocabulary", ".design/interaction-guidelines.md#drag-and-drop-reordering"]
```

| Rule | Requirement |
|---|---|
| Names & roles | Every control MUST expose an accessible name, role, and state to the platform a11y API (ARIA / UIA / SemanticProperties). Icon-only controls MUST have an explicit label (see `#iconography-accessibility`). |
| Live regions | Transient status MUST be announced via an appropriate live region without moving focus. |

### Save-State Announcements

The save-state indicator is not a button, so it MUST be announced:

| State | Announcement | Politeness |
|---|---|---|
| Saving | "Saving" (announce sparingly; debounce so rapid edits don't spam) | polite (`role="status"`) |
| Saved | "All changes saved" | polite |
| Offline | "Offline. Changes are saved locally and will sync when you reconnect." | polite |
| Conflict | "A newer version was saved. Your change was reconciled." | assertive (`role="alert"`) |
| Error | "Couldn't save your last change. Retrying." + focusable retry | assertive (`role="alert"`) |

Rules: routine `Saving`/`Saved` transitions MUST be throttled so screen readers
are not flooded during continuous typing.

### Reorder Announcements

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

| Rule | Requirement |
|---|---|
| Block changes | Applying a block type (heading, list, code) via slash/autoformat SHOULD announce the resulting block type. |
| Validation/errors | Inline errors MUST be linked via `aria-describedby` and announced (see `interaction-guidelines.md#error-states`). |
| Unsupported blocks | Raw passthrough blocks MUST be labelled as raw Markdown so users know they are editing source. |

## Focus Visibility

```meta
status: active
related: [".design/interaction-guidelines.md#focus-and-selection"]
```

| Rule | Requirement |
|---|---|
| Always visible | Keyboard focus MUST always be visible; `outline: none` (or platform equivalent) without a compliant replacement is prohibited. |
| Style | Focus uses `color-border-focus` outline at `border-width-2` with a 2 px offset; use `outline` (not shadow-only) so it survives Windows High Contrast. |
| Contrast | The focus indicator MUST meet 3:1 against adjacent surfaces. |
| Restore | Closing a modal/drawer MUST return focus to the triggering control; after a keyboard reorder, focus MUST stay on the moved item's handle. |
| Distinct states | Focus, hover, and selection MUST be visually distinguishable. |

## Reduced Motion

```meta
status: active
related: [".design/interaction-guidelines.md#motion-and-reduced-motion"]
```

| Rule | Requirement |
|---|---|
| Honor preference | The product MUST honor `prefers-reduced-motion` (and the platform equivalent on WinUI/MAUI). |
| Degrade gracefully | Under reduced motion: no translate/scale/rotate, no slide-ins, no parallax; use instant changes or opacity-only fades; spinners show a static/indeterminate state; skeleton pulse is disabled. |
| Function preserved | Reduced motion MUST NOT remove functionality — drag autoscroll, reorder, and save feedback still work, just without decorative motion. |

## Target Sizes and Text

```meta
status: active
related: [".design/typography-and-layout.md#iconography"]
```

| Rule | Requirement |
|---|---|
| Touch/click targets | Interactive targets MUST be ≥ 44 × 44 px (including icon-only buttons and drag handles), even when the visual glyph is smaller. |
| Spacing | Adjacent targets MUST have enough spacing to prevent mis-activation (use the spacing scale). |
| Minimum text | Readable text MUST NOT be smaller than `font-size-sm` (14 px). |
| Respect user scale | Use scale tokens (rem / device-independent units) so OS/browser font-size and zoom settings are respected; layouts MUST reflow without loss of content up to 200% zoom. |
| Not meaning by style alone | Do not convey meaning by weight, italic, or color alone — pair with text or icon. |

## Iconography Accessibility

```meta
status: active
related: [".design/typography-and-layout.md#iconography"]
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
status: active
related: [".design/component-libraries.md", ".arc42/04-solution-strategy.md#technology-choices"]
```

| Channel | Accessibility notes |
|---|---|
| Desktop — .NET MAUI Blazor Hybrid | UI is Razor in WebView2 (ADR 0001): use ARIA for the app content, same as the IDE webviews; `AutomationProperties` still applies to the thin native shell/window chrome; verify with Accessibility Insights; ensure High Contrast rendering keeps focus visible (outline-based). |
| Mobile — .NET MAUI (native) | Use `SemanticProperties`; support platform screen readers (Narrator/VoiceOver/TalkBack) and OS text-scaling; ensure single-column reorder is keyboard/switch accessible. |
| IDE webviews (VS Code / VS) | Use ARIA; announce via `aria-live`; respect the host's reduced-motion and high-contrast signals but keep product tokens for content contrast; keep focus visible against the webview surface. |

`[TODO: clarify]` whether a formal accessibility audit / VPAT is required per
release, and which assistive technologies are in the supported test matrix.

## Materialization

```meta
status: active
related: [".design/README.md#living-reference-the-ui-storybook", ".design/interaction-guidelines.md#materialization"]
```

The storybook is the review surface: every component renders there in isolation
with the semantics it ships with, so roles, labels, focus order and focus
visibility can be checked one component at a time rather than only inside a
screen.

What holds today:

| Rule | Materialized as |
|---|---|
| Names & roles | Composite widgets declare theirs — `role="tree"`/`treeitem` (TreeView), `role="tablist"`/`tab` (Tabs), `role="menu"`/`menuitem` (MenuList), `role="switch"` (Toggle, because a checkbox role would announce the wrong control), `role="separator"` (SplitPane resizer), `role="search"` (SearchBox), `role="region"` with a label (FileView, CodeView, GraphView) |
| Live regions | `SaveIndicator` is `role="status"` + `aria-live="polite"`; `ToastHost` is one polite live region for the page, with warnings and errors raised to `role="alert"` inside it; `CodeView`'s copy result is a `role="status"` line rather than a changed button label |
| Focus visibility | Every interactive component sets its own `:focus-visible` outline in `color-border-focus` at `border-width-2` with a 2 px offset — `outline`, never a shadow |
| Non-native focus targets | Scrollable regions that take focus say so and show it (`FileView` body, `CodeView` body) |
| Reduced motion | Honored per component: the fold chevron and toggle drop their transitions, the spinner stops turning and becomes an opacity-only pulse, the skeleton stops altogether rather than substituting one, graph cards keep colour transitions only |
| Not by colour alone | Save state carries text, not just a dot; badges carry their label; the inert-link style is a dotted underline as well as a colour |

Gaps, tracked rather than assumed:

- **`color-border` misses the 3:1 this file asks of control boundaries** — it
  measures 2.49:1 against the base surface. It failed under the org guide's own
  values too (2.36:1), so it is inherited rather than introduced; the numbers and
  a candidate replacement are in
  `color-scheme.md#surface-and-border-deviation`.
- **No reorder announcements.** There is no `aria-live` region anywhere in the
  desktop app, so `#reorder-announcements` is entirely unimplemented. Keyboard
  reorder works and each grip is labelled with how to use it; the position
  feedback is the missing half.
- **No icon set**, so `#iconography-accessibility` has almost nothing to govern
  yet — the product draws its few glyphs in CSS or borrows an emoji. See
  `typography-and-layout.md#materialization`.
- **Target sizes are unverified.** The ≥ 44 × 44 px minimum is not asserted by
  any test, and dense rows are exactly where it tends to fail.
- **Only one channel exists.** Every rule above is checked in the web-rendered
  surface. Mobile MAUI's `SemanticProperties` and the IDE webviews have no
  implementation to check.
