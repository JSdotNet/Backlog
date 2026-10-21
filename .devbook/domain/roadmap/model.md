# Roadmap Planning

```meta
type: model
status: draft
related: [.devbook/domain/roadmap/domain.md#roadmap-plan]
```

> Structural view of the domain model for this bounded context: aggregates,
> entities, value objects, and their relationships. Keep this in sync with
> `domain.md` (which describes responsibilities/invariants in prose) — this file
> focuses on structure and relationships. Lifecycle and process flows live in
> `flow.md`.

## Model diagram

```mermaid
classDiagram
    class RoadmapPlan {
        <<aggregate root>>
        +PlanId Id
        +RoadmapItem[] Items
        +Milestone[] Milestones
        +AddItem(...)
        +Reschedule(itemId, PlannedWindow)
        +Prioritise(itemId, PlanningPriority)
        +AddDependency(nodeId, dependsOnId)
        +RemoveDependency(nodeId, dependsOnId)
    }

    class RoadmapItem {
        <<entity>>
        +RoadmapItemId Id
        +string Title
        +RoadmapTag Tag
        +KnowledgeRef[] KnowledgeRefs
        +string? Notes
    }

    class Milestone {
        <<entity>>
        +RoadmapMilestoneId Id
        +string Title
        +DateOnly On
    }

    class PlannedWindow {
        <<value object>>
        +DateOnly Start
        +DateOnly End
        +int Days
    }

    class RepositoryScope {
        <<value object>>
        +string[] Aliases
    }

    class Dependency {
        <<value object>>
        +RoadmapNodeId DependsOnId
    }

    class PlanningLane {
        <<value object>>
        +string Name
    }

    class TaskLink {
        <<value object>>
        +Guid EntryId
    }

    class RoadmapTag {
        <<value object>>
        +string Slug
    }

    class KnowledgeRef {
        <<value object>>
        +string Path
        +string Slug
    }

    class PlanningPriority {
        <<enumeration>>
        Low
        Medium
        High
        Critical
    }

    class MilestoneKind {
        <<enumeration>>
        Release
        Freeze
        Review
        Commitment
    }

    class PlanSequencing {
        <<domain service>>
        +Order(RoadmapPlan)
        +Contradictions(RoadmapPlan)
        +WouldCycle(nodeId, dependsOnId)
    }

    class RepositoryScopeResolution {
        <<domain service>>
        +Resolve(RepositoryScope)
    }

    class RoadmapItemGathering {
        <<domain service>>
        +Gather(RoadmapItem) GatheredSet
        +TotalRegisteredEffort(GatheredSet)
        +UnestimatedCount(GatheredSet)
    }

    RoadmapPlan "1" *-- "0..*" RoadmapItem : owns
    RoadmapPlan "1" *-- "0..*" Milestone : owns

    RoadmapItem "1" *-- "1" PlannedWindow : has
    RoadmapItem "1" *-- "1" RoadmapTag : filed under
    RoadmapItem --> PlanningPriority : has
    RoadmapItem "1" *-- "1" RepositoryScope : has
    RoadmapItem "1" *-- "1" PlanningLane : filed under
    RoadmapItem "1" *-- "0..*" Dependency : waits on
    RoadmapItem "1" *-- "0..1" TaskLink : optionally names
    RoadmapItem "1" *-- "0..*" KnowledgeRef : references

    Milestone --> MilestoneKind : has
    Milestone "1" *-- "0..1" RepositoryScope : optionally has
    Milestone "1" *-- "0..1" PlanningLane : optionally filed under
    Milestone "1" *-- "0..*" Dependency : waits on

    Dependency ..> RoadmapItem : may point at
    Dependency ..> Milestone : may point at

    PlanSequencing ..> RoadmapPlan : reads
    RepositoryScopeResolution ..> RepositoryScope : reads
    RoadmapItemGathering ..> RoadmapItem : reads
    RoadmapItemGathering ..> RoadmapTag : gathers by
    RoadmapItemGathering ..> TaskLink : gathers by
    RoadmapItemGathering ..> KnowledgeRef : gathers by
```

## Relationship notes

- `RoadmapPlan` is a **singleton per workspace** — one plan per storage location,
  not one per repository or per project. There is no `Plan` selector in the model
  because a person has one set of intentions, read in different groupings.
- `RoadmapItem` and `Milestone` are owned entities, not aggregates. They are drawn
  with composition because they have no life outside the plan: deleting the plan
  deletes them, and neither can be loaded, validated, or saved on its own.
- `Dependency` points at a **Roadmap Node**, the union of `RoadmapItem` and
  `Milestone`, which is why both dotted associations leave the same class. There
  is no inheritance between item and milestone: they share an id space and a role
  as a dependency endpoint, and nothing else. Modelling a common base class would
  suggest a shared lifecycle they do not have.
- The dependency association is **one-way, and held by the waiting side**. The
  plan can answer "what depends on this" by scanning, and does; storing the
  reverse edge as well would create two places for one fact to be wrong.
- `RepositoryScope` holds aliases as **plain strings, by value** — never a
  reference to a `Repository` from
  [Repository Management](../repository-management/model.md). That is what keeps
  the two contexts independent, and it is why resolution is a service on the read
  path rather than a navigation in the model.
- `TaskLink` is likewise **an id, not an association**. There is no line
  from `RoadmapItem` to `Task` in this diagram, and there deliberately
  cannot be: they are separate aggregates in separate contexts, related by
  Partnership (see `dependencies.md`).
- `RoadmapTag` is a **value object held by the item, not a shared registry**.
  There is no `Tag` table the items point into, because two items may carry the
  same slug. The diagram draws no line from `Title` to `Tag`, because
  [renaming keeps the tag](domain.invariants.md#invariant-renaming-keeps-the-tag).
- `KnowledgeRef` is **an id-shaped reference, not an association**, for the same
  reason as `TaskLink`. The chapter it names lives in
  [Devbook](../devbook/model.md), so there is no line to it and no
  navigation through it. Both foreign references may dangle and neither is
  validated.
- `RoadmapItemGathering`, `PlanSequencing` and `RepositoryScopeResolution` are
  drawn as services because no answer they give is in a single node's own state.
  Gathering reads foreign tasks and chapters
  ([Roadmap Item Gathering](domain.md#roadmap-item-gathering)), sequencing needs
  the whole graph, and resolution needs a foreign registry. The dotted lines from
  gathering to `RoadmapTag`, `TaskLink`, and `KnowledgeRef` are the two ways it
  reaches work. The foreign tasks and chapters are not on this diagram, because
  they are not this context's model.
- Both enumerations are the plan's own. `PlanningPriority` shares its four values
  with Tasks's `Priority` on purpose, but the two are distinct types;
  there is no association between them and no conversion in either direction.
