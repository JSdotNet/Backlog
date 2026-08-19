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
| [Backlog Management](../backlog/domain.md#aggregate-backlog-entry) | Partnership | Optional cross-link by foreign id | `.domain/backlog/domain.md#aggregate-backlog-entry` | A planned item may name the entry that executes it, so the plan can show real progress. Ids only, in both directions; neither side holds the other's aggregate and neither writes to it. |

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
