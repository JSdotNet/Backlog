# Capture

```meta
type: dependencies
status: draft
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#inbox-item) | Customer/Supplier (Capture = customer of Inbox intake) | Async handoff into the Inbox's [incoming queue](../inbox/features.md#incoming-queue). From another device it travels through the sync replica. On the desktop, a source monitor hands a new entry to the Inbox's `IInboxIntake` port in-process. | `.devbook/domain/capture/domain.md#itemcaptured` | Capture depends on the Inbox accepting normalized captures and taking ownership of the resulting Inbox Item lifecycle. |
| External sources (YouTube, websites/RSS, IMAP email, browser, IDE-class hosts: VS Code, Visual Studio, GitHub Copilot App) | ACL | Polling / event intake via source adapters | `.devbook/domain/capture/domain.md#source-adapter` | Raw content is acquired from third-party systems and normalized behind adapters so their formats never leak downstream. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#inbox-item) | OHS + Published Language (Capture = supplier) | Subscribes to async `ItemCaptured` | `.devbook/domain/capture/domain.md#itemcaptured` | Relies on the normalized capture shape (title, `body_md`, source, tags, `captured_at`) and preserved source link. |

## Notes

- The `Source Adapter` service is an anti-corruption layer. It translates external
  formats into the Capture shape, so no external model crosses the boundary. Those
  formats are video metadata, RSS diffs, email MIME, and selections from an IDE or
  an agentic session.
- The GitHub Copilot App adapter is planned. It would run locally against the
  session's worktree, like the IDE extension adapters, and would add no external
  credential surface beyond what the editor adapters already have.
- Delivery to Inbox is intentionally one-way and fire-and-forget; Capture never
  reads Inbox state.
- [Composable monitors](features.md#composable-monitors) may one day run on an
  existing self-hosted automation platform instead of scheduling in-product. xyOps
  is the candidate that prompted the idea. That platform would then sit behind the
  same `Source Adapter` anti-corruption layer as the other external sources, and
  gain an outbound row above. It is absent from the table until that choice is made.
