# Invariants

```meta
type: invariants
```

What each domain service on [`domain.md`](domain.md) enforces. The rules of the two aggregates are on [the Task's](domain.task.invariants.md#task) and [the Plan's](domain.plan.invariants.md#plan) subpages.

## Plan Sequencing

```meta
type: invariants
related: [.devbook/domain/work/domain.md#plan-sequencing]
```

> The rules Plan Sequencing holds its answers about the graph to.

### Invariant: A contradiction is reported, never corrected

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanDependencyTests.OverlappingDatesAcrossADependency_AreReportedNotRefused, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanDependencyTests.ReschedulingDoesNotDragTheDependentWorkAlong, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PlanEditingTests.EditingDatesIntoAContradiction_IsAllowedAndReported]
```

A change that makes work open on or before the close of what it waits on is accepted, and the contradiction is reported, never refused or repaired.
A plan may be temporarily wrong, because that is how a person finds out a date does not fit.

Enforced at: `PlanSequencing.Contradictions()`, a pure query over the nodes; `AddDependency()`, `Reschedule()` and `UpdateItem()` accept the change that makes one

## Roadmap Item Gathering

```meta
type: invariants
related: [.devbook/domain/work/domain.md#roadmap-item-gathering]
```

> The rules Roadmap Item Gathering holds its totals to, whoever asks for them.

### Invariant: Registered effort is summed

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapItemRollupDtoTotalsTests.AMixOfEstimatedAndUnestimated_SumsTheOne_AndCountsTheOther
```

An item's total registered effort is the sum of the story points registered on what it gathers.

Enforced at: `RoadmapItemRollupDto.TotalEffort`

### Invariant: No estimate is invented

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapItemRollupDtoTotalsTests.Null_ContributesNothing_ButRaisesTheUnestimatedCount
```

Gathered work that registered no estimate adds nothing to the total, and is counted as unestimated instead.

Enforced at: `RoadmapItemRollupDto.UnestimatedCount`

### Invariant: Something reached both ways is counted once

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapRollupMergeTests.TheSameThingReachedBothWays_isCountedOnce_andWearsBoth, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapRollupMergeTests.Keys_compareCaseInsensitively_soOneThingIsNotTwo, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.AnEntryBothLinkedAndTagged_IsCountedOnce_WearingBoth, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.AChapterBothReferencedAndRoadmapTagged_IsCountedOnce_WearingBoth, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderBrainTests.GatheringByTag_ReachesEveryNoteNamingTheTag_CaseInsensitively_OnceEach]
```

A task, chapter or Brain note an item reaches both by name and by tag appears once in what it gathers, marked as reached both ways.
A person deciding whether a link is safe to remove needs to see that the tag still holds the work.

Enforced at: `RoadmapRollup.Merge()`, keyed without regard to case; `RoadmapItemRollupBuilder` reads each entry once and marks it reached both ways

### Invariant: A plan tag matches with or without its sigil

```meta
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.AnEntryWearingThePlanSigil_AndOneWearingTheBareSlug_BothRollUp, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.APersonTagSpellingTheSlug_DoesNotRollUp]
```

A task tagged `+slug` and a task tagged with the bare `slug` are both gathered by the item whose tag is `slug`, and a task tagged `@slug` is not.
A task tagged before the [Plan Sigil](domain.md#plan-sigil) existed still counts toward its item.

Enforced at: `RoadmapItemRollupBuilder.CarriesPlanTag()`, which lifts only a leading `+` before comparing

### Invariant: Unknown progress is never finished

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapItemRollupDtoProgressTests.SomethingThatRegisteredNoProgress_IsNotDone, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.AKnowledgeChapter_RegistersNoProgress]
```

A gathered thing that registered no progress, as a devbook chapter does, carries none and never reads as done.
Progress has four states, and unknown is not a fifth.

Enforced at: `RoadmapGatheredLink.IsDone`, true only for `RoadmapProgress.Done`

### Invariant: A wait on work the item did not gather is dropped

```meta
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.ADependencyOnSomethingTheItemDidNotGather_IsNotCarried, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapRollupOrderTests.ADependencyOnSomethingAbsent_OrdersNothing, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapRollupWaveTests.AWaitOnSomethingNotGathered_CountsAsNothing]
```

A gathered thing carries as its dependencies only the other things this same item gathered.
A wait on work outside the item still blocks that work, but it is not an edge between two things the item can draw.

Enforced at: `RoadmapItemRollupBuilder.GatheredDependencies()`; `RoadmapRollup.InDependencyOrder()` ignores a key that is not in the list

### Invariant: Finished effort counts registered estimates only

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapItemRollupDtoProgressTests.DoneEffort_SumsOnlyWhatIsFinished_AndTotalEffortStillSumsTheLot, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapItemRollupDtoProgressTests.FinishedWorkThatRegisteredNoEstimate_RaisesTheDoneCount_ButNotTheDoneEffort]
```

The finished effort is the sum of the estimates registered on finished work, and the finished count counts every finished thing, estimated or not.
A reading from effort alone would overstate how far along an item is as soon as finished work turned out to be unsized.

Enforced at: `RoadmapItemRollupDto.DoneEffort` and `RoadmapItemRollupDto.DoneCount`

### Invariant: An entry's own dates win over its sessions'

```meta
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.AnEntrysOwnDates_WinOverItsSessions
```

A gathered entry's start and end come from the dates the task recorded on it, and a linked AI session supplies only a date the entry lacks.

Enforced at: `RoadmapItemRollupBuilder.Dates()`

### Invariant: A session never ends open work

```meta
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.AnUndatedEntry_WithLinkedSessions_StartsWithTheFirstSession, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.NeedsSessionDates_OnlyForAnEntryWithSessionsAndAMissingDate]
```

A linked session dates the end of an entry only when the entry's work is over.
A session ending says nothing about open work being finished.

Enforced at: `RoadmapItemRollupBuilder.Dates()` and `NeedsSessionDates()`

### Invariant: Nothing is placed before what it waits on

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapRollupOrderTests.AStepComesAfterTheOneItWaitsOn_WhateverOrderItArrivedIn, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapRollupOrderTests.AChain_ComesOutInChainOrder, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapRollupOrderTests.EveryStepComesBackExactlyOnce]
```

In the order of an item's steps, every step comes after each gathered step it waits on.

Enforced at: `RoadmapRollup.InDependencyOrder()`

### Invariant: Steps equally free keep the order they were gathered in

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapRollupOrderTests.TwoStepsWaitingOnNothing_KeepTheOrderTheyArrivedIn, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapRollupOrderTests.AStepFreedLaterStillLosesATieToOneThatArrivedEarlier]
```

Of two steps equally free to go next, the one gathered first comes first.
The order invents no ranking of its own.

Enforced at: `RoadmapRollup.InDependencyOrder()`

### Invariant: A cycle of waits is released at the earliest waiter

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapRollupOrderTests.ACycle_FallsBackToAppearanceOrder_RatherThanThrowing, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapRollupOrderTests.WorkDownstreamOfACycle_StillLandsAfterIt, unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.RoadmapRollupWaveTests.ACycle_StillPlacesEveryStepExactlyOnce]
```

When every remaining step waits on another, the earliest gathered of them is released and the ordering goes on, rather than the item being refused.
A person may write a cycle of tasks, so a typed `after:` must not break the reading.

Enforced at: `RoadmapRollup.InDependencyOrder()`

### Invariant: A whole plan is gathered the way each item is

```meta
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.BuildPlan_AgreesWithBuild_ForTheSameItem
```

Gathering every item of a plan in one pass answers each item exactly as gathering that item alone does.

Enforced at: `RoadmapItemRollupBuilder.BuildPlan()`, which builds each item through the same `Build()` over one read of each supplier

### Invariant: Every item of a plan is answered

```meta
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapItemRollupBuilderTests.BuildPlan_AnswersEveryItem_IncludingTheOnesThatGatheredNothing
```

A plan-wide gathering holds an answer for every item of the plan, and an item that gathered nothing answers empty.
"Gathered nothing" is then never confused with "was not asked about".

Enforced at: `RoadmapItemRollupBuilder.BuildPlan()`

### Invariant: Gathering never writes

```meta
type: invariant
```

Gathering changes nothing in the plan, in a task, in a chapter or in a Brain note, and stores none of what it reads, the actual dates included.
An unreachable supplier therefore shrinks a total rather than corrupting a plan.

Enforced at: `IRoadmapItemRollup`, whose two operations only answer; `RoadmapItemRollupService` calls nothing on the backlog but `ListAsync()` and nothing on the Brain but `IRoadmapBrainNotes.ReadAsync()`, and `RoadmapItemRollupBuilder` is a pure function

## My Day

```meta
type: invariants
related: [.devbook/domain/work/domain.md#my-day]
```

> The rules My Day holds the tasks and the plans a person picked for today to.

### Invariant: A task and a plan are in My Day by one rule

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Work.Tasks.UnitTests.MyDayTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanMyDayTests.A_plan_stamped_today_is_in_My_Day_and_one_on_no_day_or_a_later_day_is_not, unit:dotnet:Backlog.Desktop.UI.UnitTests.PlanMyDayTests.With_Clear_My_Day_overnight_off_an_unfinished_plan_carries_over_and_a_finished_one_does_not]
```

Whether a task or a plan is in today's My Day is answered by the same rule over its date, today, the day it was finished and the Clear My Day overnight setting.
Two rules would let a plan and its own entries disagree about today.

Enforced at: `MyDay.Includes()` in `Backlog.Modules.Work.Abstractions`, which the plan's reading calls with the plan's date and the day its last entry was ticked off

### Invariant: Putting a plan on My Day never moves its window

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PutPlanOnMyDayTests.Putting_a_plan_on_My_Day_stamps_its_item_and_leaves_the_window_where_it_was_planned
```

Setting or clearing a plan's My Day date leaves its Planned Window where it was.
My Day is this morning's choice, not a plan for when the work happens.

Enforced at: `RoadmapPlan.PutPlanOnMyDay()`

### Invariant: Every item carrying the tag takes the date

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Work.Plans.UnitTests.PutPlanOnMyDayTests.A_shared_tag_is_one_plan_so_every_item_carrying_it_takes_the_date
```

Putting a plan on My Day dates every Roadmap Item that carries its tag, because a shared tag is one plan.

Enforced at: `RoadmapPlan.PutPlanOnMyDay()`
