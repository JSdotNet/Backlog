# Dependencies: Roadmap Planning

```meta
status: draft
related: [.domain/context-map.md]
```

> Dependencies this bounded context has on other bounded contexts or modules, and
> known dependents. Note the DDD relationship pattern, integration mechanism, and
> published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Repository Management](../repository-management/domain.md#aggregate-repository-registry) | Customer/Supplier (Roadmap Planning = customer) | Registry lookup by repository alias, on the read path | `.domain/repository-management/naming.md#term-repository` | Repository Scope aliases resolve to configured repositories so a portfolio plan can be read one project at a time. Roadmap conforms to the registry's identity and never becomes a second authority for what a repository is. |
| [Backlog Management](../backlog/domain.md#aggregate-backlog-entry) | Partnership | Optional cross-link by foreign id, plus read-side gather by tag | `.domain/backlog/domain.md#aggregate-backlog-entry` | A planned item may name the entry that executes it, and also gathers every entry filed under its tag; over both it totals the entries' registered effort. Ids only, in both directions; neither side holds the other's aggregate and neither writes to it, and Roadmap reads effort it never registers. |
| [Second Brain](../second-brain/domain.md#aggregate-knowledge-note) | Customer/Supplier (Roadmap Planning = customer) | Read-side gather by `<path>#<slug>` reference and by tag | `.domain/second-brain/domain.md#aggregate-knowledge-note` | A Roadmap Item gathers the knowledge chapters it references directly (`knowledge_refs`) and the chapters whose own `roadmap` list names its tag, and totals their registered effort. Reads only, on the read path: Roadmap resolves chapters by reference or tag and reads the effort they registered, and never writes a chapter or owns an effort value. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Monitoring & Dashboard](../monitoring/domain.md#aggregate-progress-signal) | OHS + Published Language (Roadmap Planning = supplier) | Subscribes to `RoadmapItemScheduled` | `.domain/roadmap/domain.md#domain-event-roadmapitemscheduled` | Relies on planned windows, and on the previous window being carried, to compare intent against delivery. Breaks if the window stops being inclusive at both ends, or if the previous window is dropped. |
| [Backlog Management](../backlog/domain.md#aggregate-backlog-entry) | Partnership | Cross-link by foreign id | `.domain/roadmap/naming.md#term-roadmap-item` | Relies on `roadmap_item_id` staying stable across a reschedule, so a link made once keeps pointing at the same planned work. |

## Notes

- Repository aliases are held as **opaque strings**. Only
  [Repository Scope Resolution](domain.md#domain-service-repository-scope-resolution)
  resolves them, and it does so on the read path — so an unreachable or changed
  registry degrades the reading of a plan, never the plan itself.
- The Partnership with Backlog Management is the same shape as the existing
  Backlog ↔ [Second Brain](../second-brain/domain.md#domain-service-cross-linking)
  relationship: both sides keep only foreign ids, and the link semantics are
  coordinated rather than shared through an aggregate.
- Gathering and totalling read **foreign registered effort and own none of it.**
  [Roadmap Item Gathering](domain.md#domain-service-roadmap-item-gathering) reads
  Backlog Entries and knowledge chapters on the read path — by named reference and
  by tag — and adds the story points they registered. Backlog Management and Second
  Brain register the effort; Roadmap only totals it, and an unreachable supplier
  degrades a total rather than corrupting a plan.
- The tag vocabulary flows **out** of this context. A Roadmap Item's tag is the
  slug Backlog Management offers in its picker and a knowledge chapter names in its
  `roadmap` list; its stability across a rename is the contract those borrowings
  rest on. This context supplies the vocabulary and reads back what was filed under
  it — it does not learn the tag from either consumer.
- The direction of authority is worth stating twice, because the two contexts both
  use the word *priority*: **Backlog Management owns entry status and entry
  priority; Roadmap Planning owns planning priority and sequence.** Neither writes
  the other's value.
- Roadmap Planning publishes to Monitoring and subscribes to nothing. Nothing
  observed downstream reaches back in and edits a plan — a plan changes because a
  person changed it.
- There is no dependency on [Environment](../environment/domain.md#aggregate-environment-catalog).
  Surfacing environment shortcuts beside planned work is
  [Environment's own feature](../environment/features.md#feature-environment-aware-work-context),
  offered to whatever view asks for it, and does not put a Roadmap dependency in
  the model.
