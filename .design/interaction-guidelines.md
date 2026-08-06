# Interaction Guidelines

```meta
status: active
related: [".design/design-principles.md#no-save-buttons--auto-save-everywhere", ".design/accessibility.md", ".arc42/08-crosscutting-concepts.md#storage-and-sync"]
```

> Binding interaction rules for the Backlog product: auto-save (there are no save
> buttons), drag-and-drop reordering of both items and chapters with mandatory
> keyboard equivalents, feedback/toasts, motion and reduced-motion, and the
> empty/loading/error state patterns. Motion tokens and interaction patterns are
> sourced from the `jsdotnet-project-design` MCP guides `04-motion-and-interaction`
> and `09-interaction-patterns`; conflict handling aligns with the last-write-wins
> rule in `.arc42/08-crosscutting-concepts.md#storage-and-sync`. Token names are
> declared in `color-scheme.md` and `typography-and-layout.md`.

## Auto-Save (No Save Buttons)

```meta
status: active
related: [".design/design-principles.md#no-save-buttons--auto-save-everywhere", ".arc42/08-crosscutting-concepts.md#storage-and-sync"]
```

There is **no manual save anywhere in the product**. Every edit persists
automatically to the local canonical store first (local-first), then syncs.

### Save Timing and Debounce

| Rule | Requirement |
|---|---|
| No save affordance | There MUST NOT be a Save button, menu item, or save-only keyboard gesture as the means of persistence. |
| Text debounce | Continuous text edits MUST auto-save on a **debounce of 500 ms–1000 ms** after the last keystroke (recommended default 750 ms). |
| Idle/blur flush | A pending save MUST be flushed immediately on field/editor **blur**, on navigation away, and on app background/close. |
| Discrete actions | Discrete changes (reorder, toggle, checkbox, metadata edit, drop) MUST save **immediately** (no debounce). |
| Max in-flight latency | If a save has not been acknowledged within ~5 s, the indicator MUST move from `Saving` toward `Offline`/retry rather than appear stuck. |
| Local-first ordering | Persistence order is: update UI (optimistic) → write local canonical Markdown → background sync. Editing MUST NOT wait on sync. |

### Optimistic UI

| Rule | Requirement |
|---|---|
| Immediate reflection | The UI MUST reflect the change instantly, before persistence completes. |
| Non-blocking | Auto-save MUST NOT block typing, navigation, or further edits. |
| Rollback on failure | If a local write genuinely fails, the change MUST be visibly rolled back or flagged (never silently dropped) and surfaced via an error toast. |

### Save-State Indicator Vocabulary

A single, always-visible save-state indicator communicates persistence. The
vocabulary is **fixed and identical across all channels**.

| State | Label | Icon | Surface / color | Meaning |
|---|---|---|---|---|
| Idle / saved | `Saved` | `check-circle` | `color-text-secondary` on base surface | All changes persisted locally (and synced if online). |
| Saving | `Saving…` | `loader-2` (spin; static under reduced motion) | `color-text-secondary` | A write/sync is in flight. |
| Offline | `Offline — changes saved locally` | `cloud-off` | `color-info` surface | No connectivity; edits are safe locally and will sync on reconnect. |
| Conflict | `Conflict resolved` / `Review conflict` | `alert-triangle` | `color-warning` surface | A concurrent edit was reconciled (see conflict handling). |
| Error | `Couldn't save` + retry | `x-circle` | `color-error` surface | A local write failed; retry offered. |

Rules:

- The indicator MUST be visible without user action and MUST NOT be a button
  that triggers saving.
- `Saved` is the resting state; it MUST NOT nag (no persistent green banner) —
  it settles to a quiet `color-text-secondary` state.
- State transitions use motion per `#motion-and-reduced-motion`.
- Screen-reader announcements for save state are specified in
  `accessibility.md#screen-reader-announcements`.

### Undo and History

| Rule | Requirement |
|---|---|
| Undo always available | Because there is no save gate, **undo/redo** (Ctrl/Cmd+Z, Ctrl/Cmd+Shift+Z) MUST be available for content edits and for reorder actions. |
| Reorder is undoable | A drag-and-drop or keyboard reorder MUST be a single undoable step that restores the previous order. |
| History granularity | Undo history SHOULD be per-document and coalesce rapid keystrokes into sensible steps. |
| AI edits reversible | AI-applied changes MUST be undoable like any other edit (see `design-principles.md#ai-first-surfaces`). |
| Version history (optional) | Longer-term version/history browsing is a product feature `[TODO: clarify]`; at minimum session-level undo MUST exist. |

### Conflict Handling

Aligns with the architectural rule: **new items always create; edits are
last-write-wins** (`.arc42/08-crosscutting-concepts.md#storage-and-sync`).

| Rule | Requirement |
|---|---|
| New items | Concurrent creation MUST NOT conflict — both items are created (always-create). |
| Edits | Concurrent edits to the same item resolve by **last-write-wins**; the most recent write is canonical. |
| Non-destructive surfacing | When last-write-wins discards a local change, the UI MUST surface the `Conflict` state rather than silently losing edits, and SHOULD offer access to the superseded version where available. |
| No blocking merge UI | Conflict handling MUST NOT block editing with a mandatory merge dialog; resolution is automatic, notification is passive. |
| Reorder conflicts | Concurrent reorders resolve by last-write-wins on the ordering metadata; the losing client re-renders to the winning order and MAY show the `Conflict` state. |

## Drag-and-Drop Reordering

```meta
status: active
related: [".design/accessibility.md#keyboard-navigation", ".design/design-principles.md#keyboard-first"]
```

The product supports reordering of **two distinct things**, both with the same
affordance language and both with mandatory keyboard equivalents:

1. **Items** — files/entries in a list (e.g. backlog entries, inbox items,
   knowledge notes).
2. **Chapters** — headings/sections *within a document* (reordering a `##`
   section moves it and all its nested content).

### Drag Affordances

| Rule | Requirement |
|---|---|
| Visible handle | Each reorderable row/section MUST expose a drag handle using the `grip-vertical` icon at `icon-md`. On dense rows the handle MAY appear on hover/focus but MUST be reachable by keyboard. |
| Cursor | Pointer over a handle uses a grab/grabbing cursor. |
| Lift feedback | On drag start, the dragged item lifts with `shadow-lg` and a subtle `color-background-alt` tint; the rest dims slightly. |
| Handle target | The handle MUST meet the ≥ 44 × 44 px target (see `accessibility.md#target-sizes-and-text`). |

### Drop Indicators

| Rule | Requirement |
|---|---|
| Placeholder | During drag, a placeholder/insertion line MUST show the exact drop position, drawn in `color-primary` at `border-width-2`. |
| Valid vs invalid | Valid drop targets highlight; invalid targets MUST show a not-allowed cursor and MUST NOT show an insertion line. |
| Nesting cue | For chapters, horizontal indentation of the insertion line MUST indicate the resulting heading depth (see nesting rules). |

### Reliable Drop Targets (Items)

> The drag handle and the drop target are not the same hit area. A handle
> sized for a dense row (`#drag-affordances`) is reliably graspable but too
> small to reliably *catch* a drop — the drop target must be sized for
> releasing, not for picking up.

| Rule | Requirement |
|---|---|
| Handle starts the drag only | The visible handle is the pointer-down / `dragstart` origin; it is not required to also be the drop target. |
| Drop target covers the row | While a drag is in progress, each candidate row/card MUST expose a drop target spanning its own full width, split into a "before" and "after" half by height — releasing anywhere over the row commits to the nearer half, not just to the handle's footprint. |
| Cancel both dragenter and dragover | A drop target MUST prevent default on both `dragenter` and `dragover`. A `drop` only fires where the immediately preceding `dragover` was cancelled, and a target that only appears once dragging has started needs its `dragenter` cancelled too, or the first `dragover` over it can be missed. |
| Handle target size still applies | The ≥ 44 × 44 px minimum in `#drag-affordances` (see `accessibility.md#target-sizes-and-text`) governs the handle as an independent pointer/keyboard-focus target, regardless of how large the drop target is. |

### Keyboard-Accessible Reordering

Drag-and-drop alone is **not accessible**. A keyboard path is **mandatory** for
both items and chapters.

| Rule | Requirement |
|---|---|
| Grab/move model | Focus the handle, press **Space/Enter** to "pick up", **Arrow Up/Down** to move, **Space/Enter** to drop, **Escape** to cancel and restore. |
| Explicit move commands | Additionally provide **Move up / Move down** (and **Move to top / bottom**) commands via the item's context menu and the command palette, mapped to keyboard shortcuts. |
| Chapter indent | For chapters, **Arrow Left/Right** (or Tab/Shift+Tab while picked up) MUST change nesting depth within the allowed range. |
| Announcements | Every keyboard move MUST announce the new position via a live region (see `accessibility.md#screen-reader-announcements`), e.g. "Moved to position 3 of 8." |
| Parity | Anything achievable by dragging MUST be achievable by keyboard, including cross-container moves. |

### Autoscroll

| Rule | Requirement |
|---|---|
| Edge autoscroll | Dragging near the top/bottom edge of a scrollable container MUST autoscroll toward that edge at a bounded speed. |
| Reduced motion | Autoscroll MUST remain functional but MUST NOT add parallax/decorative motion under `prefers-reduced-motion`. |
| Keyboard scroll | Keyboard moves MUST keep the moving item scrolled into view. |

### Cross-Container Moves

| Rule | Requirement |
|---|---|
| Item cross-move | Where the domain allows it, items MAY be dragged between containers (e.g. inbox → backlog list). Invalid cross-moves MUST be rejected with a not-allowed cue, not a silent no-op. |
| Chapter scope | Chapter reordering is scoped to **within a single document** by default; moving a section to another document is a distinct action and MUST be explicit (command), not an accidental drag. |
| Keyboard cross-move | Cross-container moves MUST also be possible via the keyboard "move to…" command. |

### Nesting / Indent Rules (Chapters)

| Rule | Requirement |
|---|---|
| Depth reflects headings | A chapter's nesting depth maps to Markdown heading level (`#`=1 … `######`=6). Reordering/indenting MUST update the heading level of the moved section and MUST keep child sections' relative depth. |
| Legal depth only | Indent MUST NOT produce an illegal jump (e.g. an `##` cannot become `####` in one step without intermediate parent); clamp to a legal level and reflect it in the drop indicator. |
| Content preserved | Moving/indenting a heading MUST move its entire body and descendants together and MUST NOT reflow or corrupt the underlying Markdown (see `content-editing.md`). |
| Round-trip safe | The resulting document MUST round-trip losslessly to canonical Markdown. |

### Reorder × Auto-Save

| Rule | Requirement |
|---|---|
| Immediate persist | A completed reorder (drag drop or keyboard) is a discrete change and MUST auto-save immediately (no debounce), driving the save-state indicator to `Saving` → `Saved`. |
| Optimistic | The new order MUST render immediately; persistence happens in the background. |
| Undoable | A reorder MUST be a single undo step (see `#undo-and-history`). |
| Failure | If persistence fails, the order MUST visibly revert and surface the `Error` save state. |

## Feedback and Toasts

```meta
status: active
```

| Rule | Requirement |
|---|---|
| Non-blocking | Toasts/snackbars are brief, non-blocking, and self-dismissing; they MUST NOT steal keyboard focus. |
| Placement | Prefer bottom-right on desktop/IDE, bottom-center on mobile. Stack vertically with `spacing-sm`; show ≤ 3 at once and queue the rest. |
| Roles | Use `role="status"` for success/info toasts and `role="alert"` for warning/error (see `accessibility.md`). |
| Dismiss | The dismiss control MUST have an accessible label ("Dismiss notification"). |
| No redundant save toasts | Routine auto-saves MUST NOT spam toasts — the persistent save-state indicator carries that; toasts are for failures and notable events only. |
| Inline confirmation | Prefer subtle inline confirmation (e.g. a brief `ease-bounce` flash on a saved inline edit) over a toast for small edits. |

## Motion and Reduced Motion

```meta
status: active
related: [".design/typography-and-layout.md#shadows-and-elevation", ".design/accessibility.md#reduced-motion"]
```

Motion serves purpose only — to communicate a state change, direct attention, or
give feedback. Use the fixed duration/easing tokens.

| Duration token | Value | Easing token | Value |
|---|---|---|---|
| `transition-instant` | 0ms | `ease-linear` | linear |
| `transition-fast` | 150ms | `ease-in` | cubic-bezier(0.4,0,1,1) |
| `transition-base` | 250ms | `ease-out` | cubic-bezier(0,0,0.2,1) |
| `transition-slow` | 350ms | `ease-in-out` | cubic-bezier(0.4,0,0.2,1) |
| `transition-page` | 500ms | `ease-bounce` | cubic-bezier(0.34,1.56,0.64,1) |

Standard combinations:

| Interaction | Duration | Easing |
|---|---|---|
| Button/handle hover, focus ring | `transition-fast` | `ease-in-out` |
| Drag lift, card hover elevation | `transition-base` | `ease-in-out` |
| Drop settle | `transition-base` | `ease-out` |
| Dropdown / slash-command open | `transition-base` | `ease-out` |
| Modal / drawer enter | `transition-slow` | `ease-out` |
| Toast / save-state enter | `transition-slow` | `ease-out` |
| Saved-confirmation flash | `transition-base` | `ease-bounce` |

Rules:

- Default to `ease-in-out`; use `ease-out` for entering elements, `ease-in` for
  exiting. `ease-bounce` is reserved for positive confirmations, never errors.
- Animate `transform`/`opacity`, not layout properties (`width`/`height`/`top`/`left`).
- Under `prefers-reduced-motion: reduce`: replace all translate/scale/rotate and
  slide effects with instant changes or opacity-only fades; spinners stop
  spinning (show a static/indeterminate state); autoscroll keeps working but no
  parallax. See `accessibility.md#reduced-motion`.

## Focus and Selection

```meta
status: active
related: [".design/accessibility.md#focus-visibility"]
```

| Rule | Requirement |
|---|---|
| Visible focus | Keyboard focus MUST use a `color-border-focus` outline at `border-width-2` with a 2 px offset, using `outline` (survives high-contrast) — never `outline: none` without a compliant replacement. |
| Logical order | Focus order MUST follow reading order; modals/drawers trap focus and restore it to the trigger on close. |
| Selection distinct from focus | Selected list items use `color-border-focus` border + optional `color-primary` accent strip; selection MUST be visually distinct from hover and from focus. |
| Multi-select | Bulk selection shows a bulk-action bar with a live count ("3 items selected") and a clear-selection control; "Select all" uses the indeterminate state for partial selection. |
| Reorder focus retention | After a keyboard reorder, focus MUST stay on the moved item's handle. |

## Empty, Loading, and Error States

```meta
status: active
```

### Empty States

| Rule | Requirement |
|---|---|
| Structure | Provide an `icon-xl`/`icon-2xl` illustration, a one-line explanation, and a primary CTA when the user can act. |
| Variants | First-use (CTA to create), filtered/no-results (Clear filters), permissions (no action), error (Try again), complete (e.g. empty inbox — celebratory, no action). |
| Copy | Calm, human copy; no dead ends. |

### Loading States

| Rule | Requirement |
|---|---|
| Skeletons | Use content-shaped skeletons for lists/cards/tables; subtle 2 s pulse, disabled under reduced motion; fall back to error after ~10 s. |
| Spinner | Use for action-triggered ops and page-level loads with no layout preview; never as a content placeholder where a skeleton fits. |
| Stale refresh | When refreshing an already-populated view, keep content visible and show a subtle `icon-sm` spinner in the header — do not blank the view. |
| Local-first | Because data is local-first, most views SHOULD render instantly from local storage; loading states are the exception, not the norm. |

### Error States

| Level | Scope | Pattern |
|---|---|---|
| Page-level | Whole view unusable | Full-page error state with **Retry** + navigation escape (back/home). |
| Section | One widget fails | Inline error card within that section. |
| Action | A button/action fails | Error toast + restore control state so the user can retry. |
| Field | Validation | Inline error below the field, linked via `aria-describedby`, color + text + icon. |

### Offline

| Rule | Requirement |
|---|---|
| Persistent banner | Show a calm, persistent offline banner (`color-info` surface) at the top of the viewport; auto-dismiss on reconnect. |
| Editing continues | Reading and editing MUST continue offline; only genuinely network-only actions (e.g. GitHub sync) are disabled with explanation. |
| Save state | The save-state indicator shows `Offline — changes saved locally` (see `#save-state-indicator-vocabulary`). |
