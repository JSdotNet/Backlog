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
| Headings `#`–`######` | Rendered per `typography-and-layout.md` heading defaults; heading level drives chapter reorder/nesting (`interaction-guidelines.md#nesting--indent-rules-chapters`). |
| Paragraphs | Default body text. |
| Unordered / ordered lists | Including nested lists; stable marker style on serialize. |
| Task lists `- [ ] / - [x]` | Interactive checkboxes; toggling a checkbox is a discrete auto-saved change. Distinct from `##` sub-item headings — see `#backlog-entry-structure`. |
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
| Links | Editable target + text; keyboard-accessible link editing. A link is rendered as a link only when its scheme is `http`, `https`, `mailto`, or a relative path — anything else (`javascript:`, `data:`) MUST render as the literal text the author typed, still readable but inert. The read view is a webview, and an `href` is the one place typed text could otherwise become behaviour. |
| `#tags` | Recognized inline per `.arc42/08-crosscutting-concepts.md#tagging-and-organization`; rendered as a subtle chip but stored as literal `#tag` text. |
| Mentions / references | If supported, stored as their canonical Markdown/text form. |

Anything not in these tables falls under `#unsupported-syntax-preservation`.

## Backlog Entry Structure

```meta
status: active
related: [".domain/backlog/domain.md#aggregate-backlog-entry", ".domain/backlog/naming.md#term-sub-item", ".design/interaction-guidelines.md#nesting--indent-rules-chapters"]
```

> A Backlog Entry is edited as one Markdown document, with headings carrying
> entry-specific meaning beyond the generic chapter nesting in
> `interaction-guidelines.md#nesting--indent-rules-chapters`.

| Construct | Renders as |
|---|---|
| First line | The entry title. Whatever is typed on the first line — with or without a leading `#` — becomes the `#` title on save; an entry is never left without one. |
| `##` heading | A **Sub-Item**: a distinct unit rendered indented one level below the entry title, with its own heading and notes. It is a heading, not a checkbox. |
| `- [ ]` / `- [x]` | A checklist step, rendered as a real checkbox affordance (`#supported-constructs`). Checkbox syntax is reserved exclusively for task-list lines. |
| Metadata line | Backtick-quoted tokens carrying type/priority/status/area/tags — see `#structured-metadata-sigils`. |

| Rule | Requirement |
|---|---|
| Title normalization on flush | The first-line-becomes-title rule applies on blur/auto-save flush, not per keystroke, so the caret never jumps mid-word while typing. |
| Sub-item ≠ checkbox | A `##` sub-item and a `- [ ]` checklist item MUST remain visually and semantically distinct; neither rendering may substitute for the other. |
| Sub-item done-state | A sub-item's own completion (if used) renders as strikethrough plus a `color-primary` accent rule on the sub-item itself, never as a checkbox glyph — the checkbox glyph is reserved for literal task-list syntax. |

## Structured Metadata Sigils

```meta
status: active
related: [".domain/backlog/naming.md#term-entry-status", ".domain/backlog/naming.md#term-area", ".design/typography-and-layout.md#font-families"]
```

> Backlog entries carry structured metadata (type, priority, status, area,
> tags) inline as backtick-quoted tokens. Each kind but one is disambiguated by
> a one-character sigil, so neither a human reader nor the parser has to guess
> which bare word means what.

| Sigil | Kind | Example |
|---|---|---|
| *(none)* | type | `` `task` ``, `` `idea` ``, `` `follow-up` `` |
| `!` | status | `` `!draft` ``, `` `!ready` ``, `` `!in-progress` `` |
| `*` | priority | `` `*high` `` |
| `@` | area | `` `@repos` `` |
| `#` | tag | written in the body, e.g. `#sync` |

| Rule | Requirement |
|---|---|
| Type is the one bare word | The entry already is its type, so the type token needs no sigil; every other kind gets one because none of them is "the" word an entry is. |
| Backward compatible | Metadata written before this convention existed (bare, un-sigilled tokens) MUST continue to parse; saving an entry rewrites its metadata line into canonical sigil form so entries self-heal. |
| Sigil wins over guessing | A sigilled token that does not match a known value for its declared kind (e.g. a priority sigil on a status word) MUST NOT fall through and be reinterpreted as another kind — the sigil already declared intent, so it is simply unrecognized rather than misread. |
| Monospace | Metadata tokens render in `font-family-mono`, matching inline code, so the syntax reads as structured rather than prose. |

## Live Parse Confirmation

```meta
status: active
related: [".design/content-editing.md#editing-feedback-and-state"]
```

> While an entry is focused, the editor shows a single-line "reads as" hint
> beneath the raw Markdown, restating exactly what the current text will be
> saved as — produced from the same parse the save uses, not a separate guess.

| Rule | Requirement |
|---|---|
| Same-parse guarantee | The hint MUST be produced by the identical parse that persistence uses; it must never diverge from what actually gets saved. |
| Explicit vs. default | A value the user actually typed renders at full emphasis; a value that is only the current default (nothing typed yet) renders at reduced emphasis, so a default is never mistaken for something the user asserted. |
| Refused status is explained | If the typed status is not a legal next step in the entry's lifecycle (`.domain/backlog/flow.md#backlog-entry-lifecycle`), the hint MUST show the status that will actually be kept plus which statuses are legal next steps — the typed word is never silently dropped. |
| Not an error state | A refused status is informational (`color-primary` accent), not an error toast or blocking validation; the entry still saves, just without the refused change. |

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
related: [".design/interaction-guidelines.md#save-state-indicator-vocabulary", ".design/content-editing.md#live-parse-confirmation"]
```

| Rule | Requirement |
|---|---|
| Save state | The editor surfaces the shared save-state indicator (`Saved` / `Saving…` / `Offline` / `Conflict` / error) — never a save button. |
| Undo/redo | Standard undo/redo applies to all editor changes and coalesces rapid keystrokes (see `interaction-guidelines.md#undo-and-history`). |
| Conflicts | Concurrent edits resolve last-write-wins with passive `Conflict` surfacing (see `interaction-guidelines.md#conflict-handling`). |
| Spellcheck scope | Spellcheck applies to prose, not to code blocks, inline code, or raw passthrough blocks. |
| Domain-rule refusal | A parsed value the domain model refuses (e.g. an illegal status transition, see `#live-parse-confirmation`) is surfaced inline, next to the text that produced it, not through the save-state indicator — the save still succeeds; only that one value is refused. |

## Materialization

```meta
status: active
related: [".design/README.md#living-reference-the-ui-storybook", ".design/typography-and-layout.md#heading-defaults"]
```

| Piece | Where | Review surface |
|---|---|---|
| Parser | `MarkdownPreview` (`src/Core/Backlog.UI.Components/Markdown`) | Storybook → *Markdown* → **Blocks the parser produces**, which lists what the source in the first story parsed to |
| Read view | `MarkdownView` | Storybook → *Markdown* |
| Document surface | `FileView` — a file's header and its body, the body scrolling under a fixed header. The header also carries what a reader does to the file (copy, edit, compare), and a Markdown body is read whole: each chapter's `meta` status beside its heading, each diagram where its fence is, a copy button per chapter, and remarks in the margin | Storybook → *File view* → **A knowledge chapter, whole** |
| Comparison surface | `MarkdownCompare` (a pure function over two texts) and `MarkdownCompareView`, aligned by heading and never by line. `Bare` gives up its frame so `FileView` can show it without a second header or a second scroll region | Storybook → *Section comparison*, and *File view* → **Compared against two versions of itself** |
| Chapter remarks | `MarkdownView` comments, anchored to a block index rather than a character range, drawn inline or in a margin column | Storybook → *Markdown* → **The same comments, in the margin** |
| Code blocks | `CodeView` — line numbers, copy button, per-language highlighting on the tokens in `color-scheme.md#syntax-highlighting-tokens` | Storybook → *Code* |
| Metadata sigils | `MetadataBadge`, `StatusBadge`, `PriorityBadge`, `TagChip` | Storybook → *Badges* |
| Task lists | `MarkdownView` checkbox, toggling straight back into the source | Storybook → *Markdown* → **Edit and read** |

What is true today, and where it differs from the model above:

- **The editing model is inverted.** `#editing-model` specifies WYSIWYG as the
  primary mode with a raw escape hatch. What exists is the opposite: the source
  is edited as raw Markdown in a text area, with a live read view beside it. There
  is no rich-text editor, no slash menu (`#slash-and-inline-commands`), and no
  inline autoformat. Markdown being canonical is therefore trivially satisfied,
  and `#round-trip-fidelity` is not yet under pressure — both become real
  requirements the moment an editor lands.
- **Task toggling is already correct.** Ticking a checkbox writes back into the
  source and saves immediately with no debounce, and a `- [ ]` inside a code
  fence is left alone — it is a code sample, not a task.
- **Headings render on the compact ramp**, not the display ramp; see
  `typography-and-layout.md#heading-defaults` for which surface gets which.
- **Body text renders in `color-text-secondary`.** `design-principles.md#low-chrome-content-first`
  reserves `color-text-primary` for content and `color-text-secondary` for
  chrome, and the read view currently has that the other way round for
  paragraphs, list items and code blocks. It reads as intended inside a dense
  card and reads as washed-out in a document; a document surface SHOULD move to
  `color-text-primary`. `[TODO: clarify]`
