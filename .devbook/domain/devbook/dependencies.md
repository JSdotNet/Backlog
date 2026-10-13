# Devbook

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
| [Tasks](../tasks/domain.md#task) | Partnership | Id-based cross-link and read-side embedding | `.devbook/domain/devbook/domain.md#cross-linking` | Notes link to and embed tasks; a note can spawn a task when an action is identified. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#inbox-item) | OHS + Published Language (Inbox = supplier) | Publishes `ItemTriaged` (knowledge route) | `.devbook/domain/inbox/domain.md#itemtriaged` | Relies on Devbook writing a Knowledge Note into the Brain's inbox area from an item routed **To Brain**, a capture or an Inbox note alike. |
| [Tasks](../tasks/domain.md#task) | Partnership | Reference/embed read model plus bi-directional link | `.devbook/domain/devbook/domain.md#cross-linking` | Relies on note content being embeddable and cross-queryable without sharing aggregates. |
| [Monitoring](../monitoring/domain.md#progress-signal) | Customer/Supplier (Monitoring = customer) | Knowledge-activity read-side feed | `.devbook/domain/devbook/domain.md#knowledge-note` | Knowledge activity contributes to the project health view; progress insights can be captured back as notes. |
| [Roadmap Planning](../roadmap/domain.md#roadmap-plan) | Customer/Supplier (Roadmap Planning = customer) | Read-side gather by `<path>#<slug>` reference and by roadmap tag | `.devbook/domain/devbook/domain.md#knowledge-note` | A Roadmap Item gathers the chapters it references (`knowledge_refs`) and the chapters whose `roadmap` list names its tag, and totals their registered `effort`. Relies on chapters being resolvable by reference or tag and on their `effort`/`roadmap` metadata; reads only, and never writes a chapter or owns an effort value. |

## Notes

- Links to Tasks are bi-directional but each side stores only the other's id;
  the Cross-Linking service reconciles both directions.
- A chapter's `roadmap` list is **not** a link and reconciles nothing, so it needs
  no consistency service. Each entry is a
  [Roadmap Contribution](domain.md#roadmap-contribution).
- A repository's devbook folders are read **in place and never owned**, as
  [A devbook that stays current](features.md#a-devbook-that-stays-current) says.
  That is why Roadmap Planning's gather reads chapters by reference and by tag
  rather than by reading an index. Which index exists, and whether one exists at
  all, is not part of the contract.
