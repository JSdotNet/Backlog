# Naming: Inbox

> Canonical ubiquitous-language terms for this bounded context and their
> aliases. Each term links to where it is modeled (`related`); the surface
> names it is also known by are recorded in the `aliases` metadata field so a
> synonym can always be resolved back to one canonical concept.

## Term: Inbox Item

```meta
status: draft
aliases: [InboxItem]
related: [.domain/inbox/domain.md#aggregate-inbox-item]
```

A captured piece of input awaiting triage. Distinct from a `Capture`: the Inbox
Item is created on intake and carries its own `received_at`, while the original
`captured_at` from Capture is preserved.

## Term: Capture Source

```meta
status: draft
aliases: [CaptureSource, source]
related: [.domain/inbox/domain.md#capture-source]
```

Same published enum as the Capture context's Capture Source
(see `.domain/capture/naming.md#term-capture-source`); the Inbox conforms to it
rather than defining its own value set.

## Term: Triage

```meta
status: draft
aliases: [Triage]
related: [.domain/inbox/domain.md#domain-service-triage]
```

The act of deciding an item's outcome (route, defer, or archive). A routed item
keeps the stored status `triaged`; `Routed` is a workflow outcome, not a stored
value (see `flow.md`).

## Term: Routing Target

```meta
status: draft
aliases: [RoutingTarget]
related: [.domain/inbox/domain.md#routing-target]
```

The recorded destination (domain, optional `repo_id`) an item was routed to;
the Inbox never embeds the target aggregate itself.

## Term: Inbox Status

```meta
status: draft
aliases: [InboxStatus]
related: [.domain/inbox/domain.md#inbox-status]
```

Stored state of an inbox item; see `flow.md` for the transitions.
