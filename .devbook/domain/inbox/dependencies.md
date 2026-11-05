# Inbox

```meta
type: dependencies
related: [.devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md]
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Tasks](../tasks/domain.md#task) | OHS + Published Language (Inbox = supplier) | In-process port `IInboxBacklogTarget`, answered by an adapter over `ITaskItems` | `.devbook/domain/inbox/domain.md#itemtriaged` (realised as `InboxRouteRequestDto`) | Routing an actionable item creates one draft Task per assigned repository. |
| [Brain](../brain/domain.md#knowledge-note) | OHS + Published Language (Inbox = supplier) | In-process port `IInboxBrainTarget`, answered by an adapter over `IBrainNoteWriter` | `.devbook/domain/inbox/domain.md#itemtriaged` (realised as `InboxBrainNoteRequestDto`) | Routing an item To Brain writes it into the Brain's inbox area as a Knowledge Note. |
| [Sync](../sync/context.md) | Customer/Supplier (Inbox = customer of the intake channel) | The `IInboxIntake` and `IInboxCaptureOutbox` ports, driven by the desktop sync client | `.devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md` | Captures reach the desktop, and the phone learns their fate, without a second sync path. |

- **Tasks:** `IInboxBacklogTarget` is declared in `Backlog.Modules.Inbox.Abstractions`. An infrastructure adapter answers it over Tasks' published `ITaskItems`, not through an async event. Several assigned repositories make sibling Tasks that name each other and share a `#from-inbox-` tag. A drafted plan is imported through Tasks' plan import. The Inbox never sees Tasks, and Tasks never sees the Inbox.
- **Brain:** `IInboxBrainTarget` is declared in `Backlog.Modules.Inbox.Abstractions`. An infrastructure adapter answers it over Devbook's `IBrainNoteWriter`, not through an async event. The Knowledge Note keeps the item's title and body, and goes through the writer the Devbook pane's Capture uses.
- **Sync:** The desktop sync client hands every `capture`-kind replica document to the `IInboxIntake` port before the task merge. It drains the `IInboxCaptureOutbox` port into the ordinary tasks push as tombstones. Those captures are the ones made on the phone or in the IDE, and a tombstone is how the phone learns a capture was dealt with.

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Capture](../capture/domain.md#capture) | OHS + Published Language (Capture = supplier) | Publishes `ItemCaptured` to the Inbox, through Sync or through the `IInboxIntake` port | `.devbook/domain/capture/domain.md#itemcaptured` | Relies on the Inbox accepting normalized items into the incoming queue, keyed by one per-entry id. |
| [Monitoring](../monitoring/domain.md#progress-signal) | Customer/Supplier (Monitoring = customer) | Read-side queue-health feed | `.devbook/domain/inbox/features.md#queue-health` | Relies on Inbox queue-health metrics (unprocessed count, oldest age, automation run status). |
| [Monitoring](../monitoring/domain.md#progress-signal) | OHS + Published Language (Monitoring = supplier) | Emits `FollowUpCaptured` back into the Inbox | `.devbook/domain/monitoring/domain.md#followupcaptured` | Dashboard follow-ups create new Inbox Items through a stable feedback contract. |

- **Capture:** From another device, `ItemCaptured` arrives as a replica capture document through Sync. On the desktop itself, a source monitor (YouTube channel, website) delivers it in-process through the port instead, with no replica document in between. The per-entry id is the same whichever path delivered the item.

## Notes

- The Inbox is the hub of the capture -> triage -> backlog/knowledge/archive
  pipeline. Its contract with Tasks and Devbook is the
  [`ItemTriaged`](domain.md#itemtriaged) published language.
- The Inbox -> Tasks edge is realised the way the roadmap's cross-context joins
  are. A port sits in the Inbox's Abstractions and an adapter in
  `src/Infrastructure` references both published surfaces. There is no project
  reference between the two modules.
- The Inbox's own store is the module's: `inbox_items`, `inbox_lists` and
  `inbox_groups` in `backlog.db`. The adapter never touches another module's
  table (`.devbook/arc42/adr/guidelines/0014-persistence-and-repository-boundaries.md`).
  Lists and groups never sync.
- The inbox is global, one per workspace root, and not scoped to a repository.
  Every source delivers to one shared inbox, and repositories are assigned per
  item.
- The follow-up loop is drawn in `.devbook/domain/monitoring/domain.md#signal-aggregation`.
