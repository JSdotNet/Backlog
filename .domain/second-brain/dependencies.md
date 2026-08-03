# Dependencies: Second Brain

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the integration pattern for each
> relationship (synchronous call, domain/integration event, shared kernel,
> anti-corruption layer, etc.).

## Outbound dependencies

| Depends on (context/module) | Integration pattern | Why |
|---|---|---|
| [Backlog](../backlog/domain.md#aggregate-backlog-entry) | Id reference + bi-directional link | Notes link to and embed backlog entries; a note can spawn a backlog entry when an action is identified. |

## Inbound dependents (known)

| Consumer (context/module) | Integration pattern | What it relies on |
|---|---|---|
| [Inbox](../inbox/domain.md#aggregate-inbox-item) | Publishes `ItemTriaged` (knowledge route) | Relies on Second Brain creating a Knowledge Note from a triaged item. |
| [Backlog](../backlog/domain.md#aggregate-backlog-entry) | Reference / embed (read) + bi-directional link | Relies on note content being embeddable and cross-queryable. |
| [Monitoring](../monitoring/domain.md#aggregate-progress-signal) | Emits knowledge-activity signals | Knowledge activity contributes to the project health view; progress insights can be captured back as notes. |

## Notes

- Links to Backlog are bi-directional but each side stores only the other's id;
  the Cross-Linking service reconciles both directions.
- `.brain/` folders exist at workspace, project, and repo scope (see
  `domain-knowledge` folder conventions); cross-scope aggregation is by
  discovering all `.brain/` folders, not by a shared store.
