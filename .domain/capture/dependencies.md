# Dependencies: Capture

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the integration pattern for each
> relationship (synchronous call, domain/integration event, shared kernel,
> anti-corruption layer, etc.).

## Outbound dependencies

| Depends on (context/module) | Integration pattern | Why |
|---|---|---|
| [Inbox](../inbox/domain.md#aggregate-inbox-item) | Async domain event (`ItemCaptured`) | Capture delivers every normalized capture to the Inbox incoming queue; the Inbox owns the resulting Inbox Item lifecycle. |
| External sources (YouTube, websites/RSS, IMAP email, browser, IDE host) | Polling / event intake via source adapters (ACL) | Raw content is acquired from third-party systems and normalized behind adapters so their formats never leak downstream. |

## Inbound dependents (known)

| Consumer (context/module) | Integration pattern | What it relies on |
|---|---|---|
| [Inbox](../inbox/domain.md#aggregate-inbox-item) | Subscribes to `ItemCaptured` | Relies on the normalized Inbox Item shape (title, `body_md`, source, tags, `captured_at`) and preserved source link. |

## Notes

- The `Source Adapter` service is an anti-corruption layer: external formats
  (video metadata, RSS diffs, email MIME, IDE selections) are translated into the
  Capture shape so no external model crosses the boundary.
- Delivery to Inbox is intentionally one-way and fire-and-forget; Capture never
  reads Inbox state.
