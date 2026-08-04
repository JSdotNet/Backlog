# Dependencies: Capture

```meta
status: draft
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#aggregate-inbox-item) | Customer/Supplier (Capture = customer of Inbox intake) | Async handoff into the Inbox intake pipeline | `.domain/capture/domain.md#domain-event-itemcaptured` | Capture depends on the Inbox accepting normalized captures and taking ownership of the resulting Inbox Item lifecycle. |
| External sources (YouTube, websites/RSS, IMAP email, browser, IDE-class hosts: VS Code, Visual Studio, GitHub Copilot App) | ACL | Polling / event intake via source adapters | `.domain/capture/domain.md#domain-service-source-adapter` | Raw content is acquired from third-party systems and normalized behind adapters so their formats never leak downstream. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#aggregate-inbox-item) | OHS + Published Language (Capture = supplier) | Subscribes to async `ItemCaptured` | `.domain/capture/domain.md#domain-event-itemcaptured` | Relies on the normalized capture shape (title, `body_md`, source, tags, `captured_at`) and preserved source link. |

## Notes

- The `Source Adapter` service is an anti-corruption layer: external formats
  (video metadata, RSS diffs, email MIME, IDE/agentic-session selections) are
  translated into the Capture shape so no external model crosses the boundary.
- The GitHub Copilot App adapter runs locally against the session's worktree,
  like the IDE extension adapters; it introduces no new external credential
  surface beyond what the editor adapters already have.
- Delivery to Inbox is intentionally one-way and fire-and-forget; Capture never
  reads Inbox state.
