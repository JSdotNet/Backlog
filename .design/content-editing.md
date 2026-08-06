# Content Editing

```meta
status: active
related: [".arc42/02-constraints.md#technical-constraints", ".design/interaction-guidelines.md#auto-save-no-save-buttons", ".design/typography-and-layout.md#type-scale", ".design/accessibility.md"]
```

> Rules for the product's core editing surface: **direct Markdown editing through
> a rich text (WYSIWYG-style) editor where Markdown is the canonical stored
> format**. Per `.arc42/02-constraints.md#technical-constraints`, Markdown is the
> single source of truth; the editor is a view over it and MUST never corrupt it.
> Rendering (headings, code, etc.) uses the tokens in
> `typography-and-layout.md`; editor persistence follows the auto-save rules in
> `interaction-guidelines.md#auto-save-no-save-buttons`.

## Editing Model

```meta
status: active
related: [".arc42/02-constraints.md#technical-constraints"]
```

| Rule | Requirement |
|---|---|
| Markdown is canonical | The stored artifact is Markdown. The editor is a WYSIWYG **view** over canonical Markdown; the document model MUST serialize back to Markdown as the source of truth. |
| Edit in place | Users edit formatted content directly (type, apply styles, insert blocks) and see the result inline — not a split "write raw / preview" as the primary mode. |
| No lossy intermediate format | The editor MUST NOT store a proprietary format as canonical; any internal document model exists only to render/serialize Markdown. |
| Auto-save | Editor changes persist per `interaction-guidelines.md#auto-save-no-save-buttons` (debounced text saves, immediate discrete changes, flush on blur). There is no save button. |
| Content vs chrome | Editor content uses `color-text-primary`; editor chrome/placeholder uses `color-text-secondary` (see `design-principles.md#low-chrome-content-first`). |

## Round-Trip Fidelity

```meta
status: active
```

Round-trip fidelity is the single most important correctness rule for the editor.

| Rule | Requirement |
|---|---|
| Lossless round-trip | `parse(serialize(doc)) === doc` for all supported constructs: opening a Markdown file, editing an unrelated part, and saving MUST NOT rewrite or reorder untouched content. |
| Stable serialization | Serialization MUST be deterministic and stable (consistent list markers, heading style, emphasis characters, fence style) so diffs stay minimal and Git-friendly. |
| Preserve unknown/unsupported | Constructs the WYSIWYG layer does not render richly (see `#unsupported-syntax-preservation`) MUST be preserved verbatim, never dropped. |
| Whitespace discipline | The serializer MUST NOT introduce gratuitous whitespace/reflow churn; it SHOULD preserve the author's existing formatting where reasonable. |
| No silent normalization | Any unavoidable normalization MUST be documented and predictable; it MUST NOT change document meaning. |

## Raw-Markdown Escape Hatch

```meta
status: active
related: [".design/typography-and-layout.md#font-families"]
```

Power users and edge cases need direct access to the source.

| Rule | Requirement |
|---|---|
| Always available | A **raw Markdown** view/mode MUST be available for the current document, toggleable via a command and keyboard shortcut. |
| Faithful source | The raw view shows the exact canonical Markdown that will be stored, in `font-family-mono`. |
| Two-way | Edits in raw mode MUST update the same canonical document; switching back to WYSIWYG re-renders from that source. |
| Block-level hatch | For a single tricky block, the editor SHOULD allow editing that block's raw Markdown inline without switching the whole document. |
| Same auto-save | Raw-mode edits auto-save under the same rules; there is no separate save step. |

## Supported Constructs

```meta
status: active
```

The editor MUST render and edit these as first-class WYSIWYG constructs, and MUST
serialize them to canonical Markdown.

### Block Constructs

| Construct | Notes |
|---|---|
| Headings `#`–`######` | Rendered per `typography-and-layout.md` heading defaults; heading level drives chapter reorder/nesting (`interaction-guidelines.md#nesting-indent-rules-chapters`). |
| Paragraphs | Default body text. |
| Unordered / ordered lists | Including nested lists; stable marker style on serialize. |
| Task lists `- [ ] / - [x]` | Interactive checkboxes; toggling a checkbox is a discrete auto-saved change. |
| Blockquotes `>` | Including nested. |
| Code blocks (fenced) | `font-family-mono`, language hint preserved; not spell-checked; content never "smart-formatted". |
| Tables (GFM) | Editable grid; serialize to pipe tables. |
| Horizontal rules `---` | |
| Images / embeds | Rendered for dark surfaces (see `design-principles.md#dark-mode-only`); alt text required (see `accessibility.md`). |
| Front-matter / metadata block | Preserved verbatim; not silently reformatted. |

### Inline Constructs

| Construct | Notes |
|---|---|
| Bold, italic, strikethrough | `**`, `*`/`_`, `~~`. |
| Inline code | Backticks; `font-family-mono`. |
| Links | Editable target + text; keyboard-accessible link editing. |
| `#tags` | Recognized inline per `.arc42/08-crosscutting-concepts.md#tagging-and-organization`; rendered as a subtle chip but stored as literal `#tag` text. |
| Mentions / references | If supported, stored as their canonical Markdown/text form. |

Anything not in these tables falls under `#unsupported-syntax-preservation`.

## Paste Behavior

```meta
status: active
```

| Source | Behavior |
|---|---|
| Rich text (HTML) from browser/office | Convert to clean Markdown-equivalent constructs; strip styles, colors, and fonts (dark-mode-only product) — keep structure (headings, lists, links, bold/italic, tables, code). |
| Plain text | Insert as-is; MUST NOT auto-linkify or transform beyond standard Markdown autoformat. |
| Markdown text | Insert and parse as Markdown so it renders in WYSIWYG. |
| Code | Paste into a code block MUST preserve exact characters and whitespace; no smart quotes, no reflow. |
| Images | Handle per product image-storage policy `[TODO: clarify]` (embed vs. reference); alt text prompt SHOULD follow. |

Rules:

- **Paste and match** (paste as plain text) MUST be available via the standard
  shortcut.
- Paste MUST NOT inject disallowed inline color/font styling; the product is
  token-themed and dark-only.
- Pasted content MUST round-trip losslessly once inserted.

## Slash and Inline Commands

```meta
status: active
related: [".design/interaction-guidelines.md#feedback-and-toasts", ".design/design-principles.md#keyboard-first"]
```

| Rule | Requirement |
|---|---|
| Slash menu | Typing `/` at the start of an empty block MUST open a command menu to insert blocks (heading, list, task, code, table, quote, divider, image) and invoke AI actions. |
| Keyboard-driven | The slash menu MUST be fully keyboard-operable (type to filter, arrows to move, Enter to select, Escape to dismiss) and rendered on `z-index-dropdown`. |
| Inline autoformat | Standard Markdown autoformat MUST work: `# ` → heading, `- `/`* ` → list, `1. ` → ordered list, `> ` → quote, ``` ``` ``` → code block, `[ ] ` → task, `**x**` → bold, etc. |
| Escape autoformat | Undo (Ctrl/Cmd+Z) immediately after an autoformat MUST revert to the literal typed characters. |
| Consistent command surface | Slash commands and the command palette SHOULD share one command registry (see `design-principles.md#ai-first-surfaces`). |
| Accessible | Command menus follow menu ARIA and announcement rules in `accessibility.md`. |

## Unsupported Syntax Preservation

```meta
status: active
related: [".design/interaction-guidelines.md#reorder--auto-save"]
```

The editor MUST **preserve, never destroy**, Markdown it cannot render richly.

| Rule | Requirement |
|---|---|
| Preserve verbatim | Unknown/unsupported syntax (custom directives, uncommon HTML, extension syntax, raw HTML blocks) MUST be preserved byte-for-byte on save. |
| Visible, editable | Such content SHOULD render as a raw/mono passthrough block that the user can still see and edit as source, clearly marked as raw. |
| No silent deletion | The editor MUST NOT drop, "clean up", or rewrite unsupported syntax as a side effect of editing elsewhere. |
| Round-trip guarantee | Documents containing unsupported syntax MUST still satisfy the lossless round-trip rule (`#round-trip-fidelity`). |
| Fail safe | If the editor cannot confidently parse a document, it MUST fall back to the raw-Markdown escape hatch rather than risk corrupting content. |

## Editing Feedback and State

```meta
status: active
related: [".design/interaction-guidelines.md#save-state-indicator-vocabulary"]
```

| Rule | Requirement |
|---|---|
| Save state | The editor surfaces the shared save-state indicator (`Saved` / `Saving…` / `Offline` / `Conflict` / error) — never a save button. |
| Undo/redo | Standard undo/redo applies to all editor changes and coalesces rapid keystrokes (see `interaction-guidelines.md#undo-and-history`). |
| Conflicts | Concurrent edits resolve last-write-wins with passive `Conflict` surfacing (see `interaction-guidelines.md#conflict-handling`). |
| Spellcheck scope | Spellcheck applies to prose, not to code blocks, inline code, or raw passthrough blocks. |
