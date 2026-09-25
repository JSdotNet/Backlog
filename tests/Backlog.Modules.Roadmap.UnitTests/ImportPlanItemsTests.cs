using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Features.ImportPlanItems;
using Backlog.Modules.Roadmap.Features.RescheduleItem;
using Backlog.Modules.Roadmap.Features.UpdateItem;
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
    private readonly FixedVelocity _velocity = new(1);
    private readonly FakeTimeProvider _clock = new();

    public ImportPlanItemsTests()
    {
        _clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        _clock.SetUtcNow(new DateTimeOffset(Today, new TimeOnly(9, 0), TimeSpan.Zero));
    }

    private Task<Result<PlanImportResultDto>> ImportAsync(
        IReadOnlyList<PlanImportEntryDto> entries,
        params PlanTagEffortDto[] effort) =>
        new ImportPlanItemsCommandHandler(_plans, _velocity, _clock)
            .Handle(new ImportPlanItemsCommand(entries, effort), TestContext.Current.CancellationToken);

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
        _velocity.StoryPointsPerDay = 2;

        await ImportedAsync([Entry("plan-a")], new PlanTagEffortDto("plan-a", 7, 1));

        Assert.Equal(4, Stored("plan-a").Window.Days); // 7 / 2 = 3.5, rounded up
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
        Assert.Equal(design.Window.End.AddDays(1), build.Window.Start);
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
        Assert.Equal(new DateOnly(2026, 4, 11), item.Window.Start);
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
        Assert.Equal(12, item.Window.Days);
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
        var moved = await new RescheduleItemCommandHandler(_plans).Handle(
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
        params PlanTagEffortDto[] effort) =>
        new ImportPlanItemsCommandHandler(_plans, _velocity, _clock)
            .Handle(new ImportPlanItemsCommand(entries, effort, createIfMissing), TestContext.Current.CancellationToken);

    [Fact]
    public async Task GatheredEffortAlone_RelengthensAnEffortPlacedItem_KeepingItsStart()
    {
        await ImportedAsync([Entry("plan-a")]);
        _clock.Advance(TimeSpan.FromDays(3)); // the start stays where it was placed

        var result = await ImportedAsync([], new PlanTagEffortDto("plan-a", 8, 0));

        Assert.Equal(PlannedWindow.Of(Today, Today.AddDays(7)), Stored("plan-a").Window);
        Assert.Equal(Stored("plan-a").Id, Assert.Single(result.Relengthened).Id);
        Assert.Empty(result.Created);
        Assert.Empty(result.Updated);
        var scheduled = Assert.Single(result.Scheduled);
        Assert.Equal(Today.AddDays(4), scheduled.PreviousEnd);
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

        var edited = await new UpdateItemCommandHandler(_plans).Handle(
            new UpdateItemCommand(item.Id, item.Title, Today, Today.AddDays(20), item.Priority, item.Scope.Aliases,
                Tag: item.Tag.Value),
            TestContext.Current.CancellationToken);

        Assert.True(edited.IsSuccess);
        Assert.Null(Stored("plan-a").PlacedByImport);
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
                item.KnowledgeRefs, item.PlacedByImport)),
            plan.Milestones.Select(milestone => new Milestone(
                milestone.Id, milestone.Title, milestone.On, milestone.Kind, milestone.Scope, milestone.Lane,
                Dependencies.Of(milestone.Dependencies.All), milestone.IsPlanWide)),
            plan.BandColours);
    }

    private sealed class FixedVelocity(decimal storyPointsPerDay) : IPlanningVelocity
    {
        public decimal StoryPointsPerDay { get; set; } = storyPointsPerDay;

        public Task<decimal> GetStoryPointsPerDayAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(StoryPointsPerDay);
    }
}
