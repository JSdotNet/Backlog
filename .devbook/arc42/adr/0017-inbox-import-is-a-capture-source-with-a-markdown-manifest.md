# ADR 0017: Inbox import is a capture source; its manifest is Markdown with front matter, not entry text

```meta
related: [".devbook/domain/capture/domain.md#capture-source", ".devbook/domain/capture/domain.md#source-adapter", ".devbook/domain/capture/features.md#import-manifest", ".devbook/domain/inbox/domain.md#capture-source", ".devbook/domain/inbox/domain.md#inbox-list", ".devbook/arc42/adr/0002-backlog-module-owns-the-entry-text-language.md", ".devbook/arc42/adr/0007-import-reuses-the-entry-text-grammar.md", ".devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md"]
```

## Status

```meta
```

Accepted, 2026-09-26. Not built yet. This is the first entry (`import-adr`) of
the `inbox-capture-extension` plan. It is written first so that every later
entry implements this one decision.

The repository owner settled four choices on 2026-09-25. They are inputs here,
not open questions:

| Choice | Settled as |
|---|---|
| The tool imported from | **Microsoft To Do** |
| The manifest's format | **Markdown with front matter**, not JSON |
| Completed items | **Dropped** by the skill that generates the manifest; never carried in as archived |
| Filing | **Allowed** into an Inbox List the mapping names; everything unmapped lands unfiled |

## Context

```meta
```

People keep loose lists in other tools, and the first of those is Microsoft
To Do. Those lists hold ideas, errands, and things to read. That is Inbox
material: it has not been triaged, and most of it is not a task yet. Bringing
it in once, and again later without doubling it, is the whole job.

Backlog already has two ways in, and each one suggests a different home for
import:

- **Tasks' import** (local ADR 0007) reads entry text: a plan is multi-task
  entry text, parsed by the grammar the Tasks module owns (local ADR 0002).
  What it produces is tasks. Those tasks skip triage, which is the one step an
  imported To Do list needs most.
- **Capture** turns outside content into Inbox Items through a `Capture Source`,
  a `Source Adapter`, and `Delivery`. The feed monitors already deliver every
  entry under an id derived from the entry itself (`CaptureIds.For`), so a
  re-run over the same feed adds nothing. The receiving side answers
  `AlreadyKnown`, and nothing remembers what was delivered.

The file a person hands over also looks like entry text. Each item has a title,
some metadata, and a body. That resemblance is what this record has to rule
on.

## Decision

```meta
```

### 1. Import is a capture source, not an Inbox feature

```meta
```

Import is a new `Capture Source`, `import`. It has its own `Source Adapter`, and
it reaches the Inbox through the same `Delivery` port the feed monitors use.
The Inbox gains no import command and no import screen. It receives imported
items the way it receives every other capture, and triages them the same way.
The adapter reads a **manifest**: a file that a skill outside the product
generates from the source tool. The product never talks to Microsoft To Do.

### 2. Dedup is the deterministic capture id, so there is no seen-store

```meta
```

Every manifest item carries the source tool's own id as `external_id`. The
adapter delivers each item under `CaptureIds.For(import, "{tool}:{external_id}")`.
This is the same name-based UUID the feed monitors get. The tool is part of the
name because two tools may reuse each other's id spellings. Re-importing the
same manifest, or a later one that overlaps it, delivers every item again. The
Inbox answers `AlreadyKnown` for each item it already holds. No table records
what was imported, and nothing needs clearing when an import is repeated. An
item the person has since archived or routed stays where they put it. Its id is
already known, so it is never re-created.

### 3. The manifest is Markdown with front matter

```meta
```

- A document-level `---` block carries two keys. `schema` is the manifest's
  version, `1` for now. `tool` is the source tool, a slug such as
  `microsoft-todo`.
- After the block comes one `#`-titled section per capture. The title is the
  Inbox Item's title.
- Directly under each title is a fenced `meta` block of `key: value` lines:
  - `external_id` (required): the tool's own id for the item.
  - `captured_at` (required): when the item was made in the tool, as ISO 8601.
  - `kind` (optional): a `Content Kind`, `text` when absent.
  - `person` (optional): who shared it, stored as the item's `@name`.
  - `list` (optional): the Inbox List to file it in.
- Everything after the fence, up to the next `#` title, is the item's notes. It
  is kept as Markdown and becomes the item's body.

A manifest with one item looks like this. The example is indented rather than
fenced so the devbook tooling does not read its heading as a chapter:

    ---
    schema: 1
    tool: microsoft-todo
    ---

    # Try the new bike route to the office

    ```meta
    external_id: AAMkADk2NzE1…AAA=
    captured_at: 2026-09-20T08:14:00Z
    kind: link
    list: Someday/Maybe
    ```

    https://example.org/route — the one past the canal, Sundays only.

**Filing.** The generating skill maps the source tool's lists to Inbox Lists
and writes the mapped name as `list`. An item whose source list the mapping
does not name gets no `list` and lands unfiled. An item whose `list` names no
existing Inbox List also lands unfiled, and the run's result line says so. An
import never creates a list, because lists are the reader's organisation
(`.devbook/domain/inbox/domain.md#inbox-list`). Filing is not triage, so every
imported item arrives `unprocessed` whether it is filed or not.

**Completed items.** The generating skill leaves out every item the tool marks
completed. A manifest has no status field, so an import cannot create an
archived item.

### 4. Why the manifest is not the entry-text grammar

```meta
```

The manifest shares a shape with entry text: a title, metadata, and a body. It
does not share a grammar. The metadata is **capture facts**: `external_id`,
`captured_at`, `kind`, `person`, and `list`. None of an entry's tokens appear in
it: no type word, no `+tag`, no `id:` or `after:`, no status, effort, or
scheduling sigil. The two formats answer different questions. An entry says
what the work is and where it stands. A capture says where content came from
and when.

Reading the manifest through the entry-text parser would put the Tasks
grammar into Capture, or make Capture call Tasks to parse a document that means
something else. Local ADR 0002 rules out both: the entry-text language is the
Tasks module's alone, and no Tasks grammar lives in Capture. That still holds.
Capture owns this manifest the way it owns its other adapters' input.

The `meta` fence is borrowed because it is the convention the devbook folders
already use: one fenced block of `key: value` lines under a heading. People and
agents in this repository already read and write it, and a Markdown renderer
shows it as a plain code block. It is borrowed as a shape only. The keys are
Capture's, and no devbook tool reads a manifest.

### 5. Why Markdown and not JSON

```meta
```

The notes are Markdown. In JSON every notes body would be one escaped string. A
person checking a manifest before importing it, or trimming one by hand, would
be reading `\n`s. Markdown keeps each item readable in any editor and renders
as a list of titled notes. The front matter and the fences hold only the few
fields the adapter needs to parse strictly.

## Consequences

```meta
```

- `CaptureSourceKind` gains `Import`, slug `import`. The Inbox's mirrored
  `Capture Source` gains the same token. An older build that meets the token
  keeps it as written, as it does for any channel it does not recognise.
- The capture that `Delivery` hands over has to carry the manifest's `kind`,
  `person`, and `list`. Today `CaptureItem` carries a source kind, title, URL,
  body, and time. Widening it is additive, as `ItemCaptured`'s published-language
  rules allow.
- An import is run on demand with a file, not on a schedule. Its result is one
  line in the form the feed monitors already use
  (`.devbook/domain/capture/features.md#import-manifest`).
- A malformed manifest refuses the item it cannot read, not the whole file.
  Examples are an item missing its `external_id` or `captured_at`, or a fence
  that does not parse. The skipped item is counted on the result line, the same
  way a monitor source's failure is that source's line.
- A second source tool needs a generating skill and a new `tool` slug. It needs
  no product change.

## Rejected

```meta
```

- **Import as an Inbox feature.** The Inbox would parse files and draw ids
  itself, and would need its own record of what it had imported. Capture
  already owns normalisation and deterministic ids, and the Inbox would be a
  second place that knows about sources.
- **Reuse the entry-text grammar (local ADR 0007's path).** The imported items
  would become tasks, not Inbox Items, and skip triage. Section 4 above covers
  the grammar itself.
- **A seen-store of imported ids.** It duplicates what the deterministic id
  already guarantees. It would also be one more thing to replicate, reset, and
  get wrong.
- **JSON.** Rejected on 2026-09-25. Section 5 above gives the reason.
- **Carry completed items in as archived.** Rejected on 2026-09-25. It would
  fill the archive with work that was finished elsewhere, and it would need a
  status field that entry-shaped metadata would invite people to extend.
