# Inbox

```meta
type: dependencies
status: draft
related: [.arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md]
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Tasks](../tasks/domain.md#task) | OHS + Published Language (Inbox = supplier) | In-process port `IInboxBacklogTarget` (declared in `Backlog.Modules.Inbox.Abstractions`), answered by an infrastructure adapter over Tasks' published `ITaskItems` — not an async event | `.domain/inbox/domain.md#itemtriaged` (realised as `InboxRouteRequestDto`) | Routing an actionable item creates one draft Task per assigned repository, and a drafted plan is imported through Tasks' plan import, without the Inbox seeing Tasks or Tasks seeing the Inbox. |
| [Second Brain](../second-brain/domain.md#knowledge-note) | OHS + Published Language (Inbox = supplier) | Async `ItemTriaged` event (not built) | `.domain/inbox/domain.md#itemtriaged` | Routing a knowledge item creates a Knowledge Note through the same published language with a different route shape. |
| Sync (module, not a bounded context) | Customer/Supplier (Inbox = customer of the intake channel) | The desktop sync client hands every `capture`-kind replica document to the `IInboxIntake` port before the task merge, and drains the `IInboxCaptureOutbox` port into the ordinary tasks push as tombstones | `.arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md` | Captures made on the phone or in the IDE reach the desktop, and the phone learns a capture was dealt with, without a second sync path. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Capture](../capture/domain.md#capture) | OHS + Published Language (Capture = supplier) | Publishes `ItemCaptured` to the Inbox; on the desktop this arrives as a replica capture document through Sync | `.domain/capture/domain.md#itemcaptured` | Relies on the Inbox accepting normalized items into the incoming queue. |
| [Monitoring](../monitoring/domain.md#progress-signal) | Customer/Supplier (Monitoring = customer) | Read-side queue-health feed | `.domain/inbox/features.md#queue-health` | Relies on Inbox queue-health metrics (unprocessed count, oldest age, automation run status). |
| [Monitoring](../monitoring/domain.md#progress-signal) | OHS + Published Language (Monitoring = supplier) | Emits `FollowUpCaptured` back into the Inbox | `.domain/monitoring/domain.md#followupcaptured` | Dashboard follow-ups create new Inbox Items through a stable feedback contract. |

## Notes

- The Inbox is the hub of the capture -> triage -> backlog/knowledge/archive
  pipeline; keep the `ItemTriaged` payload as a published language so Tasks and
  Second Brain never depend on Inbox internals.
- The Inbox -> Tasks edge is realised the way the roadmap's cross-context joins
  are: a port in the Inbox's Abstractions, an adapter in `src/Infrastructure`
  that references both published surfaces, and no project reference between
  the two modules. The adapter is the only place an inbox item becomes entry
  text (`.arc42/adr/0002-backlog-module-owns-the-entry-text-language.md`). The
  earlier Tasks.UI -> Inbox.UI edge is gone.
- The Inbox's own store — `inbox_items`, `inbox_lists`, `inbox_groups` in
  `backlog.db` — is the module's; the adapter never touches another module's
  table (`.arc42/adr/guidelines/0014-persistence-and-repository-boundaries.md`).
  Lists and groups never sync.
- The inbox is global (one per workspace root), not repo-scoped — all sources
  deliver to one shared inbox regardless of origin; repositories are assigned
  per item, not per inbox.
- See the follow-up loop in `.domain/monitoring/flow.md#signal-flow` rather than
  duplicating it here.
