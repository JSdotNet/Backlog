# Capture

```meta
type: dependencies
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#inbox-item) | Customer/Supplier (Capture = customer of Inbox intake) | Async handoff into the Inbox's [incoming queue](../inbox/features.md#incoming-queue) | `.devbook/domain/capture/domain.md#itemcaptured` | The Inbox accepts normalized captures and owns the resulting Inbox Item lifecycle. |
| External sources (listed below) | ACL | Polling / event intake via source adapters | `.devbook/domain/capture/domain.md#source-adapter` | Adapters acquire and normalize third-party content, so its formats never leak downstream. |

- **Inbox:** From another device a capture travels through the sync replica. On
  the desktop, a source monitor hands a new entry to the `IInboxIntake` port in-process.
- **External sources:**
  - YouTube;
  - websites/RSS;
  - IMAP email;
  - the browser;
  - IDE-class hosts: VS Code, Visual Studio, and the GitHub Copilot App.

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#inbox-item) | OHS + Published Language (Capture = supplier) | Subscribes to async `ItemCaptured` | `.devbook/domain/capture/domain.md#itemcaptured` | The normalized capture shape (title, `body_md`, source, tags, `captured_at`) and preserved source link. |

## Notes

- The `Source Adapter` service is an anti-corruption layer. It translates external
  formats into the Capture shape, so no external model crosses the boundary. Those formats are:
  - video metadata;
  - RSS diffs;
  - email MIME;
  - selections from an IDE or an agentic session.
- The GitHub Copilot App adapter is planned. It would run locally against the
  session's worktree, like the IDE extension adapters, with no new credential surface.
- Delivery to Inbox is intentionally one-way and fire-and-forget; Capture never
  reads Inbox state.
- [Composable monitors](features.md#composable-monitors) may one day run on an
  existing self-hosted automation platform, xyOps being the candidate. It would then sit behind the
  same `Source Adapter` layer and gain an outbound row above. The table omits it until that choice is made.
