# Second Brain

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
| [Backlog](../backlog/domain.md#backlog-entry) | Partnership | Id-based cross-link and read-side embedding | `.domain/second-brain/domain.md#cross-linking` | Notes link to and embed backlog entries; a note can spawn a backlog entry when an action is identified. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#inbox-item) | OHS + Published Language (Inbox = supplier) | Publishes `ItemTriaged` (knowledge route) | `.domain/inbox/domain.md#itemtriaged` | Relies on Second Brain creating a Knowledge Note from a triaged item. |
| [Backlog](../backlog/domain.md#backlog-entry) | Partnership | Reference/embed read model plus bi-directional link | `.domain/second-brain/domain.md#cross-linking` | Relies on note content being embeddable and cross-queryable without sharing aggregates. |
| [Monitoring](../monitoring/domain.md#progress-signal) | Customer/Supplier (Monitoring = customer) | Knowledge-activity read-side feed | `.domain/second-brain/domain.md#knowledge-note` | Knowledge activity contributes to the project health view; progress insights can be captured back as notes. |
| [Roadmap Planning](../roadmap/domain.md#roadmap-plan) | Customer/Supplier (Roadmap Planning = customer) | Read-side gather by `<path>#<slug>` reference and by roadmap tag | `.domain/second-brain/domain.md#knowledge-note` | A Roadmap Item gathers the chapters it references (`knowledge_refs`) and the chapters whose `roadmap` list names its tag, and totals their registered `effort`. Relies on chapters being resolvable by reference or tag and on their `effort`/`roadmap` metadata; reads only, and never writes a chapter or owns an effort value. |

## Notes

- Links to Backlog are bi-directional but each side stores only the other's id;
  the Cross-Linking service reconciles both directions.
- A chapter's `roadmap` list is **not** a link and reconciles nothing. It names
  Roadmap Item tag slugs owned by [Roadmap Planning](../roadmap/domain.md#roadmap-tag),
  not `<path>#<slug>` addresses, so it draws no edge and needs no consistency
  service — Roadmap reads it when gathering by tag, and a slug matching no current
  item is harmless. It is a different concept from a `Tag`, which is this context's
  own discovery `#keyword`.
- `.brain/` folders exist at workspace, project, and repo scope; cross-scope
  aggregation is by discovering all `.brain/` folders, not by a shared store.
- A repository's knowledge folders are read **in place and never owned**: they
  belong to the repository, are edited outside this context, and any prepared view
  over them is derived and must give way to the folder itself. That is why
  Roadmap Planning's gather is described as reading chapters by reference and by
  tag rather than as reading an index — which index exists, and whether one exists
  at all, is not part of the contract.
