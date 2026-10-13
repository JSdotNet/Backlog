# Invariants

```meta
status: draft
type: invariants
```

What each aggregate and domain service on [`domain.md`](domain.md) enforces, one chapter each: each rule, where it is enforced, and the unit test that proves it.

## Roadmap Plan

```meta
status: draft
type: invariants
related: [.devbook/domain/roadmap/domain.md#roadmap-plan]
```

> The rules the Roadmap Plan holds its items, milestones and dependencies to, including those its [Planned Window](domain.md#planned-window), [Milestone](domain.md#milestone), [Roadmap Tag](domain.md#roadmap-tag), [Repository Scope](domain.md#repository-scope), [Planning Lane](domain.md#planning-lane) and [Dependency](domain.md#dependency) enforce.

### Invariant: An empty plan is valid

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapPlanTests.EmptyPlan_IsAValidPlan
```

A plan with no items and no milestones is a valid plan.
It is what a first run holds, and what is left once everything has been delivered.

Enforced at: `RoadmapPlan.Empty()`

### Invariant: A cycle is refused

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanDependencyTests.ADirectCycleIsRefused_AndThePlanIsLeftAsItWas, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanDependencyTests.AnIndirectCycleIsRefused_HoweverManyStepsAway, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanDependencyTests.ACycleThroughAMilestoneIsRefusedToo]
```

A dependency that would close a cycle, directly or through any number of nodes, is refused and leaves the plan as it was (`roadmap.cyclic_dependency`).
A cycle already written into a hand-edited file is read as it stands, which is why the rule is the refusal rather than a property of every loaded plan.

Enforced at: `AddDependency()`

### Invariant: A dependency names a node in the plan

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanDependencyTests.ADependencyOnSomethingNotInThePlan_IsRefused, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RehydrationTests.ADanglingDependencyIsDroppedRatherThanFailingTheLoad, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapPlanTests.RemovingAnItem_AlsoForgetsEveryDependencyOnIt, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapPlanTests.RemovingAMilestone_AlsoForgetsEveryDependencyOnIt]
```

A dependency names an item or milestone in this plan; an edge to an unknown id is refused (`roadmap.node_not_found`).
An edge to nothing is not a weaker plan, it is a wrong one.

Enforced at: `AddDependency()`; `Rehydrate()` drops a dangling edge, and `RemoveItem()` and `RemoveMilestone()` forget every edge to the node they remove

### Invariant: Nothing waits for itself

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanDependencyTests.NothingCanWaitForItself
```

A dependency of a node on itself is refused (`roadmap.self_dependency`).

Enforced at: `AddDependency()`

### Invariant: A dependency declared twice is one dependency

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanDependencyTests.DeclaringTheSameDependencyTwice_ChangesNothingAndSucceeds
```

Declaring a dependency the plan already holds changes nothing and succeeds, so a node never waits twice on the same node.

Enforced at: `AddDependency()`; `Dependencies.Of()` collapses duplicates when a plan is loaded

### Invariant: A window does not end before it starts

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlannedWindowTests.AWindowThatEndsBeforeItStarts_IsRefusedRatherThanThrown
```

A Planned Window ends on or after the day it starts; two dates that do not make one are refused (`roadmap.invalid_window`).

Enforced at: constructor (`PlannedWindow`): `Create()` refuses, and `Of()` clamps the end to the start

### Invariant: A window counts both its days

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlannedWindowTests.BothEndsAreInclusive_SoASingleDayIsOneDayLong
```

Both days of a Planned Window are inclusive, so a single-day window is one day long rather than none.

Enforced at: `PlannedWindow.Days`

### Invariant: A milestone is a day

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapPlanTests.AMilestoneIsADay_NotAOneDayItem
```

A Milestone falls on exactly one day and has no duration, which is why it is not a one-day item.

Enforced at: constructor (`Milestone`, which holds a single `On` date and no window)

### Invariant: Planning priority never touches the task

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapPlanTests.PlanningPriority_IsThePlansOwn_AndDoesNotTouchTheLinkedEntry
```

Setting an item's Planning Priority never writes to its linked Task.
The two are different judgements made for different reasons, and collapsing them would mean reprioritising a plan by touching an issue.

Enforced at: `Prioritise()`

### Invariant: The task priority never overwrites the plan

```meta
status: draft
type: invariant
```

A Task's own priority never changes an item's Planning Priority.

Enforced at: all mutations (`RoadmapItem.Prioritise()` is internal, and none of the plan's operations that call it reads a Task)

### Invariant: An unresolved scope alias is kept

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RehydrationTests.AnAliasThatNoLongerResolves_IsKeptRatherThanDeleted
```

A Repository Scope alias the Repository Registry no longer resolves is kept, and reads as unresolved.
Dropping it would silently delete something the person wrote because a supplier's registry changed.

Enforced at: constructor (`RepositoryScope`): `Of()` normalizes the aliases and never looks one up

### Invariant: Scope aliases are normalized and distinct

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RepositoryScopeTests.AliasesAreNormalizedTheWayTheSettingsStoreWritesThem, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RepositoryScopeTests.DuplicatesCollapse_AndTypingOrderDoesNotAffectEquality]
```

A Repository Scope holds each alias once, trimmed and in lower case, and two scopes naming the same aliases are equal whatever order they were typed in.
The aliases are written the way the settings store writes them, so a scope and the registry compare without translation.

Enforced at: constructor (`RepositoryScope`): `Of()`

### Invariant: An empty scope is unfiled

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RepositoryScopeTests.AnEmptyScopeIsUnfiled_NotAnError
```

A Repository Scope with no alias, or only blank ones, is valid and means the work is under no repository.
It is neither an error nor a default repository.

Enforced at: constructor (`RepositoryScope`): `Of()` returns `Unfiled`

### Invariant: A blank lane is the default lane

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanningLaneTests.BlankIsTheDefaultLane, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanningLaneTests.ALaneIsWhateverThePersonCalledIt]
```

A blank Planning Lane is the default lane, and any other name is kept as the person wrote it, trimmed.

Enforced at: constructor (`PlanningLane`): `Of()`

### Invariant: A task link is only an id

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapPlanTests.PlanningPriority_IsThePlansOwn_AndDoesNotTouchTheLinkedEntry
```

A Task Link is a foreign id, and the plan never reads through it to decide anything about itself.

Enforced at: constructor (`RoadmapItem.TaskId` is a bare `Guid?`, set by `LinkTo()`; the module has no Tasks reader)

### Invariant: Every item has a tag

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanTaggingTests.AnItemAddedWithoutATag_GetsOneDerivedFromItsTitle
```

Every Roadmap Item has exactly one Roadmap Tag, derived from its title when none is given.

Enforced at: constructor (`RoadmapItem`)

### Invariant: Renaming keeps the tag

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanTaggingTests.RenamingTheTitle_LeavesTheTagAlone, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanTaggingTests.UpdatingWithNoTag_RederivesFromTheTitle_BecauseTheFieldWasCleared]
```

Renaming an item's title never changes its tag.
An update that clears the tag field is an edit of the tag rather than a rename, and derives a new one from the title.

Enforced at: `Rename()`

### Invariant: A tag is lowercase kebab-case

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanningTagTests.EveryProducedTagMatchesTheAgreedShape, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanningTagTests.AnExplicitValueIsNormalizedThroughTheSameRules, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanningTagTests.DiacriticsAreFoldedRatherThanDropped]
```

A Roadmap Tag is lowercase kebab-case, whether it was derived from a title or typed by the person.
A tag typed by hand goes through the same slug rule as a derived one, so the two cannot drift apart.

Enforced at: constructor (`PlanningTag`): `From()` and `Of()` slugify every value

### Invariant: A title that slugifies to nothing gets the tag item

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanningTagTests.ATitleThatSlugifiesToNothing_FallsBackToAStableValue
```

A title the slug rule reduces to nothing gets the tag `item`.
There must always be something to file work against, and punctuation, an emoji, or a script the slug rule strips would otherwise leave an empty tag.

Enforced at: constructor (`PlanningTag`): `From()` and `Of()`

### Invariant: A knowledge ref is never read through

```meta
status: draft
type: invariant
```

The plan never reads through a Knowledge Ref to decide anything about itself.

Enforced at: constructor (`KnowledgeReferences`, which holds strings nothing in `RoadmapPlan` resolves)

### Invariant: Links are never validated or repaired

```meta
status: draft
type: invariant
```

A Task Link or a Knowledge Ref may name something that no longer exists, and neither is validated or repaired.

Enforced at: constructor (`RoadmapItem.TaskId` is a bare `Guid?`, and `KnowledgeReferences.Of()` only trims and removes duplicates)

### Invariant: All changes go through the plan

```meta
status: draft
type: invariant
```

Items, milestones and dependencies change only through the plan.

Enforced at: all mutations (every mutator on `RoadmapItem`, `Milestone` and `Dependencies` is internal)

## Roadmap Item Gathering

```meta
status: draft
type: invariants
related: [.devbook/domain/roadmap/domain.md#roadmap-item-gathering]
```

> The rules Roadmap Item Gathering holds its totals to, whoever asks for them.

### Invariant: Registered effort is summed

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapItemRollupDtoTotalsTests.AMixOfEstimatedAndUnestimated_SumsTheOne_AndCountsTheOther
```

An item's total registered effort is the sum of the story points registered on what it gathers.

Enforced at: `RoadmapItemRollupDto.TotalEffort`

### Invariant: No estimate is invented

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapItemRollupDtoTotalsTests.Null_ContributesNothing_ButRaisesTheUnestimatedCount
```

Gathered work that registered no estimate adds nothing to the total, and is counted as unestimated instead.

Enforced at: `RoadmapItemRollupDto.UnestimatedCount`
