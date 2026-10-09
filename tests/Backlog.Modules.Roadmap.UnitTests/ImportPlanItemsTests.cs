using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Features.ImportPlanItems;
using Backlog.Modules.Roadmap.Features.RescheduleItem;
using Backlog.Modules.Roadmap.Features.UpdateItem;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// Laying an imported document's plan-level entries out on the roadmap (ADR 0013,
/// rulings 2, 4 and 5), through the handler and a store that — like the real one —
/// hands out a fresh plan on every load, so an import that was refused can be seen to
/// have left nothing behind.
/// </summary>
public class ImportPlanItemsTests
{
    private static readonly DateOnly Today = new(2026, 3, 2);

    private readonly SnapshotPlanRepository _plans = new();
    private readonly FixedVelocity _velocity = new(7);
    private readonly TaggedWork _work = new();
    private readonly FakeTimeProvider _clock = new();

    public ImportPlanItemsTests()
    {
        _clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        SetToday(Today);
    }

    private void SetToday(DateOnly day) =>
        _clock.SetUtcNow(new DateTimeOffset(day, new TimeOnly(9, 0), TimeSpan.Zero));

    /// <summary>
    /// The import as Tasks hands it over: the tags its tasks were written under, after
    /// those tasks are down. Each tag given here files, under it, one task of its total
    /// effort and one unestimated task per unestimated count — filed in no repository, so
    /// they land in the item's first part — replacing what the tag gathered before, the
    /// way a re-imported task document replaces its tasks.
    /// </summary>
    private Task<Result<PlanImportResultDto>> ImportAsync(
        IReadOnlyList<PlanImportEntryDto> entries,
        params PlanTagEffortDto[] effort)
    {
        File(effort);
        return new ImportPlanItemsCommandHandler(_plans, _velocity, _work, _clock, new RoadmapPlanGate())
            .Handle(new ImportPlanItemsCommand(entries, effort), TestContext.Current.CancellationToken);
    }

    private void File(IEnumerable<PlanTagEffortDto> effort)
    {
        foreach (var tag in effort)
        {
            List<RoadmapGatheredLink> tasks = [];
            if (tag.TotalEffort > 0) tasks.Add(Work($"{tag.Tag}-sized", tag.TotalEffort));
            for (var index = 0; index < tag.UnestimatedCount; index++) tasks.Add(Work($"{tag.Tag}-unsized-{index}", null));

            _work.File(tag.Tag, [.. tasks]);
        }
    }

    private static RoadmapGatheredLink Work(
        string key,
        int? effort,
        string[]? repositories = null,
        string[]? after = null,
        RoadmapProgress progress = RoadmapProgress.Ready,
        DateOnly? started = null) =>
        new(key, key, effort, RollupOrigin.Tag, progress, after ?? [], repositories ?? [], started);

    private async Task<PlanImportResultDto> ImportedAsync(
        IReadOnlyList<PlanImportEntryDto> entries,
        params PlanTagEffortDto[] effort)
    {
        var result = await ImportAsync(entries, effort);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    private static PlanImportEntryDto Entry(
        string tag,
        string? title = null,
        DateOnly? due = null,
        string? id = null,
        params string[] after) =>
        new(title ?? tag, tag, id, ["backlog"], PlanningPriority.High, due, after, "The body.");

    private RoadmapItem Stored(string tag) => Assert.Single(_plans.Current.ItemsTagged(PlanningTag.Of(tag)));

    // --- Creating ---------------------------------------------------------

    [Fact]
    public async Task EachTaggedEntryBecomesOneItem_CarryingWhatTheEntrySaid()
    {
        var result = await ImportedAsync([Entry("roadmap-imported-plans", "Imported plans")]);

        var item = Stored("roadmap-imported-plans");
        Assert.Equal("Imported plans", item.Title);
        Assert.Equal(["backlog"], item.Scope.Aliases);
        Assert.Equal(PlanningPriority.High, item.Priority);
        Assert.Equal("The body.", item.Notes);
        Assert.Equal(ImportPlacement.Effort, item.PlacedByImport);
        Assert.Equal(item.Id, Assert.Single(result.Created).Id);
        Assert.Empty(result.Updated);
    }

    [Fact]
    public async Task NothingGathered_TheItemStartsTodayForTheDefaultSpan()
    {
        await ImportedAsync([Entry("plan-a")]);

        var item = Stored("plan-a");
        Assert.Equal(Today, item.Window.Start);
        Assert.Equal(5, item.Window.Days);
    }

    [Fact]
    public async Task GatheredEffortOverVelocity_SetsTheLength()
    {
        _velocity.StoryPointsPerWeek = 14;

        await ImportedAsync([Entry("plan-a")], new PlanTagEffortDto("plan-a", 7, 1));

        Assert.Equal(3, Stored("plan-a").Window.Days); // 7 points at 14 a week = 21.25 working hours
    }

    [Fact]
    public async Task EachEntryIsPlacedAtThePaceOfItsOwnRepository_FromOneRead()
    {
        _velocity.ByRepository["backlog"] = 14;
        _velocity.ByRepository["site"] = 7;

        await ImportedAsync(
            [
                Entry("plan-a"), // filed under backlog
                new PlanImportEntryDto("Plan B", "plan-b", null, ["site"], PlanningPriority.High, null, [], null)
            ],
            new PlanTagEffortDto("plan-a", 14, 0),
            new PlanTagEffortDto("plan-b", 14, 0));

        Assert.Equal(5, Stored("plan-a").Window.Days);  // 14 points at 14 a week: Monday to Friday
        Assert.Equal(12, Stored("plan-b").Window.Days); // 14 points at 7 a week: to the next Friday
        Assert.Equal(1, _velocity.Reads);
        Assert.Equal(1, _work.Reads); // the whole plan's work gathered at once
    }

    /// <summary>AC7: each repository's part at its own pace, the window their envelope — not
    /// the whole plan's effort at the slowest pace among them, which would be 35 points at 7
    /// a week, five working weeks.</summary>
    [Fact]
    public async Task AnEntryUnderSeveralRepositories_IsPlacedPartByPart_AndAnUnfiledOneAtTheGlobalPace()
    {
        _velocity.StoryPointsPerWeek = 28;
        _velocity.ByRepository["backlog"] = 14;
        _velocity.ByRepository["site"] = 7;
        _work.File("plan-both", Work("in-backlog", 28, ["backlog"]), Work("in-site", 7, ["site"]));

        await ImportedAsync(
            [
                new PlanImportEntryDto("Both", "plan-both", null, ["backlog", "site"], PlanningPriority.High, null, [], null),
                new PlanImportEntryDto("Unfiled", "plan-unfiled", null, [], PlanningPriority.High, null, [], null)
            ],
            new PlanTagEffortDto("plan-unfiled", 14, 0));

        // backlog's two working weeks beside site's one: the window is backlog's.
        Assert.Equal(PlannedWindow.Of(Today, new DateOnly(2026, 3, 13)), Stored("plan-both").Window);
        Assert.Equal(3, Stored("plan-unfiled").Window.Days); // at the global 28 a week: 21.25 hours
    }

    /// <summary>AC3: an item that hands over from one repository to another ends on its
    /// latest part end, and a plan imported in the same run that waits on it starts the
    /// day after. Today is Monday 12 October 2026; <c>app</c> gets through 8 points a week
    /// and <c>site</c> 4.</summary>
    [Fact]
    public async Task APlanWaitingOnAPlan_StartsTheDayAfterItsLatestPartEnds()
    {
        SetToday(new DateOnly(2026, 10, 12));
        _velocity.ByRepository["app"] = 8;
        _velocity.ByRepository["site"] = 4;
        _work.File("plan-a", Work("build", 8, ["app"]), Work("publish", 4, ["site"], after: ["build"]));
        _work.File("plan-b", Work("follow-up", 8, ["app"]));

        await ImportedAsync(
        [
            new PlanImportEntryDto("Plan B", "plan-b", null, ["app"], PlanningPriority.High, null, ["plan-a"], null),
            new PlanImportEntryDto("Plan A", "plan-a", null, ["app", "site"], PlanningPriority.High, null, [], null)
        ]);

        // app 12–16 Oct, then site 19–23 Oct: under the slowest pace, 12 points at 4 a
        // week, it would have run to 30 Oct.
        Assert.Equal(PlannedWindow.Of(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 23)), Stored("plan-a").Window);
        Assert.Equal(PlannedWindow.Of(new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 30)), Stored("plan-b").Window);
        Assert.Equal(ImportPlacement.Effort, Stored("plan-a").PlacedByImport);
    }

    /// <summary>A predecessor that ended before today holds nothing back: the floor is the
    /// later of today and the day after it ends, at import as in the projection.</summary>
    [Fact]
    public async Task APredecessorThatEndedBeforeToday_LeavesTheItemStartingToday()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem(
            "Done long ago", PlannedWindow.Of(new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 6)),
            tag: PlanningTag.Of("done-long-ago"));
        _plans.Current = plan;

        await ImportedAsync([Entry("plan-a", after: "done-long-ago")], new PlanTagEffortDto("plan-a", 7, 0));

        Assert.Equal(PlannedWindow.Of(Today, new DateOnly(2026, 3, 6)), Stored("plan-a").Window);
    }

    [Fact]
    public async Task ADueDateEndsTheWindow()
    {
        var due = new DateOnly(2026, 3, 20);

        await ImportedAsync([Entry("plan-a", due: due)], new PlanTagEffortDto("plan-a", 40, 0));

        var item = Stored("plan-a");
        Assert.Equal(Today, item.Window.Start);
        Assert.Equal(due, item.Window.End);
        Assert.Equal(ImportPlacement.DueDate, item.PlacedByImport);
    }

    [Fact]
    public async Task EveryNewWindowIsScheduled_WithNoPreviousWindow()
    {
        var result = await ImportedAsync([Entry("plan-a")]);

        var scheduled = Assert.Single(result.Scheduled);
        var item = Stored("plan-a");
        Assert.Equal(item.Id, scheduled.RoadmapItemId);
        Assert.Equal(item.Window.Start, scheduled.Start);
        Assert.Equal(item.Window.End, scheduled.End);
        Assert.Null(scheduled.PreviousStart);
        Assert.Null(scheduled.PreviousEnd);
    }

    // --- Skipping ---------------------------------------------------------

    [Fact]
    public async Task AnEntryWithNoTag_IsReportedAndSkipped()
    {
        var result = await ImportedAsync(
        [
            new PlanImportEntryDto("Nameless plan", Tag: null),
            new PlanImportEntryDto("Blank plan", Tag: "  "),
            Entry("plan-a")
        ]);

        Assert.Equal(["Nameless plan", "Blank plan"], result.SkippedWithoutTag);
        Assert.Single(_plans.Current.Items);
    }

    /// <summary>ADR 0019 Verification 2, for the importer: imported on a Saturday, an
    /// item waiting on nothing opens on the Monday.</summary>
    [Fact]
    public async Task ImportedOnASaturday_AnItemOpensOnTheMonday()
    {
        _clock.SetUtcNow(new DateTimeOffset(new DateOnly(2026, 3, 7), new TimeOnly(9, 0), TimeSpan.Zero));

        await ImportedAsync([Entry("plan-a")], new PlanTagEffortDto("plan-a", 7, 0));

        Assert.Equal(new DateOnly(2026, 3, 9), Stored("plan-a").Window.Start);
        Assert.Equal(new DateOnly(2026, 3, 13), Stored("plan-a").Window.End);
    }

    /// <summary>The importer counts the week the paces were read with.</summary>
    [Fact]
    public async Task TheImporterCountsThePersonsWorkingWeek()
    {
        _velocity.ByRepository["backlog"] = 38;
        _velocity.Week = new WorkingHours
        {
            Days = [new WorkingDay(DayOfWeek.Friday, true, new TimeOnly(9, 0), new TimeOnly(13, 0))]
        };

        await ImportedAsync([Entry("plan-a")], new PlanTagEffortDto("plan-a", 34, 0));

        Assert.Equal(new DateOnly(2026, 3, 5), Stored("plan-a").Window.End); // Monday to Thursday's 34 hours
    }

    // --- A start a person chose ------------------------------------------

    /// <summary>A plan dropped on a day — the Calendar's shelf — opens there, its end the
    /// one the import's own placement counts for its effort at its pace.</summary>
    [Fact]
    public async Task AChosenStart_OpensTheWindowThere_ForItsEffortAtThePace()
    {
        _velocity.StoryPointsPerWeek = 14;
        var monday = Today.AddDays(7);

        await ImportedAsync([Entry("plan-a") with { Start = monday }], new PlanTagEffortDto("plan-a", 7, 1));

        var item = Stored("plan-a");
        Assert.Equal(monday, item.Window.Start);
        Assert.Equal(3, item.Window.Days); // 7 points at 14 a week, as from today
    }

    /// <summary>The window is the person's from then on, so the keep-up projection never
    /// slides it back to today and a later import does not overrule it.</summary>
    [Fact]
    public async Task AChosenStart_LeavesTheWindowAsThePersonsPlacement()
    {
        await ImportedAsync([Entry("plan-a") with { Start = Today.AddDays(14) }], new PlanTagEffortDto("plan-a", 7, 0));

        Assert.Null(Stored("plan-a").PlacedByImport);

        await ImportedAsync([Entry("plan-a")], new PlanTagEffortDto("plan-a", 21, 0));
        Assert.Equal(Today.AddDays(14), Stored("plan-a").Window.Start);
    }

    [Fact]
    public async Task AChosenStartOnASaturday_OpensOnTheMonday()
    {
        await ImportedAsync([Entry("plan-a") with { Start = new DateOnly(2026, 3, 7) }], new PlanTagEffortDto("plan-a", 7, 0));

        Assert.Equal(new DateOnly(2026, 3, 9), Stored("plan-a").Window.Start);
    }

    /// <summary>A plan whose work has already begun, dropped on a later day, still opens on
    /// that day: the start a person chose wins over the day its work began, and its open
    /// points are forecast from there (entry 13 of plan task-views, QA scenario C6).</summary>
    [Fact]
    public async Task AChosenStart_WinsOverWorkThatHasAlreadyBegun()
    {
        var monday = Today.AddDays(14); // Monday 16 March
        _work.File(
            "plan-a",
            Work("first", 7, progress: RoadmapProgress.InProgress, started: Today),
            Work("second", 7));

        var result = await new ImportPlanItemsCommandHandler(_plans, _velocity, _work, _clock, new RoadmapPlanGate()).Handle(
            new ImportPlanItemsCommand([Entry("plan-a") with { Start = monday }], [new PlanTagEffortDto("plan-a", 14, 0)]),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);

        var item = Stored("plan-a");
        Assert.Equal(monday, item.Window.Start);
        Assert.True(item.Window.End >= monday.AddDays(11), item.Window.ToString()); // 14 open points at 7 a week: two weeks from the 16th
        Assert.Null(item.PlacedByImport);
    }

    /// <summary>Dropped on today, a plan whose work began last week opens today, not on the
    /// day its work began.</summary>
    [Fact]
    public async Task AChosenStartOfToday_WinsOverWorkThatBeganEarlier()
    {
        _work.File("plan-a", Work("first", 7, progress: RoadmapProgress.InProgress, started: Today.AddDays(-7)));

        var result = await new ImportPlanItemsCommandHandler(_plans, _velocity, _work, _clock, new RoadmapPlanGate()).Handle(
            new ImportPlanItemsCommand([Entry("plan-a") with { Start = Today }], [new PlanTagEffortDto("plan-a", 7, 0)]),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);

        Assert.Equal(Today, Stored("plan-a").Window.Start);
    }

    /// <summary>Open work is placed from today, whatever day it was dropped on.</summary>
    [Fact]
    public async Task AChosenStartBeforeToday_StartsToday()
    {
        await ImportedAsync([Entry("plan-a") with { Start = Today.AddDays(-10) }], new PlanTagEffortDto("plan-a", 7, 0));

        Assert.Equal(Today, Stored("plan-a").Window.Start);
    }

    // --- Dependencies -----------------------------------------------------

    [Fact]
    public async Task AnItemStartsTheDayAfterWhatItWaitsOn_EvenWhenThatIsWrittenBelowIt()
    {
        await ImportedAsync(
        [
            Entry("build", after: "design"),
            Entry("design")
        ]);

        var design = Stored("design");
        var build = Stored("build");
        Assert.Equal([design.Id], build.Dependencies.All);

        // Design takes one working week, Monday to Friday; the day after is a Saturday,
        // so build opens on the Monday (local ADR 0019, Verification 2).
        Assert.Equal(new DateOnly(2026, 3, 6), design.Window.End);
        Assert.Equal(new DateOnly(2026, 3, 9), build.Window.Start);
    }

    [Fact]
    public async Task AnAfterNamesASiblingByItsId_WhenOneIsWritten()
    {
        await ImportedAsync(
        [
            Entry("design-work", id: "design"),
            Entry("build", after: "design")
        ]);

        Assert.Equal([Stored("design-work").Id], Stored("build").Dependencies.All);
    }

    [Fact]
    public async Task AnAfterMayNameAnItemAlreadyOnThePlan_ByItsTag()
    {
        var plan = RoadmapPlan.Empty();
        var existing = plan.AddItem(
            "Hand-made", PlannedWindow.Of(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 10)),
            tag: PlanningTag.Of("hand-made")).Value;
        _plans.Current = plan;

        await ImportedAsync([Entry("plan-a", after: "hand-made")]);

        var item = Stored("plan-a");
        Assert.Equal([existing.Id], item.Dependencies.All);
        Assert.Equal(new DateOnly(2026, 4, 13), item.Window.Start); // the Friday's next day is a Saturday
    }

    [Fact]
    public async Task AnAfterThatNamesNothing_IsDroppedAndReported()
    {
        var result = await ImportedAsync([Entry("plan-a", after: "no-such-plan")]);

        Assert.Empty(Stored("plan-a").Dependencies.All);
        var unresolved = Assert.Single(result.UnresolvedDependencies);
        Assert.Equal("plan-a", unresolved.Tag);
        Assert.Equal("no-such-plan", unresolved.After);
    }

    [Fact]
    public async Task ACycle_RefusesTheWholeBatch_AndThePlanIsLeftAsItWas()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Untouched", PlannedWindow.Of(Today, Today.AddDays(2)), tag: PlanningTag.Of("untouched"));
        _plans.Current = plan;

        var result = await ImportAsync(
        [
            Entry("plan-a", after: "plan-b"),
            Entry("plan-b", after: "plan-a"),
            Entry("plan-c")
        ]);

        Assert.True(result.IsFailure);
        Assert.Equal("roadmap.cyclic_dependency", result.Error.Code);
        // Not half-applied: none of the three was created, not even the one with no
        // part in the cycle.
        Assert.Equal(["Untouched"], _plans.Current.Items.Select(item => item.Title));
        Assert.Equal(0, _plans.Saves);
    }

    [Fact]
    public async Task ACycleThroughAnExistingItem_RefusesTheBatchToo()
    {
        await ImportedAsync([Entry("plan-a"), Entry("plan-b", after: "plan-a")]);
        var saves = _plans.Saves;

        var result = await ImportAsync([Entry("plan-c", after: "plan-b"), Entry("plan-a", after: "plan-c")]);

        Assert.True(result.IsFailure);
        Assert.Equal(saves, _plans.Saves);
        Assert.Empty(Stored("plan-a").Dependencies.All);
    }

    [Fact]
    public async Task AReimportThatReversesAnEdge_IsNotMistakenForACycle()
    {
        await ImportedAsync([Entry("plan-a"), Entry("plan-b", after: "plan-a")]);

        await ImportedAsync([Entry("plan-a", after: "plan-b"), Entry("plan-b")]);

        Assert.Equal([Stored("plan-b").Id], Stored("plan-a").Dependencies.All);
        Assert.Empty(Stored("plan-b").Dependencies.All);
    }

    // --- Re-importing -----------------------------------------------------

    [Fact]
    public async Task AReimport_UpdatesTheItemByTag_RatherThanAddingASecond()
    {
        await ImportedAsync([Entry("plan-a", "First title")]);

        var result = await ImportedAsync(
            [new PlanImportEntryDto("Second title", "plan-a", RepositoryAliases: ["fincent"], Priority: PlanningPriority.Low)]);

        var item = Stored("plan-a");
        Assert.Equal("Second title", item.Title);
        Assert.Equal(["fincent"], item.Scope.Aliases);
        Assert.Equal(PlanningPriority.Low, item.Priority);
        Assert.Null(item.Notes);
        Assert.Empty(result.Created);
        Assert.Equal(item.Id, Assert.Single(result.Updated).Id);
    }

    [Fact]
    public async Task AReimport_ReplacesAWindowStillPlacedByImport()
    {
        await ImportedAsync([Entry("plan-a")]);
        var before = Stored("plan-a").Window;

        var result = await ImportedAsync([Entry("plan-a")], new PlanTagEffortDto("plan-a", 12, 0));

        var item = Stored("plan-a");
        Assert.Equal(11, item.Window.Days); // 12 points at 7 a week: 72.9 hours, to the second Thursday
        Assert.Equal(ImportPlacement.Effort, item.PlacedByImport);
        var scheduled = Assert.Single(result.Scheduled);
        Assert.Equal(before.Start, scheduled.PreviousStart);
        Assert.Equal(before.End, scheduled.PreviousEnd);
    }

    [Fact]
    public async Task AReimport_KeepsAWindowMovedByHand_AndStillRevisesTheRest()
    {
        await ImportedAsync([Entry("plan-a", "First title")]);
        var id = Stored("plan-a").Id;
        var moved = await new RescheduleItemCommandHandler(_plans, new RoadmapPlanGate()).Handle(
            new RescheduleItemCommand(id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)),
            TestContext.Current.CancellationToken);
        Assert.True(moved.IsSuccess);

        var result = await ImportedAsync([Entry("plan-a", "Second title")], new PlanTagEffortDto("plan-a", 2, 0));

        var item = Stored("plan-a");
        Assert.Null(item.PlacedByImport);
        Assert.Equal(new DateOnly(2026, 6, 1), item.Window.Start);
        Assert.Equal(new DateOnly(2026, 6, 30), item.Window.End);
        Assert.Equal("Second title", item.Title);
        Assert.Empty(result.Scheduled);
    }

    [Fact]
    public async Task AnUnchangedReimport_SchedulesNothing()
    {
        await ImportedAsync([Entry("plan-a")]);

        var result = await ImportedAsync([Entry("plan-a")]);

        Assert.Empty(result.Scheduled);
    }

    [Fact]
    public async Task AReimport_NeverDeletes_AnItemTheDocumentStoppedDescribing()
    {
        await ImportedAsync([Entry("plan-a"), Entry("plan-b")]);

        await ImportedAsync([Entry("plan-a")]);

        Assert.Equal(2, _plans.Current.Items.Count);
        Assert.NotNull(Stored("plan-b"));
    }

    [Fact]
    public async Task ATagSharedByExistingItems_UpdatesTheFirst_AndReportsTheRest()
    {
        var plan = RoadmapPlan.Empty();
        var window = PlannedWindow.Of(Today, Today.AddDays(3));
        var first = plan.AddItem("First", window, tag: PlanningTag.Of("shared")).Value;
        var second = plan.AddItem("Second", window, tag: PlanningTag.Of("shared")).Value;
        _plans.Current = plan;

        var result = await ImportedAsync([Entry("shared", "Imported")]);

        var ambiguity = Assert.Single(result.AmbiguousTags);
        Assert.Equal("shared", ambiguity.Tag);
        Assert.Equal(first.Id, ambiguity.UpdatedItemId);
        Assert.Equal([second.Id], ambiguity.OtherItemIds);

        var items = _plans.Current.ItemsTagged(PlanningTag.Of("shared"));
        Assert.Equal(["Imported", "Second"], items.Select(item => item.Title));
        Assert.Equal(first.Id, Assert.Single(result.Updated).Id);
    }

    [Fact]
    public async Task AHandMadeItemMatchedByTag_KeepsItsWindow()
    {
        var plan = RoadmapPlan.Empty();
        var window = PlannedWindow.Of(new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 8));
        plan.AddItem("Hand-made", window, tag: PlanningTag.Of("plan-a"));
        _plans.Current = plan;

        var result = await ImportedAsync([Entry("plan-a", "Imported")]);

        Assert.Equal(window, Stored("plan-a").Window);
        Assert.Empty(result.Scheduled);
    }

    // --- Task-level imports under an item (ruling 5) --------------------

    private Task<Result<PlanImportResultDto>> ImportAsync(
        IReadOnlyList<PlanImportEntryDto> entries,
        IReadOnlyList<PlanImportEntryDto> createIfMissing,
        params PlanTagEffortDto[] effort)
    {
        File(effort);
        return new ImportPlanItemsCommandHandler(_plans, _velocity, _work, _clock, new RoadmapPlanGate())
            .Handle(new ImportPlanItemsCommand(entries, effort, createIfMissing), TestContext.Current.CancellationToken);
    }

    /// <summary>Re-placed the way the keep-up projection places it: work nobody has begun
    /// is laid out from today, not from the start an earlier import gave it.</summary>
    [Fact]
    public async Task GatheredEffortAlone_RelaysAnEffortPlacedItemNobodyBegan_FromToday()
    {
        await ImportedAsync([Entry("plan-a")]);
        _clock.Advance(TimeSpan.FromDays(3)); // Thursday 5 March

        var result = await ImportedAsync([], new PlanTagEffortDto("plan-a", 8, 0));

        // 8 points at 7 a week: 48.6 hours from the Thursday, to the next Thursday.
        Assert.Equal(PlannedWindow.Of(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 12)), Stored("plan-a").Window);
        Assert.Equal(Stored("plan-a").Id, Assert.Single(result.Relengthened).Id);
        Assert.Empty(result.Created);
        Assert.Empty(result.Updated);
        var scheduled = Assert.Single(result.Scheduled);
        Assert.Equal(Today, scheduled.PreviousStart);
        Assert.Equal(Today.AddDays(4), scheduled.PreviousEnd);
    }

    /// <summary>AC8 at import: work already begun keeps the day it began, and only what is
    /// still open is placed.</summary>
    [Fact]
    public async Task GatheredEffortAlone_KeepsTheDayABegunItemsWorkBegan()
    {
        await ImportedAsync([Entry("plan-a")]);
        _clock.Advance(TimeSpan.FromDays(3)); // Thursday 5 March
        _work.File(
            "plan-a",
            Work("first", 7, progress: RoadmapProgress.InProgress, started: new DateOnly(2026, 3, 3)),
            Work("second", 7));

        // The tag is handed over as touched; what it gathers is the work filed above.
        var result = await new ImportPlanItemsCommandHandler(_plans, _velocity, _work, _clock, new RoadmapPlanGate()).Handle(
            new ImportPlanItemsCommand([], [new PlanTagEffortDto("plan-a", 14, 0)]),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);

        var item = Stored("plan-a");
        Assert.Equal(new DateOnly(2026, 3, 3), item.Window.Start);
        Assert.True(item.Window.End > new DateOnly(2026, 3, 12), item.Window.ToString()); // more than one week open
        Assert.Equal(ImportPlacement.Effort, item.PlacedByImport);
    }

    [Fact]
    public async Task GatheredEffortAlone_RelengthensAtThePaceOfTheItemsRepository()
    {
        await ImportedAsync([Entry("plan-a")]); // filed under backlog
        _velocity.ByRepository["backlog"] = 2;

        await ImportedAsync([], new PlanTagEffortDto("plan-a", 4, 0));

        Assert.Equal(12, Stored("plan-a").Window.Days); // 4 points at 2 a week: two working weeks
    }

    [Fact]
    public async Task GatheredEffortAlone_LeavesADueDateEndAndAHandPlacedWindow()
    {
        await ImportedAsync([Entry("plan-a", due: new DateOnly(2026, 3, 20))]);
        var dueDated = Stored("plan-a").Window;

        var plan = _plans.Current;
        var handPlaced = PlannedWindow.Of(new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 8));
        plan.AddItem("Hand-made", handPlaced, tag: PlanningTag.Of("plan-b"));
        _plans.Current = plan;
        var saves = _plans.Saves;

        var result = await ImportedAsync([], new PlanTagEffortDto("plan-a", 30, 0), new PlanTagEffortDto("plan-b", 30, 0));

        Assert.Equal(dueDated, Stored("plan-a").Window);
        Assert.Equal(handPlaced, Stored("plan-b").Window);
        Assert.Empty(result.Relengthened);
        Assert.Equal(saves, _plans.Saves); // nothing changed, so nothing was written
    }

    [Fact]
    public async Task CreateIfMissing_CreatesAnItemForATagNoItemCarries()
    {
        var result = await ImportAsync([], [Entry("plan-a", "Plan a")], new PlanTagEffortDto("plan-a", 3, 0));

        Assert.True(result.IsSuccess);
        Assert.Equal("Plan a", Assert.Single(result.Value.Created).Title);
        Assert.Equal(PlannedWindow.Of(Today, Today.AddDays(2)), Stored("plan-a").Window);
    }

    /// <summary>An entry the importer made up does not rewrite an existing item's
    /// title; the item is only re-lengthened from what it gathers.</summary>
    [Fact]
    public async Task CreateIfMissing_LeavesAnExistingItemToTheRelengthening()
    {
        await ImportedAsync([Entry("plan-a", "Chosen by a person")]);

        var result = await ImportAsync([], [Entry("plan-a", "Plan a")], new PlanTagEffortDto("plan-a", 8, 0));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Created);
        Assert.Equal("Chosen by a person", Stored("plan-a").Title);
        Assert.Single(result.Value.Relengthened);
    }

    [Fact]
    public async Task CreateIfMissing_YieldsToAnEntryOfTheDocumentWithTheSameTag()
    {
        var result = await ImportAsync([Entry("plan-a", "Written")], [Entry("plan-a", "Made up")]);

        Assert.True(result.IsSuccess);
        Assert.Equal("Written", Assert.Single(result.Value.Created).Title);
    }

    // --- The person's own gestures ----------------------------------------

    [Fact]
    public async Task EditingTheWindow_ClearsTheImportProvenance()
    {
        await ImportedAsync([Entry("plan-a")]);
        var item = Stored("plan-a");

        var edited = await new UpdateItemCommandHandler(_plans, new RoadmapPlanGate()).Handle(
            new UpdateItemCommand(item.Id, item.Title, Today, Today.AddDays(20), item.Priority, item.Scope.Aliases,
                Tag: item.Tag.Value),
            TestContext.Current.CancellationToken);

        Assert.True(edited.IsSuccess);
        Assert.Null(Stored("plan-a").PlacedByImport);
    }

    /// <summary>
    /// The backlog the importer gathers from, by tag: what the tasks filed under a tag
    /// are, as Roadmap's own read port answers it. Counts its reads, so a test can see the
    /// whole plan is gathered once.
    /// </summary>
    private sealed class TaggedWork : IRoadmapItemRollup
    {
        private readonly Dictionary<string, RoadmapGatheredLink[]> _byTag = new(StringComparer.Ordinal);

        public int Reads { get; private set; }

        /// <summary>Files <paramref name="tasks"/> under <paramref name="tag"/>, replacing
        /// what was filed there before.</summary>
        public void File(string tag, params RoadmapGatheredLink[] tasks) => _byTag[PlanningTag.Of(tag).Value] = tasks;

        public Task<RoadmapItemRollupDto> GatherAsync(RoadmapItemDto item, CancellationToken cancellationToken = default) =>
            Task.FromResult(Of(item));

        public Task<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>> GatherPlanAsync(
            RoadmapPlanDto plan,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>>(plan.Items.ToDictionary(item => item.Id, Of));
        }

        private RoadmapItemRollupDto Of(RoadmapItemDto item) =>
            _byTag.TryGetValue(item.Tag, out var tasks) ? new RoadmapItemRollupDto(tasks, []) : RoadmapItemRollupDto.Empty;
    }

    /// <summary>A store that, like the real one, hands out a fresh plan on every load
    /// and keeps only what was saved.</summary>
    private sealed class SnapshotPlanRepository : IRoadmapPlanRepository
    {
        private RoadmapPlan _stored = RoadmapPlan.Empty();

        public int Saves { get; private set; }

        /// <summary>What is stored now, as a fresh copy; setting it seeds the store
        /// without counting as a save.</summary>
        public RoadmapPlan Current
        {
            get => Copy(_stored);
            set => _stored = Copy(value);
        }

        public Task<RoadmapPlan> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Copy(_stored));

        public Task SaveAsync(RoadmapPlan plan, CancellationToken cancellationToken = default)
        {
            _stored = Copy(plan);
            Saves++;
            return Task.CompletedTask;
        }

        private static RoadmapPlan Copy(RoadmapPlan plan) => RoadmapPlan.Rehydrate(
            plan.Items.Select(item => new RoadmapItem(
                item.Id, item.Title, item.Window, item.Priority, item.Scope, item.Lane,
                Dependencies.Of(item.Dependencies.All), item.TaskId, item.Notes, item.Tag,
                item.KnowledgeRefs, item.PlacedByImport, item.EndPinned)),
            plan.Milestones.Select(milestone => new Milestone(
                milestone.Id, milestone.Title, milestone.On, milestone.Kind, milestone.Scope, milestone.Lane,
                Dependencies.Of(milestone.Dependencies.All), milestone.IsPlanWide)),
            plan.BandColours);
    }
}
