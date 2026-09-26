# Capture

```meta
type: features
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Mobile capture

```meta
type: feature
status: draft
```

Frictionless capture from a phone while away from the desktop, with the minimum
required fields and offline-first behavior.

### One-tap entry

```meta
type: sub-feature
status: draft
```

Rapid title + body capture with optional tags and source context.

The sync service takes the body, the tags and a person alongside the title and
the source, each optional and each bounded. A person is sent as the person and
never as a tag; on the desktop it becomes the item's source person, and the tags
are stored bare and once each. A phone that sends only a title and a source is
answered exactly as before.

### Speech-to-text capture

```meta
type: sub-feature
status: draft
```

On-device transcription of voice notes into usable markdown, with retry on
transcription failure, preserving source metadata.

### Offline-first sync

```meta
type: sub-feature
status: draft
```

Local storage of captures when offline and background synchronization when the
network returns.

Every capture is kept on the phone before it is sent, so no signal never costs a
capture. It appears in the Inbox list at once, marked waiting, and leaves in the
order it was made. A failed send is retried on its own a few times with growing
waits. After that it shows "waiting — tap to retry", and is tried again on a
tap, when the app is reopened, or when the network comes back. Nothing captured
later is sent ahead of it.
The status line says how many captures are waiting.

Each capture carries an id the phone mints before its first send, so resending
it after a lost answer delivers it once: the service answers with the capture it
already holds instead of storing a second one.

The Inbox list reads at a glance: when each capture was made, a glyph for what
kind of thing it is, and the first line of its body. A tap opens the whole
capture with its source, tags and person. The phone acknowledges rather than
triages, so the one action on a row is Dismiss. The list refreshes on a pull
down, on the refresh button and on returning to the app. When the service
cannot answer, the last list the phone saw stays on screen with a line saying
why it is not newer.

### Share-sheet and shortcuts

```meta
type: sub-feature
status: draft
```

Share-sheet and shortcut integration for quick clipping from other apps.

## Automation capture

```meta
type: feature
status: draft
```

Unattended monitors that watch external sources and create captures on a
configurable schedule, with retry/backoff and failure logging.

### Run capture now

```meta
type: sub-feature
status: draft
related: [.devbook/domain/capture/domain.md#source-adapter, .devbook/domain/inbox/features.md#incoming-queue]
feature-flag: .devbook/domain/capture/context.md#inbox-pane
```

The reader runs the monitors on demand from the Inbox, without waiting for a
schedule. Which sources are watched — YouTube, website, email — and what each
one looks at (channels, URLs, senders) is set in a folded Sources panel on the
same Inbox pane, beside the button that runs them, and kept on the machine; a
source is off until switched on. A run answers per source: how many new
captures it delivered, or that no `Source Adapter` exists for it yet, so a
source that cannot be watched says so instead of silently finding nothing.
With no source switched on, the run points at the panel that would change
that. Each source's row also carries when it was last looked at and how many
new captures that run delivered, and a log of the runs before it — the same
per-source line each run reported, kept on the machine as a bounded window
rather than an archive.

### YouTube monitor

```meta
type: sub-feature
```

Poll a subscribed channel's video feed — given as a feed URL, a channel id, or
a handle/user/c page resolved to one — for new videos. Delivers straight into
the Inbox; no folder filing and no `#capture/youtube` auto-tag yet.

### Website monitor

```meta
type: sub-feature
```

Watch a configured URL for a discoverable feed — `<link rel=alternate>`, a
same-host feed link, or a well-known path — and deliver new entries into the
Inbox. No DOM-diff fallback for a site with no feed, no folder filing, and no
`#capture/web/{domain}` auto-tag yet.

### News email ingestion

```meta
type: sub-feature
status: draft
```

Poll an IMAP inbox for newsletters/summaries; auto-tag `#capture/email/{sender}`.

### Scheduled scans

```meta
type: sub-feature
status: draft
```

Run all monitors on a configurable schedule without manual intervention.

### Composable monitors

```meta
type: sub-feature
status: proposed
related: [.devbook/domain/capture/domain.md#source-adapter, .devbook/domain/monitoring/features.md#automation-status]
```

Let the user compose a monitor themselves — a trigger, a condition, and the
capture it produces — instead of choosing only from the monitors the product
ships with. A composed monitor carries the same schedule, retry/backoff, and
failure-logging guarantees as a built-in one and still delivers a normalized
capture, so nothing downstream can tell the two apart.

Recorded as a possibility, not a commitment. What prompted it is xyOps
(<https://github.com/pixlcore/xyops>), a self-hosted automation platform that
wires events, triggers, actions, and monitors into pipelines through a visual
editor and files the result carrying its own context. Whether this capability is
built in-product or obtained by running such a platform behind the
`Source Adapter` anti-corruption layer is a technology choice this chapter does
not settle. Either way the local-first rule holds: a composed monitor's source
credentials stay on the machine and never route through the optional Cloud
Service.

Boundary: this covers monitors watching sources *outside* the product. Signals
about the product's own health continue to reach the Inbox as Monitoring's
`FollowUpCaptured`, not as a capture.

## Web clipper capture

```meta
type: feature
status: draft
```

Browser extension or bookmarklet that clips web content — URL, title, selected
text, and page metadata — and converts it to markdown with the source link
preserved.

## IDE capture

```meta
type: feature
status: draft
```

Adapter that lets IDE-class hosts trigger a capture of selected code/text or an
in-session note, attaching file path, line number, and branch (or session and
worktree context) as context metadata. Covers both editor extensions (VS Code,
Visual Studio) and agentic session tools (GitHub Copilot App).

### Copilot App session capture

```meta
type: sub-feature
status: draft
related: [.devbook/domain/capture/domain.md#source-adapter]
```

Let a GitHub Copilot App session capture a backlog idea, follow-up, or
knowledge note directly from within its agent conversation, attaching the
session id, local worktree path, and current branch as context metadata. Runs
against the session's local worktree only, so no source credential leaves the
machine — the same local-first constraint that applies to the desktop's
inbound polling workers.

## Manual import

```meta
type: feature
status: draft
```

Drag-and-drop files or paste content directly, convert to markdown (MarkItDown
or equivalent), and extract tags, links, and source metadata automatically.

### Import manifest

```meta
type: sub-feature
status: draft
related: [.devbook/arc42/adr/0017-inbox-import-is-a-capture-source-with-a-markdown-manifest.md, .devbook/domain/capture/domain.md#capture-source, .devbook/domain/inbox/domain.md#inbox-list]
```

Bring the items from another tool into the Inbox by importing a manifest.
Microsoft To Do is the first tool. A skill outside the product reads the tool
and writes the manifest. It leaves out completed items, and it maps the tool's
lists to Inbox Lists. The manifest is Markdown with front matter. A `---` block
names the `schema` and the `tool`. Then comes one `#`-titled item per capture.
Each item has a `meta` fence carrying `external_id`, `captured_at`, and
optionally `kind`, `person`, and `list`, followed by its notes as the body.

Every item arrives as an `unprocessed` Inbox Item with source `import`. An item
is filed in the Inbox List its `list` names when that list exists. It lands
unfiled when the item names no list, or names one that does not exist.
Importing the same manifest again, or a later overlapping one, adds only the
items not already there. An item is known by its id, which comes from the tool
and the item's own id, and nothing records past imports.

The run answers with one line, in the form the feed monitors use:
`Import (microsoft-todo): 12 new items · 30 already known.` When there are
exceptions, the line adds them, such as
`2 left unfiled: no list "Errands"` or `1 skipped: no external_id`.

## Normalized delivery

```meta
type: feature
status: draft
related: [.devbook/domain/inbox/features.md#incoming-queue]
```

Every capture source produces a standard Inbox Item (title, `body_md`, source,
tags, `captured_at`) and delivers it to the Inbox incoming queue, preserving the
original source link and capture timestamp.

## In-app feedback capture

```meta
type: feature
status: draft
feature-flag: .devbook/domain/capture/context.md#feedback-reporting
related: [.devbook/domain/repository-management/features.md#github-access-resolution]
```

Report a problem with the app from inside the app, at the moment it happens.
The report carries a title, optional detail, and which area of the screen the
problem concerns, and the product attaches a picture of the current screen so
the reporter does not have to describe what they were looking at. The report is
filed as an issue against the product's own repository, and a failure to capture
the screen is stated in the report rather than silently dropping it.
