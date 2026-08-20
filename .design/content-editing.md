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
related: [".domain/backlog/domain.md#backlog-entry", ".domain/backlog/naming.md#sub-item", ".design/interaction-guidelines.md#nesting--indent-rules-chapters"]
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
related: [".domain/backlog/naming.md#entry-status", ".domain/backlog/naming.md#area", ".design/typography-and-layout.md#font-families", ".design/content-editing.md#scheduling-and-dependency-tokens"]
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
| The namespace is closed | These five kinds are the whole of the sigil vocabulary. A new kind of metadata takes a named `name:value` token instead — see [Scheduling and Dependency Tokens](#scheduling-and-dependency-tokens). Minting a sixth sigil would trade a readable name for a character nobody remembers. |

## Scheduling and Dependency Tokens

```meta
status: active
related: [".design/content-editing.md#structured-metadata-sigils", ".domain/backlog/naming.md#due-date", ".domain/backlog/naming.md#reminder", ".domain/backlog/naming.md#recurrence", ".domain/backlog/naming.md#my-day", ".domain/backlog/naming.md#dependency"]
```

> When an entry is scheduled or waits on other entries, those facts ride on the
> same backtick-quoted metadata line — but as `name:value` tokens rather than
> one-character sigils. Five date-shaped facts cannot be told apart by
> punctuation a reader will remember, and this line is hand-edited.

| Token | Kind | Example |
|---|---|---|
| `due:` | due date | `` `due:2026-08-21` `` |
| `remind:` | reminder | `` `remind:2026-08-21T09:00` `` |
| `repeat:` | recurrence | `` `repeat:weekly` ``, `` `repeat:weekdays` ``, `` `repeat:2w` `` |
| `myday:` | My Day | `` `myday:2026-08-19` `` |
| `after:` | dependency | `` `after:a1b2c3` `` — may repeat |
| `files:` | attached folder or archive | `` `files:D:/reviews/panel-review` ``, `` `files:D:/reviews/panel.zip` `` |
| `view:` | which reading of the body to open in | `` `view:steps` ``, `` `view:notes` `` |

A full metadata line mixes both forms, sigils first:

```markdown
# Deploy SpecManager

`task` `*high` `!ready` `@repos` `#deploy` `due:2026-08-21` `remind:2026-08-21T09:00` `after:a1b2c3`
```

| Rule | Requirement |
|---|---|
| Named, not sigilled | A named token says which fact it carries without the reader holding a legend. The sigil namespace is also nearly exhausted, so five more marks would leave nothing for a sixth kind. |
| A due date is a date | `due:` and `myday:` take a calendar date (`YYYY-MM-DD`) with no time and no timezone. A due date is a commitment to a day, and an instant would move the deadline whenever the device changed zone. |
| A reminder is wall-clock | `remind:` takes a local date and time (`YYYY-MM-DDTHH:mm`) and deliberately carries no zone or offset: `09:00` means 09:00 wherever the reader is when it arrives, not the instant 09:00 once meant elsewhere. |
| My Day expires by arithmetic | `myday:` holds the date the entry was picked for, not a flag. The entry is in My Day exactly while that date is the reader's current local date, so yesterday's list clears itself with no timer and no overnight sweep. |
| A path is taken as written | `files:` takes a path and the parser has no opinion about it — separators, drive letters and spaces all survive, because a path is whatever the file system will accept and a grammar that refused the ones it disliked would be a grammar with an opinion about operating systems. Nothing is checked against the disk either: a path is meaningful on the machine that wrote it, so an entry read elsewhere may name a place that is not there and is no less valid for it. |
| One attached place, and never a list | `files:` appears at most once. A second one is a replacement rather than an addition, and the last one on the line wins. This is the opposite rule to `after:` above, deliberately: a task waiting on two things is ordinary, where an entry pointing at two folders is an entry whose presentation grows with however many places somebody named. |
| Dependencies repeat | `after:` may appear more than once and the order carries no meaning — an entry waiting on two things names both, and asking which is the real predecessor has no answer. An id naming nothing visible still counts, and still blocks. |
| Unknown tokens survive an edit | A `name:value` token the parser does not recognize MUST be preserved when an unrelated field is changed, on the same terms as the backward-compatibility rule for sigils. It is unrecognized, not invalid. |
| Absent means absent | An unset field carries no token rather than an empty one. `` `due:` `` with nothing after it is malformed, not "no due date". |
| Canonical rewrite is destructive by design | Saving rewrites the metadata line into canonical form from the entry model, so a token the model cannot represent does not survive the next save. A new token MUST therefore be added to the domain model, the entry DTO, and the canonical rewrite in the same change — adding it to the parser alone loses data silently, with no error. |
| `view:` is a preference, not a fact about the work | The steps list and the Markdown block are two readings of the same body (`#backlog-entry-structure`), and `view:` records which one an entry opens in. It is the one token here that says nothing about the task — it is about looking at it. It rides on this line anyway because Markdown is canonical (`#editing-model`): a preference kept in a sidecar would not survive the file being shared, and whoever opened the entry from a clone would get somebody else's default. |
| A view is chosen, never derived onto the line | An entry nobody has expressed a preference about carries no `view:` token and MUST NOT acquire one by being saved — "absent means absent" applies here too. The surface picks a sensible reading for an entry with no token (an entry with no `##` chapters has no steps to list), and that choice stays in the surface: a default written into the text is a preference nobody made, and it would have to be unwritten from every entry before the default could change. |
| Neither reading may hide text without saying so | The steps reading lists `##` chapters, so prose an entry opens with has no row. Where a reading omits body text that exists, the surface MUST say so and offer the Markdown block in the same breath. Silently showing less than the entry holds reads as text the app has lost, which is the failure `#round-trip-fidelity` exists to prevent — only on screen rather than on disk. |

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

## AI Proposals in a Document

```meta
status: active
related: [".design/design-principles.md#ai-first-surfaces", ".design/content-editing.md#editing-model", ".design/accessibility.md#screen-reader--announcements"]
```

The product may offer to rewrite a passage or to answer a remark. What it may
**not** do is change the document on its own.

| Rule | Requirement |
|---|---|
| Anchoring | A proposal is anchored to a block, the same way a remark is — never to a character range, which no edit survives. "Rewrite part" therefore means rewrite this paragraph, and the product MUST NOT offer a finer grain than its anchors can carry. |
| Never in place | The change does not happen until a person says so. Original and suggested text are shown together, and rejecting leaves the document byte-identical rather than restored. |
| Applied upstream | Accepting raises a callback; the host re-parses and hands the document back. The view MUST NOT write into the document itself. |
| Attribution survives | An accepted proposal keeps who wrote it, when, and where it came from, and moves to an accepted state rather than disappearing. "Did a person write this paragraph" is a question the document has to be able to answer later. |
| One act, one answer | Where a proposal answers a remark, accepting the text and resolving the remark are two acts with two callbacks. One button doing both denies a reader who wanted the wording but not the resolution. |
| Two carriers | AI-authored content MUST be distinguishable visually **and** named for assistive technology: a tint for the eye, an accessible name for everything else. Neither alone reaches everybody. |
| The tint | The tint is a mix of the one brand hue against the card surface. A second semantic hue is not available (`color-scheme.md#role-tokens`). |
| The product's own AI | An AI act that runs inside the product carries **no vendor mark**, and what it writes is attributed to AI and nothing further. A vendor logo is a claim about where a reader's content travelled; only an act that leaves the application may make one. |
| Offline | An AI act offline is an unavailable act like any other: present, disabled, with a calm sentence and no remedy. Editing, commenting and reading MUST remain untouched — nothing may suggest the document depends on it. |
| Lost anchors | A proposal whose block has gone joins the same orphan region a remark does, rather than being dropped. |

Review surface: storybook → *Integrations* → **AI in the document**.

## Materialization

```meta
status: active
related: [".design/README.md#living-reference-the-ui-storybook", ".design/typography-and-layout.md#heading-defaults", ".design/content-editing.md#scheduling-and-dependency-tokens"]
```

| Piece | Where | Review surface |
|---|---|---|
| Parser | `MarkdownPreview` (`src/Core/Backlog.UI.Components/Markdown`) | Storybook → *Markdown* → **Blocks the parser produces**, which lists what the source in the first story parsed to |
| Read view | `MarkdownView` | Storybook → *Markdown* for every block and inline; *Markdown document* for a section with copy, remarks and a way into the editor |
| Document surface | `FileView` — a file's header and its body, the body scrolling under a fixed header. The header also carries what a reader does to the file (copy, edit, compare), and a Markdown body is read whole: each chapter's `meta` status beside its heading, each diagram where its fence is, a copy button per chapter, and remarks in the margin | Storybook → *File view* → **A knowledge chapter, whole** |
| What a file says it is for | A file opening with YAML frontmatter can state it above the body instead of through it: the description as prose, `applyTo` and `tools` as badges, and every other key the file wrote as a label and a value in that order. Asked for, never assumed, and the block leaves the read view when it is shown — `---` parses as a divider, so frontmatter otherwise reads as two rules around a run-together paragraph. Keys it does not draw are still drawn, because a view must not hide what it does not show; a buffer being edited keeps the block verbatim, since a save without those lines would drop them | Storybook → *File view* → **What the file says it is for** |
| Comparison surface | `MarkdownCompare` (a pure function over two texts) and `MarkdownCompareView`, aligned by heading and never by line. `Bare` gives up its frame so `FileView` can show it without a second header or a second scroll region | Storybook → *Section comparison*, and *File view* → **Compared against two versions of itself** |
| Chapter remarks | `MarkdownView` comments, anchored to a block index rather than a character range, drawn inline or in a margin column | Storybook → *Markdown document* → **The same comments, in the margin** |
| Code blocks | `CodeView` — line numbers, copy button, per-language highlighting on the tokens in `color-scheme.md#syntax-highlighting-tokens` | Storybook → *Code* |
| Metadata sigils | `MetadataBadge`, `StatusBadge`, `PriorityBadge`, `TagChip` | Storybook → *Badges* |
| Task lists | `MarkdownView` checkbox, toggling straight back into the source | Storybook → *Entry edit* → **A checkbox writes back to the source** |
| Scheduling and dependency tokens | Read and written by `EntryTextParser`; `TaskAction` is the shape a control over one takes, over the date, time and repeat fields beside it | Storybook → *Inputs* → **A date, a time and a repeat**, **TaskAction — set it, see it, clear it** |
| Entry list and detail pane | `SplitPane` (anchored to the end, so the open entry is the fixed half and the list flexes) over a `TaskListView` of entries and one open entry: its own row with the title as a field, its body as either a `TaskListView` of steps or one `MarkdownEditor`, the `TaskAction` rows, the selectors, and the raw hatch | Storybook → *Task list*; *Layout* → **Split pane**, **Split pane, anchored to the end** |
| The body's two readings | One region, switched by `view:` and remembered on the entry; the steps reading says so when the body holds prose it is not showing | Backlog pane → the **Steps** / **Markdown** button group |
| Raw-Markdown escape hatch | The whole entry's canonical text in a mono `TextArea`, with the live "reads as" hint under it | Backlog pane → the **Markdown** toggle, or Ctrl+Shift+M |

What is true today, and where it differs from the model above:

- **The editing model is no longer inverted, and is not yet WYSIWYG either.** The
  backlog's detail pane edits an entry a field at a time — a title, a note, a
  step, a date, a dependency — and the canonical Markdown is a toggle and a
  keyboard shortcut away, which is the escape hatch
  `#raw-markdown-escape-hatch` asks for rather than the primary surface
  `#editing-model` rules out. What is still missing from `#editing-model` is the
  rich-text part: the note is a Markdown editor with a formatting toolbar, not a
  WYSIWYG view, and there is no slash menu (`#slash-and-inline-commands`) and no
  inline autoformat. Markdown being canonical is therefore still trivially
  satisfied, and `#round-trip-fidelity` is only under pressure where a field
  rewrite touches text it was not asked about — which is why each of those
  rewrites is scoped to one chapter and leaves the rest byte-for-byte.
- **The metadata line now carries two syntaxes.** Sigil tokens for type, priority,
  status, area and tags, and named `name:value` tokens for the scheduling and
  dependency fields — see `#scheduling-and-dependency-tokens` for why the second
  form exists rather than five more sigils. A reader sees one line either way; the
  split is about which characters were still available, not about two kinds of
  fact.
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
