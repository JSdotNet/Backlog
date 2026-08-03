# Features: Capture

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: Mobile capture

```meta
status: draft
depends-on: []
related: []
issue: null
```

Frictionless capture from a phone while away from the desktop, with the minimum
required fields and offline-first behavior.

### Sub-feature: One-tap entry

```meta
status: draft
depends-on: []
related: []
issue: null
```

Rapid title + body capture with optional tags and source context.

### Sub-feature: Speech-to-text capture

```meta
status: draft
depends-on: []
related: []
issue: null
```

On-device transcription of voice notes into usable markdown, with retry on
transcription failure, preserving source metadata.

### Sub-feature: Offline-first sync

```meta
status: draft
depends-on: []
related: []
issue: null
```

Local storage of captures when offline and background synchronization when the
network returns.

### Sub-feature: Share-sheet and shortcuts

```meta
status: draft
depends-on: []
related: []
issue: null
```

Share-sheet and shortcut integration for quick clipping from other apps.

## Feature: Automation capture

```meta
status: draft
depends-on: []
related: []
issue: null
```

Unattended monitors that watch external sources and create captures on a
configurable schedule, with retry/backoff and failure logging.

### Sub-feature: YouTube monitor

```meta
status: draft
depends-on: []
related: []
issue: null
```

Poll subscribed channels for new videos; auto-tag `#capture/youtube` and file
under `inbox/incoming/youtube/{channel_name}/`.

### Sub-feature: Website monitor

```meta
status: draft
depends-on: []
related: []
issue: null
```

Watch configured URLs for content changes (RSS, DOM diff); auto-tag
`#capture/web/{domain}`.

### Sub-feature: News email ingestion

```meta
status: draft
depends-on: []
related: []
issue: null
```

Poll an IMAP inbox for newsletters/summaries; auto-tag `#capture/email/{sender}`.

### Sub-feature: Scheduled scans

```meta
status: draft
depends-on: []
related: []
issue: null
```

Run all monitors on a configurable schedule without manual intervention.

## Feature: Web clipper capture

```meta
status: draft
depends-on: []
related: []
issue: null
```

Browser extension or bookmarklet that clips web content — URL, title, selected
text, and page metadata — and converts it to markdown with the source link
preserved.

## Feature: IDE capture

```meta
status: draft
depends-on: []
related: []
issue: null
```

Adapter that lets IDE extensions trigger a capture from selected code/text,
attaching file path, line number, and branch as context metadata.

## Feature: Manual import

```meta
status: draft
depends-on: []
related: []
issue: null
```

Drag-and-drop files or paste content directly, convert to markdown (MarkItDown
or equivalent), and extract tags, links, and source metadata automatically.

## Feature: Normalized delivery

```meta
status: draft
depends-on: []
related: [.domain/inbox/features.md#feature-incoming-queue]
issue: null
```

Every capture source produces a standard Inbox Item (title, `body_md`, source,
tags, `captured_at`) and delivers it to the Inbox incoming queue, preserving the
original source link and capture timestamp.
