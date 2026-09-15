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
aliases: [InboxItem, InboxItemDto, inbox_items]
related: [.domain/inbox/domain.md#inbox-item]
```

A captured piece of input awaiting triage. Distinct from a `Capture`: the Inbox
Item is created on intake and carries its own `received_at`, while the original
`captured_at` from Capture is preserved. An item that arrived through sync
reuses the capture's id; `inbox_items` is its table in `backlog.db`.

## Capture Source

```meta
type: term
status: draft
aliases: [CaptureSource, source, channel]
related: [.domain/inbox/domain.md#capture-source]
```

Same published enum as the Capture context's Capture Source
(see `.domain/capture/naming.md#capture-source`); the Inbox conforms to it
rather than defining its own value set. In code the value is the `channel`
string on `InboxSource`, normalised by `InboxEnumMap.NormalizeChannel` (the
editor extension's `vscode` files as `ide`).

## Capture (manual)

```meta
type: term
status: draft
aliases: [CaptureItemCommand, manual, ManualChannel]
related: [.domain/inbox/domain.md#capture-source, .domain/inbox/features.md#add-by-hand]
```

A thought typed straight into the desktop through the Inbox's Add dialog: a
title and, optionally, notes that become the item's body. It becomes an
`unprocessed` Inbox Item with channel `manual` and no replica behind it —
`CaptureItemCommand` is the slice, `InboxEnumMap.ManualChannel` the token.
Distinct from a `Capture` in the Capture context, which arrives from a device
through sync.

## Content Kind

```meta
type: term
status: draft
aliases: [ContentKind, CaptureKinds, kind, KindSlug]
related: [.domain/inbox/domain.md#content-kind]
```

What a captured thing *is* — text, article, link, youtube, image, document,
email, code, voice, claude-artifact — as opposed to `Capture Source`, which is
how it arrived. The shared component library spells the same values as slugs
(`CaptureKinds`); the Inbox's `ContentKind` enum maps onto them and an item
keeps the raw slug (`KindSlug`) so a kind this build does not know is shown as
its own word.

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

## List

```meta
type: term
status: draft
aliases: [InboxList, InboxListDto, list_id, inbox_lists]
related: [.domain/inbox/domain.md#inbox-list]
```

A named place a reader files waiting items, shown as a leaf in the pane's side
menu with a count of the open items it holds. The fixed **Inbox** entry above
the lists is not a list: it is the items whose `list_id` is empty.

## Group

```meta
type: term
status: draft
aliases: [InboxGroup, InboxGroupDto, group_id, inbox_groups]
related: [.domain/inbox/domain.md#inbox-group]
```

A fold in the side menu that holds lists. Membership lives on the list
(`group_id`); ungrouping moves the lists to the top level and removes the
group.

## Triage

```meta
type: term
status: draft
aliases: [Triage, RouteToBacklogCommand, CreatePlanCommand, ArchiveItemCommand]
related: [.domain/inbox/domain.md#triage]
```

The act of deciding an item's outcome (route, defer, or archive). A routed item
keeps the stored status `triaged`; `Routed` is a workflow outcome, not a stored
value (see `flow.md`). On the desktop the two routing doors are
`RouteToBacklogCommand` and `CreatePlanCommand`.

## Plan tag

```meta
type: term
status: draft
aliases: [PlanTag, import_plan_id]
related: [.domain/inbox/domain.md#triage, .arc42/adr/0007-import-reuses-the-entry-text-grammar.md]
```

The `#tag` every entry of a plan drafted from an item shares, which Tasks reads
as the plan's identity (`import_plan_id`). Shaped `{title-slug}-{last eight hex
digits of the item id}`, at most forty characters, so it is unique per item and
a legal tag on the metadata line.

## Routing Target

```meta
type: term
status: draft
aliases: [RoutingTarget, InboxRoutingDto]
related: [.domain/inbox/domain.md#routing-target]
```

The recorded destination (domain, `repo_ids`, `task_ids`, `routed_at`) an item
was routed to; the Inbox never embeds the target aggregate itself.

## Inbox Status

```meta
type: term
status: draft
aliases: [InboxStatus]
related: [.domain/inbox/domain.md#inbox-status]
```

Stored state of an inbox item; see `flow.md` for the transitions.
