# Dependencies: Second Brain

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Backlog](../backlog/domain.md#aggregate-backlog-entry) | Partnership | Id-based cross-link and read-side embedding | `.domain/second-brain/domain.md#domain-service-cross-linking` | Notes link to and embed backlog entries; a note can spawn a backlog entry when an action is identified. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#aggregate-inbox-item) | OHS + Published Language (Inbox = supplier) | Publishes `ItemTriaged` (knowledge route) | `.domain/inbox/domain.md#domain-event-itemtriaged` | Relies on Second Brain creating a Knowledge Note from a triaged item. |
| [Backlog](../backlog/domain.md#aggregate-backlog-entry) | Partnership | Reference/embed read model plus bi-directional link | `.domain/second-brain/domain.md#domain-service-cross-linking` | Relies on note content being embeddable and cross-queryable without sharing aggregates. |
| [Monitoring](../monitoring/domain.md#aggregate-progress-signal) | Customer/Supplier (Monitoring = customer) | Knowledge-activity read-side feed | `.domain/second-brain/domain.md#aggregate-knowledge-note` | Knowledge activity contributes to the project health view; progress insights can be captured back as notes. |

## Notes

- Links to Backlog are bi-directional but each side stores only the other's id;
  the Cross-Linking service reconciles both directions.
- `.brain/` folders exist at workspace, project, and repo scope; cross-scope
  aggregation is by discovering all `.brain/` folders, not by a shared store.
