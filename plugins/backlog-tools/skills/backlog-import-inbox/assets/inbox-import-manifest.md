# Inbox Import Manifest Grammar

Reference for `skills/backlog-import-inbox`, which writes manifests, and for
`inbox-import-review.html`, whose checks mirror this file rule for rule. The decision behind
it is local ADR 0017
(`.devbook/arc42/adr/0017-inbox-import-is-a-capture-source-with-a-markdown-manifest.md`), and
the reader is the Capture module's import adapter. The manifest is **not** Backlog's entry
text: it shares that grammar's shape (a title, metadata, a body) and none of its tokens. So
`+tag`, `id:`, `after:`, `!status` and a type word mean nothing here.

A manifest is often edited by hand before it is imported: an item trimmed, a title fixed, a
list renamed. Every rule below is written so that a person can follow it in an editor, and
so that the review view can say exactly which item breaks which rule.

## Document shape

One Markdown document, UTF-8, in this order:

1. The **front matter**: a `---` line as the file's very first line, the keys below, and a
   closing `---` line.
2. One **item** per capture, each opening with a `# ` title line. A line that begins `# `
   always starts a new item, wherever it appears.

Anything between the front matter and the first item is ignored. Keep it empty.

## Front matter

| Key | Rule |
|---|---|
| `schema` | Required. The manifest's version. It is `1`, the only version there is. |
| `tool` | Required. The source tool's slug, lower-case letters, digits and hyphens, e.g. `microsoft-todo`. Part of every item's identity, so one tool keeps one slug for good. |

One `key: value` per line. No other keys: the reader ignores them, and the review flags
them.

## Item

```
# <Title>

```meta
external_id: <the tool's id>
captured_at: <UTC timestamp>
…optional keys…
```

<notes>
```

- **Title**: the rest of the `# ` line, trimmed, and not empty. It becomes the Inbox
  Item's title. One line: a title never wraps.
- **Meta fence**: the first non-blank line after the title opens a fence with exactly
  ` ```meta `, and a line with exactly ` ``` ` closes it. In between, one `key: value` per
  line: the key, a colon, a space, then the value to the end of the line. No blank lines,
  no nesting, no quotes (a value is taken as written, trimmed). A key appears at most once.
- **Notes**: everything after the closing fence, up to the next `# ` line, is the item's
  body. It is Markdown, kept as written, and may be empty.

### Meta keys

| Key | Required | Rule |
|---|---|---|
| `external_id` | yes | The tool's own id for the item, as the tool spells it. Unique within the manifest. Together with `tool`, it is the item's identity: `CaptureIds.For(import, "{tool}:{external_id}")`, so importing the same item again adds nothing. When the tool gives an item no id, write the **fallback id**: `title-sha256:` followed by the first 16 hex digits of the SHA-256 of `<title>\n<captured_at>` in UTF-8. It is stable only while the title and timestamp are, so a renamed item comes in again. An item a person writes without an `external_id` is imported under that same fallback, computed by the import, so write one only when the tool has it. |
| `captured_at` | yes | When the item was made in the tool: ISO 8601 in UTC, `YYYY-MM-DDTHH:MM:SSZ`. The generating skill always writes it that way. A hand edit with an offset (`+02:00`) still reads, and the review asks for `Z`. An item without one is skipped. |
| `url` | no | The original link: an absolute `http` or `https` URL the item points at. Left out when the item has none. It is never made up. |
| `kind` | no | A `Content Kind`: `text`, `article`, `link`, `youtube`, `image`, `document`, `email` or `code`. Leave it out to let intake detect it from the title, link and notes, which is the usual case. A word outside the list is kept as that word. |
| `tags` | no | The mapped labels, as bare names separated by `, ` — no `#`, no `@`, no spaces inside a name. |
| `person` | no | Who shared the item, as a bare name. It is stored as the item's `@name`. |
| `list` | no | The Inbox List to file the item in, by its exact name. It must already exist in the Inbox. An import never creates a list, and an item whose list does not exist lands unfiled. Leave it out to land the item unfiled. |

Any other key is ignored by the reader and flagged by the review.

### Notes

- A notes line that would begin `# ` starts a new item. The generating skill writes such a
  line as `## ` instead. A person editing by hand has to do the same.
- Facts the manifest has no key for (a due date, an importance, a reminder) go in the notes
  as one closing line, in the tool's own terms. They are for the reader, not the import.
- Checklist steps are `- [ ]` and `- [x]` lines.

## What a manifest never holds

- A completed item. The generating skill drops those, so there is no status key and an
  import can never create an archived item.
- A second shape. Every tool writes exactly this grammar and differs only in its `tool`
  slug and where its ids come from.

## Worked example

A Microsoft To Do export with three open tasks and one completed one. Its `Groceries`
list was mapped to the Inbox List `Errands`. Its `Reading` list was left unmapped. Of its
categories, only `Blue category` was mapped, as the tag `home`. The completed task was
dropped:

````markdown
---
schema: 1
tool: microsoft-todo
---

# Try the new bike route to the office

```meta
external_id: AAMkADk2NzE1LWI3ZGEtNGE1Zi1hMTIzLTQ1Njc4OWFiY2RlZgBGAAAAAAB8
captured_at: 2026-09-20T08:14:00Z
url: https://example.org/routes/canal
tags: home
```

The one past the canal, Sundays only.

# Buy oat milk

```meta
external_id: AAMkADk2NzE1LWI3ZGEtNGE1Zi1hMTIzLTQ1Njc4OWFiY2RlZgBGAAAAAAB9
captured_at: 2026-09-22T17:02:41Z
list: Errands
```

- [ ] the barista one
- [x] check the price at the corner shop

Due 2026-09-27 in Microsoft To Do.

# Read the Graph delta query write-up

```meta
external_id: AAMkADk2NzE1LWI3ZGEtNGE1Zi1hMTIzLTQ1Njc4OWFiY2RlZgBGAAAAAAC0
captured_at: 2026-09-24T21:40:07Z
url: https://learn.microsoft.com/graph/delta-query-overview
```
````

The import files `Buy oat milk` into `Errands` and lands the other two unfiled, all
`unprocessed`, with source `import`. It reports
`Import (microsoft-todo): 3 new items · 0 already known.` Running it again reports
`Import (microsoft-todo): 0 new items · 3 already known.`
