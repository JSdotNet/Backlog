# Dependencies: Inbox

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the integration pattern for each
> relationship (synchronous call, domain/integration event, shared kernel,
> anti-corruption layer, etc.).

## Outbound dependencies

| Depends on (context/module) | Integration pattern | Why |
|---|---|---|
| [Backlog](../backlog/domain.md#aggregate-backlog-entry) | Async domain event (`ItemTriaged`) | Routing an actionable item creates a Backlog Entry draft. |
| [Second Brain](../second-brain/domain.md#aggregate-knowledge-note) | Async domain event (`ItemTriaged`) | Routing a knowledge item creates a Knowledge Note. |

## Inbound dependents (known)

| Consumer (context/module) | Integration pattern | What it relies on |
|---|---|---|
| [Capture](../capture/domain.md#aggregate-capture) | Publishes `ItemCaptured` to the Inbox | Relies on the Inbox accepting normalized items into the incoming queue. |
| [Monitoring](../monitoring/domain.md#aggregate-progress-signal) | Subscribes to queue-depth / age signals | Relies on Inbox queue-health metrics (unprocessed count, oldest age, automation run status). |
| [Monitoring](../monitoring/domain.md#aggregate-progress-signal) | Emits `FollowUpCaptured` back into the Inbox | Dashboard follow-ups create new Inbox Items. |

## Notes

- The Inbox is the hub of the capture → triage → backlog/knowledge/archive
  pipeline; keep the `ItemTriaged` payload as a published language so Backlog and
  Second Brain never depend on Inbox internals.
- The `.inbox/` folder is global (workspace root), not repo-scoped — all sources
  deliver to one shared inbox regardless of origin.
- See the interaction diagram in `.domain/monitoring/model.md` for the
  Monitoring ↔ Inbox follow-up loop rather than duplicating it here.
