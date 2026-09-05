# Inbox

```meta
type: naming
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their
> aliases. Each term links to where it is modeled (`related`); the surface
> names it is also known by are recorded in the `aliases` metadata field so a
> synonym can always be resolved back to one canonical concept.

## Inbox Item

```meta
type: term
status: draft
aliases: [InboxItem]
related: [.domain/inbox/domain.md#inbox-item]
```

A captured piece of input awaiting triage. Distinct from a `Capture`: the Inbox
Item is created on intake and carries its own `received_at`, while the original
`captured_at` from Capture is preserved.

## Capture Source

```meta
type: term
status: draft
aliases: [CaptureSource, source]
related: [.domain/inbox/domain.md#capture-source]
```

Same published enum as the Capture context's Capture Source
(see `.domain/capture/naming.md#capture-source`); the Inbox conforms to it
rather than defining its own value set.

## Content Kind

```meta
type: term
status: draft
aliases: [InboxItemKind, CaptureKinds, kind]
related: [.domain/inbox/domain.md#content-kind]
```

What a captured thing *is* — text, article, link, youtube, image, document,
email, code, voice, claude-artifact — as opposed to `Capture Source`, which is
how it arrived. The shared component library spells the same values as slugs
(`CaptureKinds`); the Inbox's enum maps onto them.

## Source

```meta
type: term
status: draft
aliases: [InboxSource, person]
related: [.domain/inbox/domain.md#source]
```

The channel an item arrived through plus, optionally, the person who shared it
as a stored `@name` tag. The person is provenance, not a tag: it never appears in
an item's `Tag` set.

## PARA Lean

```meta
type: term
status: draft
aliases: [ParaCategory, Para, drawer]
related: [.domain/inbox/domain.md#para-lean]
```

The PARA drawer an unprocessed item is read under before triage routes it. The
same four values as Second Brain's `PARA Category`
(see `.domain/second-brain/naming.md`), restated because Inbox is upstream;
"drawer" is the reader's word for one of them on screen, and "unsorted" is the
absence of a lean.

## Triage

```meta
type: term
status: draft
aliases: [Triage]
related: [.domain/inbox/domain.md#triage]
```

The act of deciding an item's outcome (route, defer, or archive). A routed item
keeps the stored status `triaged`; `Routed` is a workflow outcome, not a stored
value (see `flow.md`).

## Routing Target

```meta
type: term
status: draft
aliases: [RoutingTarget]
related: [.domain/inbox/domain.md#routing-target]
```

The recorded destination (domain, optional `repo_id`) an item was routed to;
the Inbox never embeds the target aggregate itself.

## Inbox Status

```meta
type: term
status: draft
aliases: [InboxStatus]
related: [.domain/inbox/domain.md#inbox-status]
```

Stored state of an inbox item; see `flow.md` for the transitions.
