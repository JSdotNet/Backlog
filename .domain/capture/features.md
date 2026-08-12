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

Adapter that lets IDE-class hosts trigger a capture of selected code/text or an
in-session note, attaching file path, line number, and branch (or session and
worktree context) as context metadata. Covers both editor extensions (VS Code,
Visual Studio) and agentic session tools (GitHub Copilot App).

### Sub-feature: Copilot App session capture

```meta
status: draft
related: [.domain/capture/domain.md#domain-service-source-adapter]
```

Let a GitHub Copilot App session capture a backlog idea, follow-up, or
knowledge note directly from within its agent conversation, attaching the
session id, local worktree path, and current branch as context metadata. Runs
against the session's local worktree only, so no source credential leaves the
machine — the same local-first constraint that applies to the desktop's
inbound polling workers.

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

## Feature: In-app feedback capture

```meta
status: draft
related: [.domain/repository-management/features.md#sub-feature-github-access-resolution]
```

Report a problem with the app from inside the app, at the moment it happens.
The report carries a title, optional detail, and which area of the screen the
problem concerns, and the product attaches a picture of the current screen so
the reporter does not have to describe what they were looking at. The report is
filed as an issue against the product's own repository, and a failure to capture
the screen is stated in the report rather than silently dropping it.
