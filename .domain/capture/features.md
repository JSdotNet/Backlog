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

### YouTube monitor

```meta
type: sub-feature
status: draft
```

Poll subscribed channels for new videos; auto-tag `#capture/youtube` and file
under `inbox/incoming/youtube/{channel_name}/`.

### Website monitor

```meta
type: sub-feature
status: draft
```

Watch configured URLs for content changes (RSS, DOM diff); auto-tag
`#capture/web/{domain}`.

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
related: [.domain/capture/domain.md#source-adapter]
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

## Normalized delivery

```meta
type: feature
status: draft
related: [.domain/inbox/features.md#incoming-queue]
```

Every capture source produces a standard Inbox Item (title, `body_md`, source,
tags, `captured_at`) and delivers it to the Inbox incoming queue, preserving the
original source link and capture timestamp.

## In-app feedback capture

```meta
type: feature
status: draft
feature-flag: feedback-reporting
related: [.domain/repository-management/features.md#github-access-resolution]
```

Report a problem with the app from inside the app, at the moment it happens.
The report carries a title, optional detail, and which area of the screen the
problem concerns, and the product attaches a picture of the current screen so
the reporter does not have to describe what they were looking at. The report is
filed as an issue against the product's own repository, and a failure to capture
the screen is stated in the report rather than silently dropping it.
