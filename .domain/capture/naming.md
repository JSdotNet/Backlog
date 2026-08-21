# Capture

```meta
type: naming
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their
> aliases. Each term links to where it is modeled (`related`); the surface
> names it is also known by are recorded in the `aliases` metadata field so a
> synonym can always be resolved back to one canonical concept.

## Capture

```meta
type: term
status: draft
aliases: [Capture, ItemCaptured]
related: [.domain/capture/domain.md#capture]
```

A single piece of raw input from any source before it reaches the Inbox.
`ItemCaptured` is the event that hands it off; the Inbox owns the resulting
Inbox Item's identity from that point on.

## Capture Source

```meta
type: term
status: draft
aliases: [CaptureSource, source]
related: [.domain/capture/domain.md#capture-source]
```

Which channel produced a capture (mobile, youtube, website, email, web_clipper,
ide, manual). This is a published enum shared with the Inbox — both contexts use
the same value set (see `.domain/inbox/naming.md#capture-source`). The
`ide` value covers every IDE-class host that captures via the Source Adapter's
IDE-family adapters: VS Code, Visual Studio, and the GitHub Copilot App (an
agentic session-management tool treated as a peer of the editor extensions).

## Source Adapter

```meta
type: term
status: draft
aliases: [Source Adapter]
related: [.domain/capture/domain.md#source-adapter]
```

The per-source component that normalizes external input into a `Capture` and
determines the concrete keys present in `SourceMetadata`.

## Source Metadata

```meta
type: term
status: draft
aliases: [SourceMetadata]
related: [.domain/capture/domain.md#source-metadata]
```

The source-specific context map attached to a capture; its keys depend on the
Capture Source.
