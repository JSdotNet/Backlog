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

### Invariant: Two items may share a tag

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanTaggingTests.TwoItemsMayShareATag_AndBothAreFound, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanTaggingTests.TheDistinctTagsInUseAreReported_OnceEach]
```

A Roadmap Tag already carried by another item is accepted, and asking for that tag returns every item that carries it.
A tag names a grouping rather than one item, so a person may file two items under one tag on purpose.

Enforced at: `AddItem()` and `UpdateItem()` never compare a tag with the other items'; `ItemsTagged()` returns every item carrying it

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

### Invariant: A reschedule leaves the dependencies alone

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanEditingTests.EditingDoesNotTouchDependencies, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanDependencyTests.ReschedulingDoesNotDragTheDependentWorkAlong]
```

Moving or editing an item changes neither its own dependencies nor the window of anything that waits on it.
Dependent work that is not dragged along is how a date that no longer fits becomes visible.

Enforced at: `Reschedule()` and `UpdateItem()`, which change the one item's window and never its `Dependencies`

### Invariant: A hand move hands the window to the person

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanImportProvenanceTests.Rescheduling_ClearsIt, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanImportProvenanceTests.ALaneOnlyDrop_KeepsIt_BecauseNoDateWasOverruled, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.ImportPlanItemsTests.EditingTheWindow_ClearsTheImportProvenance, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PinItemEndTests.PinEnd_ToTheDateTheImporterPlaced_StillHandsTheWindowToThePerson, integration:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandKeepUpTests.The_editor_offers_no_update_from_tasks_and_a_save_that_leaves_the_dates_keeps_the_placement]
```

A change to an item's dates clears its `placed_by_import`, and a change that leaves both dates as they were keeps it.
A drop onto another lane, or a Save that leaves the dates alone, overrules no date the importer chose.

Enforced at: `RoadmapItem.MoveTo()`, which clears the value only when the window changes, and `RoadmapItem.PinEnd()`, which always clears it

### Invariant: The importer never moves a window a person placed

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanImportProvenanceTests.TheImporterMayNotMoveAWindowAPersonPlaced, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanImportProvenanceTests.TheImporterMayReplaceItsOwnWindow, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.ImportPlanItemsTests.AReimport_KeepsAWindowMovedByHand_AndStillRevisesTheRest]
```

An import may place again only a window that still carries `placed_by_import`, and is refused for any other (`roadmap.placed_by_hand`).
Only an import that creates the item anew sets the value, so once a person has placed the window it stays theirs.

Enforced at: `PlaceByImport()`; `AddImportedItem()` is the only other operation that sets the value

## Plan Sequencing

```meta
status: draft
type: invariants
related: [.devbook/domain/roadmap/domain.md#plan-sequencing]
```

> The rules Plan Sequencing holds its answers about the graph to.

### Invariant: A contradiction is reported, never corrected

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanDependencyTests.OverlappingDatesAcrossADependency_AreReportedNotRefused, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanDependencyTests.ReschedulingDoesNotDragTheDependentWorkAlong, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.PlanEditingTests.EditingDatesIntoAContradiction_IsAllowedAndReported]
```

A change that makes work open on or before the close of something it waits on is accepted, and the contradiction is reported rather than refused or repaired.
A plan is allowed to be temporarily wrong, because that is how a person finds out a date does not fit.

Enforced at: `PlanSequencing.Contradictions()`, a pure query over the nodes; `AddDependency()`, `Reschedule()` and `UpdateItem()` accept the change that makes one

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

### Invariant: Something reached both ways is counted once

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapRollupMergeTests.TheSameThingReachedBothWays_isCountedOnce_andWearsBoth, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapRollupMergeTests.Keys_compareCaseInsensitively_soOneThingIsNotTwo, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.AnEntryBothLinkedAndTagged_IsCountedOnce_WearingBoth, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.AChapterBothReferencedAndRoadmapTagged_IsCountedOnce_WearingBoth]
```

A task or chapter an item reaches both by name and by tag appears once in what it gathers, marked as reached both ways.
A person deciding whether a link is safe to remove needs to see that the tag still holds the work.

Enforced at: `RoadmapRollup.Merge()`, keyed without regard to case; `RoadmapItemRollupBuilder` reads each entry once and marks it reached both ways

### Invariant: A plan tag matches with or without its sigil

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.AnEntryWearingThePlanSigil_AndOneWearingTheBareSlug_BothRollUp, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.APersonTagSpellingTheSlug_DoesNotRollUp]
```

A task tagged `+slug` and a task tagged with the bare `slug` are both gathered by the item whose tag is `slug`, and a task tagged `@slug` is not.
A task tagged before the [Plan Sigil](domain.md#plan-sigil) existed still counts toward its item.

Enforced at: `RoadmapItemRollupBuilder.CarriesPlanTag()`, which lifts only a leading `+` before comparing

### Invariant: Unknown progress is never finished

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapItemRollupDtoProgressTests.SomethingThatRegisteredNoProgress_IsNotDone, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.AKnowledgeChapter_RegistersNoProgress]
```

A gathered thing that registered no progress, as a knowledge chapter does, carries none and never reads as done.
Progress has four states, and unknown is not a fifth.

Enforced at: `RoadmapGatheredLink.IsDone`, true only for `RoadmapProgress.Done`

### Invariant: A wait on work the item did not gather is dropped

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.ADependencyOnSomethingTheItemDidNotGather_IsNotCarried, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapRollupOrderTests.ADependencyOnSomethingAbsent_OrdersNothing, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapRollupWaveTests.AWaitOnSomethingNotGathered_CountsAsNothing]
```

A gathered thing carries as its dependencies only the other things this same item gathered.
A wait on work outside the item still blocks that work, but it is not an edge between two things the item can draw.

Enforced at: `RoadmapItemRollupBuilder.GatheredDependencies()`; `RoadmapRollup.InDependencyOrder()` ignores a key that is not in the list

### Invariant: Finished effort counts registered estimates only

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapItemRollupDtoProgressTests.DoneEffort_SumsOnlyWhatIsFinished_AndTotalEffortStillSumsTheLot, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapItemRollupDtoProgressTests.FinishedWorkThatRegisteredNoEstimate_RaisesTheDoneCount_ButNotTheDoneEffort]
```

The finished effort is the sum of the estimates registered on finished work, and the finished count counts every finished thing, estimated or not.
A reading drawn from effort alone would overstate how far along an item is as soon as finished work turned out to be unsized.

Enforced at: `RoadmapItemRollupDto.DoneEffort` and `RoadmapItemRollupDto.DoneCount`

### Invariant: An entry's own dates win over its sessions'

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.AnEntrysOwnDates_WinOverItsSessions
```

A gathered entry's start and end come from the dates Tasks recorded on it, and a linked AI session supplies only a date the entry lacks.

Enforced at: `RoadmapItemRollupBuilder.Dates()`

### Invariant: A session never ends open work

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.AnUndatedEntry_WithLinkedSessions_StartsWithTheFirstSession, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.NeedsSessionDates_OnlyForAnEntryWithSessionsAndAMissingDate]
```

A linked session dates the end of an entry only when the entry's work is over.
A session ending says nothing about open work being finished.

Enforced at: `RoadmapItemRollupBuilder.Dates()` and `NeedsSessionDates()`

### Invariant: Nothing is placed before what it waits on

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapRollupOrderTests.AStepComesAfterTheOneItWaitsOn_WhateverOrderItArrivedIn, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapRollupOrderTests.AChain_ComesOutInChainOrder, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapRollupOrderTests.EveryStepComesBackExactlyOnce]
```

In the order of an item's steps, every step comes after each gathered step it waits on.

Enforced at: `RoadmapRollup.InDependencyOrder()`

### Invariant: Steps equally free keep the order they were gathered in

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapRollupOrderTests.TwoStepsWaitingOnNothing_KeepTheOrderTheyArrivedIn, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapRollupOrderTests.AStepFreedLaterStillLosesATieToOneThatArrivedEarlier]
```

Of two steps equally free to go next, the one gathered first comes first.
The order invents no ranking of its own.

Enforced at: `RoadmapRollup.InDependencyOrder()`

### Invariant: A cycle of waits is released at the earliest waiter

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapRollupOrderTests.ACycle_FallsBackToAppearanceOrder_RatherThanThrowing, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapRollupOrderTests.WorkDownstreamOfACycle_StillLandsAfterIt, unit:dotnet:Backlog.Modules.Roadmap.UnitTests.RoadmapRollupWaveTests.ACycle_StillPlacesEveryStepExactlyOnce]
```

When every remaining step waits on another, the earliest gathered of them is released and the ordering goes on, rather than the item being refused.
Tasks lets a person write a cycle, and a reading that failed on one would break on a typed `after:`.

Enforced at: `RoadmapRollup.InDependencyOrder()`

### Invariant: A whole plan is gathered the way each item is

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.BuildPlan_AgreesWithBuild_ForTheSameItem
```

Gathering every item of a plan in one pass answers each item exactly as gathering that item alone does.

Enforced at: `RoadmapItemRollupBuilder.BuildPlan()`, which builds each item through the same `Build()` over one read of each supplier

### Invariant: Every item of a plan is answered

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.BuildPlan_AnswersEveryItem_IncludingTheOnesThatGatheredNothing
```

A plan-wide gathering holds an answer for every item of the plan, and an item that gathered nothing answers empty.
"Gathered nothing" is then never confused with "was not asked about".

Enforced at: `RoadmapItemRollupBuilder.BuildPlan()`

### Invariant: Gathering never writes

```meta
status: draft
type: invariant
```

Gathering changes nothing in the plan, in a task, or in a chapter, and stores none of what it reads, the actual dates included.
An unreachable supplier therefore shrinks a total rather than corrupting a plan.

Enforced at: `IRoadmapItemRollup`, whose two operations only answer; `RoadmapItemRollupService` calls nothing on the backlog but `ListAsync()`, and `RoadmapItemRollupBuilder` is a pure function
