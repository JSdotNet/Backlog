# Dependencies: Backlog Management

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the integration pattern for each
> relationship (synchronous call, domain/integration event, shared kernel,
> anti-corruption layer, etc.).

## Outbound dependencies

| Depends on (context/module) | Integration pattern | Why |
|---|---|---|
| GitHub (external) | REST call via Projection service (ACL) | Multi-repo entries project to one GitHub issue per target repo; status syncs bidirectionally. |
| Copilot CLI (external) | Command/task projection via Projection service (ACL) | Entries can project to one CLI task per target repo. |
| [Second Brain](../second-brain/domain.md#aggregate-knowledge-note) | Reference / embed (read) | Entries embed or deep-link Knowledge Note content for context; queries can span both. |
| [Repository Management](../repository-management/domain.md#aggregate-repository-registry) | Id reference (repo registry lookup) | `repo_ids` resolve to registered repos and their local clone paths. |

## Inbound dependents (known)

| Consumer (context/module) | Integration pattern | What it relies on |
|---|---|---|
| [Inbox](../inbox/domain.md#aggregate-inbox-item) | Publishes `ItemTriaged` | Relies on Backlog creating a draft entry from a triaged item. |
| [Second Brain](../second-brain/domain.md#aggregate-knowledge-note) | Bi-directional link | Notes link to entries and entries link back; either can spawn the other. |
| [Monitoring](../monitoring/domain.md#aggregate-progress-signal) | Subscribes to status-change / projection signals | Relies on `StatusChanged`, `EntryProjected`, and `EntryCompleted` for progress signals and GitHub-sync comparison. |

## Notes

- Keep `repo_ids` as opaque identifiers so Backlog does not couple to Repository
  Management internals; only the Projection service resolves them for GitHub/CLI.
- GitHub issue sync is a two-way relationship — flag the mismatch-detection rule
  (backlog says done vs. issue still open) as owned jointly with Monitoring.
- The `ItemTriaged` payload is Inbox's published language; treat it as a stable
  contract, not an Inbox internal.
