# Naming: Capture

```meta
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their
> aliases. Each term links to where it is modeled (`related`); the surface
> names it is also known by are recorded in the `aliases` metadata field so a
> synonym can always be resolved back to one canonical concept.

## Term: Capture

```meta
status: draft
aliases: [Capture, ItemCaptured]
related: [.domain/capture/domain.md#aggregate-capture]
```

A single piece of raw input from any source before it reaches the Inbox.
`ItemCaptured` is the event that hands it off; the Inbox owns the resulting
Inbox Item's identity from that point on.

## Term: Capture Source

```meta
status: draft
aliases: [CaptureSource, source]
related: [.domain/capture/domain.md#capture-source]
```

Which channel produced a capture (mobile, youtube, website, email, web_clipper,
ide, manual). This is a published enum shared with the Inbox — both contexts use
the same value set (see `.domain/inbox/naming.md#term-capture-source`).

## Term: Source Adapter

```meta
status: draft
aliases: [Source Adapter]
related: [.domain/capture/domain.md#domain-service-source-adapter]
```

The per-source component that normalizes external input into a `Capture` and
determines the concrete keys present in `SourceMetadata`.

## Term: Source Metadata

```meta
status: draft
aliases: [SourceMetadata]
related: [.domain/capture/domain.md#source-metadata]
```

The source-specific context map attached to a capture; its keys depend on the
Capture Source.
