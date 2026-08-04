# Dependencies: Inbox

```meta
status: draft
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Backlog](../backlog/domain.md#aggregate-backlog-entry) | OHS + Published Language (Inbox = supplier) | Async `ItemTriaged` event | `.domain/inbox/domain.md#domain-event-itemtriaged` | Routing an actionable item creates a Backlog Entry draft without exposing Inbox internals. |
| [Second Brain](../second-brain/domain.md#aggregate-knowledge-note) | OHS + Published Language (Inbox = supplier) | Async `ItemTriaged` event | `.domain/inbox/domain.md#domain-event-itemtriaged` | Routing a knowledge item creates a Knowledge Note through the same published language with a different route shape. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Capture](../capture/domain.md#aggregate-capture) | OHS + Published Language (Capture = supplier) | Publishes `ItemCaptured` to the Inbox | `.domain/capture/domain.md#domain-event-itemcaptured` | Relies on the Inbox accepting normalized items into the incoming queue. |
| [Monitoring](../monitoring/domain.md#aggregate-progress-signal) | Customer/Supplier (Monitoring = customer) | Read-side queue-health feed | `.domain/inbox/features.md#feature-queue-health` | Relies on Inbox queue-health metrics (unprocessed count, oldest age, automation run status). |
| [Monitoring](../monitoring/domain.md#aggregate-progress-signal) | OHS + Published Language (Monitoring = supplier) | Emits `FollowUpCaptured` back into the Inbox | `.domain/monitoring/domain.md#domain-event-followupcaptured` | Dashboard follow-ups create new Inbox Items through a stable feedback contract. |

## Notes

- The Inbox is the hub of the capture -> triage -> backlog/knowledge/archive
  pipeline; keep the `ItemTriaged` payload as a published language so Backlog and
  Second Brain never depend on Inbox internals.
- The `.inbox/` folder is global (workspace root), not repo-scoped — all sources
  deliver to one shared inbox regardless of origin.
- See the follow-up loop in `.domain/monitoring/flow.md#signal-flow` rather than
  duplicating it here.
