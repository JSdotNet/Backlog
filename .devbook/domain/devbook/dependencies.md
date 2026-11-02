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
| [Sync](../sync/context.md) | OHS + Published Language (Sync = supplier) | `AnnotationSyncWorker` in `Backlog.Infrastructure.Sync` pushes and pulls remarks through the sync service's third replica container; `AnnotationReplicaMerge` applies each pulled one to the annotation store | `AnnotationChange` and `AnnotationPayload` in `Backlog.Modules.Sync.Abstractions`; `.devbook/domain/sync/domain.md#change-precedence`; local ADR 0011 | A [Remark](domain.md#remark) made on one desktop reaches the person's other desktops on a task's terms: whole, and never moved backwards by a push. Sync stores it without reading it; where a remark anchors, and what it says, stay with Devbook. |
| [Sync](../sync/context.md) | OHS + Published Language (Sync = supplier) | `DevbookSubjectSyncWorker` in `Backlog.Infrastructure.Sync` pushes and pulls subject states through a replica container of its own on the sync service, `devbookSubjects`; `DevbookSubjectReplicaMerge` applies each pulled one to the subject state store | `DevbookSubjectChange` and `DevbookSubjectPayload` in `Backlog.Modules.Sync.Abstractions`; `.devbook/domain/sync/domain.md#change-precedence`; local ADR 0044 | A [Subject State](domain.md#subject-state) set on one desktop reaches the person's other desktops whole, under the one id its subject has on every device, and a push never moves it backwards. A service that does not have the route yet is a quiet "not yet", not a failure. Sync stores the state without reading it; which states a subject may be in, and what approving one means, stay with Devbook. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Brain](../brain/context.md) | Customer/Supplier (Brain = customer), the devbook convention as Published Language | In-process: the Markdown and `meta` reader, the database builder and refresher, the chapter viewer, search and remarks, pointed at the Brain folder | The devbook convention (local ADR 0016); local ADR 0040 | Relies on reading, indexing, rendering and searching a Brain note as it does a repository chapter, and on the Brain's three fields (`para`, `repos`, `tasks`) being carried to the note's node. A repo link into a repository chapter is read one way; Devbook never links back. |
| [Roadmap Planning](../roadmap/domain.md#roadmap-plan) | Customer/Supplier (Roadmap Planning = customer) | Read-side gather by `<path>#<slug>` reference and by roadmap tag | `.devbook/domain/devbook/domain.md#shared-value-objects` | A Roadmap Item gathers the chapters it references (`knowledge_refs`) and the chapters whose `roadmap` list names its tag, and totals their registered `effort`. Relies on chapters being resolvable by reference or tag and on their `effort`/`roadmap` metadata; reads only, and never writes a chapter or owns an effort value. |

## Notes

- A chapter's `roadmap` list is **not** a link and reconciles nothing, so it needs
  no consistency service. Each entry is a
  [Roadmap Contribution](domain.md#roadmap-contribution).
- A repository's devbook folders are read **in place and never owned**, as
  [A devbook that stays current](features.md#a-devbook-that-stays-current) says.
  That is why Roadmap Planning's gather reads chapters by reference and by tag
  rather than by reading an index. Which index exists, and whether one exists at
  all, is not part of the contract.
