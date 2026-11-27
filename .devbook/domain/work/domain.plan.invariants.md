# Invariants

```meta
type: invariants
```

What the Plan on [`domain.plan.md`](domain.plan.md) enforces.

## Plan

```meta
type: invariants
related: [.devbook/domain/work/domain.plan.md#plan]
```

> The rules the Plan holds its items, milestones and dependencies to. They
> include the rules these parts enforce:
>
> - [Planned Window](domain.plan.md#planned-window);
> - [Milestone](domain.plan.md#milestone);
> - [Roadmap Tag](domain.plan.md#roadmap-tag);
> - [Repository Scope](domain.plan.md#repository-scope);
> - [Planning Lane](domain.plan.md#planning-lane);
> - [Dependency](domain.plan.md#dependency).

### Invariant: An empty plan is valid

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapPlanTests.EmptyPlan_IsAValidPlan
```

A plan with no items and no milestones is a valid plan.
It is what a first run holds, and what is left once everything has been delivered.

Enforced at: `RoadmapPlan.Empty()`

### Invariant: A cycle is refused

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanDependencyTests.ADirectCycleIsRefused_AndThePlanIsLeftAsItWas, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanDependencyTests.AnIndirectCycleIsRefused_HoweverManyStepsAway, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanDependencyTests.ACycleThroughAMilestoneIsRefusedToo]
```

A dependency that would close a cycle, directly or through any number of nodes, is refused and leaves the plan as it was (`roadmap.cyclic_dependency`).
A cycle already written into a hand-edited file is read as it stands, so the rule is the refusal, not a property of every loaded plan.

Enforced at: `AddDependency()`

### Invariant: A dependency names a node in the plan

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanDependencyTests.ADependencyOnSomethingNotInThePlan_IsRefused, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RehydrationTests.ADanglingDependencyIsDroppedRatherThanFailingTheLoad, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapPlanTests.RemovingAnItem_AlsoForgetsEveryDependencyOnIt, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapPlanTests.RemovingAMilestone_AlsoForgetsEveryDependencyOnIt]
```

A dependency names an item or milestone in this plan; an edge to an unknown id is refused (`roadmap.node_not_found`).
An edge to nothing is not a weaker plan but a wrong one.

Enforced at: `AddDependency()`; `Rehydrate()` drops a dangling edge, and `RemoveItem()` and `RemoveMilestone()` forget every edge to the node they remove

### Invariant: Nothing waits for itself

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanDependencyTests.NothingCanWaitForItself
```

A dependency of a node on itself is refused (`roadmap.self_dependency`).

Enforced at: `AddDependency()`

### Invariant: A dependency declared twice is one dependency

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanDependencyTests.DeclaringTheSameDependencyTwice_ChangesNothingAndSucceeds
```

Declaring a dependency the plan already holds changes nothing and succeeds, so a node never waits twice on the same node.

Enforced at: `AddDependency()`; `Dependencies.Of()` collapses duplicates when a plan is loaded

### Invariant: A window does not end before it starts

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlannedWindowTests.AWindowThatEndsBeforeItStarts_IsRefusedRatherThanThrown
```

A Planned Window ends on or after the day it starts; two dates that do not make one are refused (`roadmap.invalid_window`).

Enforced at: constructor (`PlannedWindow`): `Create()` refuses, and `Of()` clamps the end to the start

### Invariant: A window counts both its days

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlannedWindowTests.BothEndsAreInclusive_SoASingleDayIsOneDayLong
```

Both days of a Planned Window are inclusive, so a single-day window is one day long rather than none.

Enforced at: `PlannedWindow.Days`

### Invariant: A milestone is a day

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapPlanTests.AMilestoneIsADay_NotAOneDayItem
```

A Milestone falls on exactly one day and has no duration, which is why it is not a one-day item.

Enforced at: constructor (`Milestone`, which holds a single `On` date and no window)

### Invariant: Planning priority never touches the task

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapPlanTests.PlanningPriority_IsThePlansOwn_AndDoesNotTouchTheLinkedEntry
```

Setting an item's Planning Priority never writes to its linked Task.
The two are different judgements made for different reasons, and collapsing them would mean reprioritising a plan by touching an issue. This and the next rule are the split of *priority* inside Work: a task owns its execution priority, a plan its planning priority and sequence (local ADR 0073 §1).

Enforced at: `Prioritise()`

### Invariant: The task priority never overwrites the plan

```meta
type: invariant
```

A Task's own priority never changes an item's Planning Priority.

Enforced at: all mutations (`RoadmapItem.Prioritise()` is internal, and none of the plan's operations that call it reads a Task)

### Invariant: An unresolved scope alias is kept

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RehydrationTests.AnAliasThatNoLongerResolves_IsKeptRatherThanDeleted
```

A Repository Scope alias the Repository Registry no longer resolves is kept, and reads as unresolved.
Dropping it would silently delete something the person wrote because a supplier's registry changed.

Enforced at: constructor (`RepositoryScope`): `Of()` normalizes the aliases and never looks one up

### Invariant: Scope aliases are normalized and distinct

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RepositoryScopeTests.AliasesAreNormalizedTheWayTheSettingsStoreWritesThem, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RepositoryScopeTests.DuplicatesCollapse_AndTypingOrderDoesNotAffectEquality]
```

A Repository Scope holds each alias once, trimmed and in lower case, and two scopes naming the same aliases are equal whatever order they were typed in.
The aliases are written as the settings store writes them, so a scope and the registry compare without translation.

Enforced at: constructor (`RepositoryScope`): `Of()`

### Invariant: An empty scope is unfiled

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RepositoryScopeTests.AnEmptyScopeIsUnfiled_NotAnError
```

A Repository Scope with no alias, or only blank ones, is valid and means the work is under no repository.
It is neither an error nor a default repository.

Enforced at: constructor (`RepositoryScope`): `Of()` returns `Unfiled`

### Invariant: A blank lane is the default lane

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanningLaneTests.BlankIsTheDefaultLane, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanningLaneTests.ALaneIsWhateverThePersonCalledIt]
```

A blank Planning Lane is the default lane, and any other name is kept as the person wrote it, trimmed.

Enforced at: constructor (`PlanningLane`): `Of()`

### Invariant: A task link is only an id

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapPlanTests.PlanningPriority_IsThePlansOwn_AndDoesNotTouchTheLinkedEntry
```

A Task Link is a foreign id, and the plan never reads through it to decide anything about itself.

Enforced at: constructor (`RoadmapItem.TaskId` is a bare `Guid?`, set by `LinkTo()`; the module has no Tasks reader)

### Invariant: Every item has a tag

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanTaggingTests.AnItemAddedWithoutATag_GetsOneDerivedFromItsTitle
```

Every Roadmap Item has exactly one Roadmap Tag, derived from its title when none is given.

Enforced at: constructor (`RoadmapItem`)

### Invariant: Renaming keeps the tag

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanTaggingTests.RenamingTheTitle_LeavesTheTagAlone, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanTaggingTests.UpdatingWithNoTag_RederivesFromTheTitle_BecauseTheFieldWasCleared]
```

Renaming an item's title never changes its tag.
An update that clears the tag field is an edit of the tag rather than a rename, and derives a new one from the title.

Enforced at: `Rename()`

### Invariant: A tag is lowercase kebab-case

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanningTagTests.EveryProducedTagMatchesTheAgreedShape, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanningTagTests.AnExplicitValueIsNormalizedThroughTheSameRules, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanningTagTests.DiacriticsAreFoldedRatherThanDropped]
```

A Roadmap Tag is lowercase kebab-case, whether it was derived from a title or typed by the person.
A tag typed by hand goes through the same slug rule as a derived one, so the two cannot drift apart.

Enforced at: constructor (`PlanningTag`): `From()` and `Of()` slugify every value

### Invariant: A title that slugifies to nothing gets the tag item

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanningTagTests.ATitleThatSlugifiesToNothing_FallsBackToAStableValue
```

A title the slug rule reduces to nothing gets the tag `item`.
Work needs something to be filed against, and otherwise punctuation, an emoji or a script the slug rule strips would leave an empty tag.

Enforced at: constructor (`PlanningTag`): `From()` and `Of()`

### Invariant: Two items may share a tag

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanTaggingTests.TwoItemsMayShareATag_AndBothAreFound, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanTaggingTests.TheDistinctTagsInUseAreReported_OnceEach]
```

A Roadmap Tag already carried by another item is accepted, and asking for that tag returns every item that carries it.
A tag names a grouping rather than one item, so a person may file two items under one tag on purpose.

Enforced at: `AddItem()` and `UpdateItem()` never compare a tag with the other items'; `ItemsTagged()` returns every item carrying it

### Invariant: A knowledge ref is never read through

```meta
type: invariant
```

The plan never reads through a Knowledge Ref to decide anything about itself.

Enforced at: constructor (`KnowledgeReferences`, which holds strings nothing in `RoadmapPlan` resolves)

### Invariant: Links are never validated or repaired

```meta
type: invariant
```

A Task Link or a Knowledge Ref may name something that no longer exists, and neither is validated or repaired.

Enforced at: constructor (`RoadmapItem.TaskId` is a bare `Guid?`, and `KnowledgeReferences.Of()` only trims and removes duplicates)

### Invariant: All changes go through the plan

```meta
type: invariant
```

Items, milestones and dependencies change only through the plan.

Enforced at: all mutations (every mutator on `RoadmapItem`, `Milestone` and `Dependencies` is internal)

### Invariant: A reschedule leaves the dependencies alone

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanEditingTests.EditingDoesNotTouchDependencies, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanDependencyTests.ReschedulingDoesNotDragTheDependentWorkAlong]
```

Moving or editing an item changes neither its own dependencies nor the window of anything that waits on it.
Dependent work not dragged along is how a date that no longer fits becomes visible.

Enforced at: `Reschedule()` and `UpdateItem()`, which change the one item's window and never its `Dependencies`

### Invariant: A hand move hands the window to the person

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanImportProvenanceTests.Rescheduling_ClearsIt, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanImportProvenanceTests.ALaneOnlyDrop_KeepsIt_BecauseNoDateWasOverruled, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.ImportPlanItemsTests.EditingTheWindow_ClearsTheImportProvenance, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PinItemEndTests.PinEnd_ToTheDateTheImporterPlaced_StillHandsTheWindowToThePerson, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandKeepUpTests.The_editor_offers_no_update_from_tasks_and_a_save_that_leaves_the_dates_keeps_the_placement]
```

A change to an item's dates clears its `placed_by_import`, and a change that leaves both dates as they were keeps it.
A drop onto another lane, or a Save that leaves the dates alone, overrules no date the importer chose.

Enforced at: `RoadmapItem.MoveTo()`, which clears the value only when the window changes, and `RoadmapItem.PinEnd()`, which always clears it

### Invariant: The importer never moves a window a person placed

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanImportProvenanceTests.TheImporterMayNotMoveAWindowAPersonPlaced, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanImportProvenanceTests.TheImporterMayReplaceItsOwnWindow, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.ImportPlanItemsTests.AReimport_KeepsAWindowMovedByHand_AndStillRevisesTheRest]
```

An import may place again only a window that still carries `placed_by_import`, and is refused for any other (`roadmap.placed_by_hand`).
Only an import that creates the item anew sets the value, so once a person has placed the window it stays theirs.

Enforced at: `PlaceByImport()`; `AddImportedItem()` is the only other operation that sets the value

### Invariant: Gathered tasks outrank a completion by hand

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapItemCompletionTests.OnceTasksCarryTheItem_TheirProgressWins_OverTheHandSetDay, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapItemCompletionTests.WhenTasksCarryTheItemAndAreAllDone_ItEndsWhenTheLastTaskWasTickedOff, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapItemCompletionTests.OnceTasksCarryTheItem_TheLayoutFollowsTheTasks_NotTheHandSetDay]
```

While an item gathers at least one task, it is finished only when every gathered task is done, and its `completed_on` is kept but not read.
The day is kept rather than cleared, so it reads again if every task leaves the item.

Enforced at: `RoadmapItemCompletion.IsFinished()`, `IsCompletedByHand()` and `FinishedOn()`, the one reading the layout, keep-up, the roadmap's bars and the dashboard share

### Invariant: A devbook chapter does not carry an item

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapItemCompletionTests.AKnowledgeChapter_IsNoTask_SoTheHandSetDayStillCounts, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapItemCompletionTests.AnItemWhoseWorkWasNotRead_ReadsAsHavingGatheredNothing]
```

An item that gathered only devbook chapters is an item no task carries, so its completion by hand counts.
A chapter registers no progress, so it cannot say the work is over.

Enforced at: `RoadmapItemCompletion.HasTasks()`, which counts gathered backlog entries only

### Invariant: A completion day is not after today

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.CompleteItemTests.TheCompleteCommand_RefusesADayAfterToday_AndSavesNothing, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.CompleteItemTests.TheCompleteCommand_TakesToday]
```

Completing an item on a day after today is refused and leaves the item as it was (`roadmap.completed_in_future`).
Finished work cannot end in the future.

Enforced at: `CompleteItemCommandHandler`, against today on its clock

### Invariant: Completing again replaces the completion day

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.CompleteItemTests.CompleteItem_AgainOnAnotherDay_MovesTheDay
```

Completing an item that is already completed keeps only the newest day.

Enforced at: `RoadmapPlan.CompleteItem()`

### Invariant: Reopening clears the completion day

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.CompleteItemTests.ReopenItem_ClearsTheDay, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.CompleteItemTests.ReopenItem_OnAnOpenItem_LeavesItOpen]
```

Reopening an item clears its `completed_on`, and reopening an open item changes nothing.

Enforced at: `RoadmapPlan.ReopenItem()`

### Invariant: Completing leaves the window where it was planned

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.CompleteItemTests.CompleteItem_RecordsTheDay_AndLeavesTheWindowWhereItWasPlanned
```

Completing or reopening an item changes its `completed_on` and nothing else, its Planned Window included.

Enforced at: `RoadmapPlan.CompleteItem()` and `ReopenItem()`

### Invariant: Moving or editing a completed item keeps it completed

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.CompleteItemTests.MovingOrEditingACompletedItem_KeepsItCompleted
```

A reschedule, a pinned or unpinned end, a new priority or an edit of a completed item leaves its `completed_on` as it was.
Only Reopen opens it again.

Enforced at: `Reschedule()`, `PinEnd()`, `UnpinEnd()`, `Prioritise()` and `UpdateItem()`, none of which writes `completed_on`
