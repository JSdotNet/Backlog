# Features: Capture

```meta
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: Mobile capture

```meta
status: draft
```

Frictionless capture from a phone while away from the desktop, with the minimum
required fields and offline-first behavior.

### Sub-feature: One-tap entry

```meta
status: draft
```

Rapid title + body capture with optional tags and source context.

### Sub-feature: Speech-to-text capture

```meta
status: draft
```

On-device transcription of voice notes into usable markdown, with retry on
transcription failure, preserving source metadata.

### Sub-feature: Offline-first sync

```meta
status: draft
```

Local storage of captures when offline and background synchronization when the
network returns.

### Sub-feature: Share-sheet and shortcuts

```meta
status: draft
```

Share-sheet and shortcut integration for quick clipping from other apps.

## Feature: Automation capture

```meta
status: draft
```

Unattended monitors that watch external sources and create captures on a
configurable schedule, with retry/backoff and failure logging.

### Sub-feature: YouTube monitor

```meta
status: draft
```

Poll subscribed channels for new videos; auto-tag `#capture/youtube` and file
under `inbox/incoming/youtube/{channel_name}/`.

### Sub-feature: Website monitor

```meta
status: draft
```

Watch configured URLs for content changes (RSS, DOM diff); auto-tag
`#capture/web/{domain}`.

### Sub-feature: News email ingestion

```meta
status: draft
```

Poll an IMAP inbox for newsletters/summaries; auto-tag `#capture/email/{sender}`.

### Sub-feature: Scheduled scans

```meta
status: draft
```

Run all monitors on a configurable schedule without manual intervention.

## Feature: Web clipper capture

```meta
status: draft
```

Browser extension or bookmarklet that clips web content — URL, title, selected
text, and page metadata — and converts it to markdown with the source link
preserved.

## Feature: IDE capture

```meta
status: draft
```

Adapter that lets IDE extensions trigger a capture from selected code/text,
attaching file path, line number, and branch as context metadata.

## Feature: Manual import

```meta
status: draft
```

Drag-and-drop files or paste content directly, convert to markdown (MarkItDown
or equivalent), and extract tags, links, and source metadata automatically.

## Feature: Normalized delivery

```meta
status: draft
related: [.domain/inbox/features.md#feature-incoming-queue]
```

Every capture source produces a standard Inbox Item (title, `body_md`, source,
tags, `captured_at`) and delivers it to the Inbox incoming queue, preserving the
original source link and capture timestamp.
