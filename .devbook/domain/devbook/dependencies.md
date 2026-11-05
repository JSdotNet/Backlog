# Devbook

```meta
type: dependencies
```

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Sync](../sync/context.md), for remarks | OHS + Published Language (Sync = supplier) | `AnnotationSyncWorker` pushes and pulls remarks through the sync service's third replica container | `AnnotationChange` and `AnnotationPayload` in `Backlog.Modules.Sync.Abstractions`; `.devbook/domain/sync/domain.md#change-precedence`; local ADR 0011 | A [Remark](domain.md#remark) made on one desktop reaches the person's other desktops. |
| [Sync](../sync/context.md), for subject states | OHS + Published Language (Sync = supplier) | `DevbookSubjectSyncWorker` pushes and pulls subject states through their own replica container, `devbookSubjects` | `DevbookSubjectChange` and `DevbookSubjectPayload` in `Backlog.Modules.Sync.Abstractions`; `.devbook/domain/sync/domain.md#change-precedence`; local ADR 0044 | A [Subject State](domain.md#subject-state) set on one desktop reaches the person's other desktops. |

- **Sync, for remarks:** `AnnotationSyncWorker` and `AnnotationReplicaMerge` live in
  `Backlog.Infrastructure.Sync`, and the latter applies each pulled remark to the annotation store. A
  remark travels on a task's terms: whole, and never moved backwards by a push.
  Sync stores it unread; its anchor and text stay with Devbook.
- **Sync, for subject states:** `DevbookSubjectSyncWorker` and
  `DevbookSubjectReplicaMerge` live in `Backlog.Infrastructure.Sync`, and the latter applies each pulled
  state to the subject state store. A state arrives whole, under the one id its
  subject has on every device, and a push never moves it backwards. A service
  without the route yet answers a quiet "not yet", not a failure. Sync stores the
  state unread; the allowed states and what approving one means stay with Devbook.

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Brain](../brain/context.md) | Customer/Supplier (Brain = customer), the devbook convention as Published Language | In-process: this context's reading machinery, pointed at the Brain folder | The devbook convention (local ADR 0016); local ADR 0040 | Reading, indexing, rendering and searching a Brain note as it does a repository chapter. |
| [Roadmap Planning](../roadmap/domain.md#roadmap-plan) | Customer/Supplier (Roadmap Planning = customer) | Read-side gather by `<path>#<slug>` reference and by roadmap tag | `.devbook/domain/devbook/domain.md#shared-value-objects` | Chapters resolvable by reference or tag, and their `effort` and `roadmap` metadata. |

- **Brain:** The Brain also relies on its three fields (`para`, `repos`, `tasks`)
  being carried to the note's node. A repo link into a repository chapter is read
  one way, and Devbook never links back. The reading machinery is:
  - the Markdown and `meta` reader;
  - the database builder and refresher;
  - the chapter viewer;
  - search and remarks.
- **Roadmap Planning:** A Roadmap Item gathers the chapters it references
  (`knowledge_refs`) and those whose `roadmap` list names its tag, and totals their
  `effort`. It only reads, never writing a chapter or owning an effort value.

## Notes

- A chapter's `roadmap` list is **not** a link and reconciles nothing, so it needs
  no consistency service. Each entry is a
  [Roadmap Contribution](domain.md#roadmap-contribution).
- A repository's devbook folders are read **in place and never owned**
  ([A devbook that stays current](features.md#a-devbook-that-stays-current)), so
  Roadmap Planning's gather reads chapters by reference and by tag, not by an
  index. Whether an index exists is no part of the contract.
