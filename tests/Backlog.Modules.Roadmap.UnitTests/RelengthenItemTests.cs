using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Features.RelengthenItem;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// "Update from tasks": re-lengthening one item from what its tasks register now, by
/// the effort rule a task-level re-import applies (ADR 0013, ruling 5) — and the
/// proposal asked for before the action is offered. Through the handlers and a store
/// that hands out a fresh plan on every load, so a refusal can be seen to have written
/// nothing.
/// </summary>
public class RelengthenItemTests
{
    private static readonly DateOnly Start = new(2026, 3, 2);

    private readonly SnapshotPlanRepository _plans = new();
    private readonly FixedVelocity _velocity = new(7);

    private Task<Result<RoadmapRelengthResultDto>> RelengthenAsync(Guid itemId, int gatheredEffort) =>
        new RelengthenItemCommandHandler(_plans, _velocity)
            .Handle(new RelengthenItemCommand(itemId, gatheredEffort), TestContext.Current.CancellationToken);

    private Task<RoadmapRelengthProposalDto?> ProposeAsync(Guid itemId, int gatheredEffort) =>
        new ProposeRelengthQueryHandler(_plans, _velocity)
            .Handle(new ProposeRelengthQuery(itemId, gatheredEffort), TestContext.Current.CancellationToken);

    /// <summary>An item as an import leaves it: placed by <paramref name="placement"/>,
    /// over <paramref name="days"/> days from <see cref="Start"/>.</summary>
    private Guid Imported(string tag, int days = 5, ImportPlacement placement = ImportPlacement.Effort)
    {
        var plan = _plans.Current;
        var added = plan.AddImportedItem(
            tag,
            PlanningTag.Of(tag),
            PlannedWindow.Of(Start, Start.AddDays(days - 1)),
            placement);
        Assert.True(added.IsSuccess);
        _plans.Current = plan;
        return added.Value.Id;
    }

    private Guid HandPlaced(string tag, DateOnly start, DateOnly end, params Guid[] after)
    {
        var plan = _plans.Current;
        var added = plan.AddItem(tag, PlannedWindow.Of(start, end), tag: PlanningTag.Of(tag));
        Assert.True(added.IsSuccess);
        foreach (var dependsOn in after) Assert.True(plan.AddDependency(added.Value.Id, dependsOn).IsSuccess);
        _plans.Current = plan;
        return added.Value.Id;
    }

    private RoadmapItem Stored(Guid id) => Assert.Single(_plans.Current.Items, item => item.Id == id);

    // --- The command ------------------------------------------------------

    [Fact]
    public async Task AnEffortPlacedItem_IsLengthenedFromItsTasks_KeepingItsStartAndItsPlacement()
    {
        var id = Imported("plan-a");

        var result = await RelengthenAsync(id, 8);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlannedWindow.Of(Start, Start.AddDays(7)), Stored(id).Window);
        Assert.Equal(ImportPlacement.Effort, Stored(id).PlacedByImport);
        Assert.Equal(Start, result.Value.PreviousStart);
        Assert.Equal(Start.AddDays(4), result.Value.PreviousEnd);
        Assert.Equal(Start.AddDays(7), result.Value.Item.End);
        Assert.Equal(ImportPlacement.Effort, result.Value.Item.PlacedByImport);
        Assert.Equal(1, _plans.Saves);
    }

    [Fact]
    public async Task AnEffortPlacedItem_IsShortenedWhenItsTasksNowAddUpToLess()
    {
        var id = Imported("plan-a", days: 10);

        var result = await RelengthenAsync(id, 3);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlannedWindow.Of(Start, Start.AddDays(2)), Stored(id).Window);
    }

    [Fact]
    public async Task TheReadersPaceSetsTheLength()
    {
        var id = Imported("plan-a");
        _velocity.StoryPointsPerWeek = 14;

        await RelengthenAsync(id, 9); // 9 points at 14 a week = 4.5 days, rounded up

        Assert.Equal(PlannedWindow.Of(Start, Start.AddDays(4)), Stored(id).Window);
    }

    [Fact]
    public async Task AHandPlacedItem_IsRefused_AndNothingIsWritten()
    {
        var window = PlannedWindow.Of(Start, Start.AddDays(4));
        var id = HandPlaced("plan-a", window.Start, window.End);

        var result = await RelengthenAsync(id, 20);

        Assert.True(result.IsFailure);
        Assert.Equal("roadmap.not_placed_by_effort", result.Error.Code);
        Assert.Contains("moved by hand", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(window, Stored(id).Window);
        Assert.Equal(0, _plans.Saves);
    }

    [Fact]
    public async Task ADueDatePlacedItem_IsRefused_AndKeepsTheEndThePersonWrote()
    {
        var id = Imported("plan-a", placement: ImportPlacement.DueDate);
        var window = Stored(id).Window;

        var result = await RelengthenAsync(id, 20);

        Assert.True(result.IsFailure);
        Assert.Equal("roadmap.not_placed_by_effort", result.Error.Code);
        Assert.Contains("due date", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(window, Stored(id).Window);
        Assert.Equal(ImportPlacement.DueDate, Stored(id).PlacedByImport);
        Assert.Equal(0, _plans.Saves);
    }

    [Fact]
    public async Task AMissingItem_IsRefused_AndNothingIsWritten()
    {
        var result = await RelengthenAsync(Guid.NewGuid(), 8);

        Assert.True(result.IsFailure);
        Assert.Equal("roadmap.item_not_found", result.Error.Code);
        Assert.Equal(0, _plans.Saves);
    }

    [Fact]
    public async Task AWindowTheTasksAlreadyMake_SucceedsWithoutASave()
    {
        var id = Imported("plan-a", days: 8);

        var result = await RelengthenAsync(id, 8);

        Assert.True(result.IsSuccess);
        Assert.Equal(Start.AddDays(7), result.Value.PreviousEnd);
        Assert.Equal(Start.AddDays(7), result.Value.Item.End);
        Assert.Empty(result.Value.NowOverlapping);
        Assert.Equal(0, _plans.Saves);
    }

    [Fact]
    public async Task ADependentThatNowOverlaps_IsNamed_AndNotMoved()
    {
        var id = Imported("plan-a");
        var dependentWindow = PlannedWindow.Of(Start.AddDays(5), Start.AddDays(9));
        var dependent = HandPlaced("plan-b", dependentWindow.Start, dependentWindow.End, id);

        var result = await RelengthenAsync(id, 8);

        Assert.True(result.IsSuccess);
        var overlapping = Assert.Single(result.Value.NowOverlapping);
        Assert.Equal(dependent, overlapping.Id);
        Assert.Equal("plan-b", overlapping.Title);
        Assert.Equal(dependentWindow, Stored(dependent).Window);
    }

    [Fact]
    public async Task ADependentThatAlreadyOverlapped_IsNotNews()
    {
        var id = Imported("plan-a");
        HandPlaced("plan-b", Start.AddDays(2), Start.AddDays(9), id); // opens before plan-a closes already

        var result = await RelengthenAsync(id, 8);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.NowOverlapping);
    }

    // --- The proposal -----------------------------------------------------

    [Fact]
    public async Task TheProposal_KeepsTheStartAndNamesBothEnds()
    {
        var id = Imported("plan-a");

        var proposal = await ProposeAsync(id, 8);

        Assert.NotNull(proposal);
        Assert.Equal(new RoadmapRelengthProposalDto(id, Start, Start.AddDays(4), Start.AddDays(7), 8), proposal);
        Assert.Equal(0, _plans.Saves);
    }

    [Fact]
    public async Task NothingIsProposed_ForAMissingItem() =>
        Assert.Null(await ProposeAsync(Guid.NewGuid(), 8));

    [Fact]
    public async Task NothingIsProposed_ForAHandPlacedItem() =>
        Assert.Null(await ProposeAsync(HandPlaced("plan-a", Start, Start.AddDays(4)), 20));

    [Fact]
    public async Task NothingIsProposed_ForADueDatePlacedItem() =>
        Assert.Null(await ProposeAsync(Imported("plan-a", placement: ImportPlacement.DueDate), 20));

    [Fact]
    public async Task NothingIsProposed_WhenTheTasksMakeTheWindowItAlreadyHas() =>
        Assert.Null(await ProposeAsync(Imported("plan-a", days: 8), 8));

    /// <summary>The store the real one is: a fresh plan on every load, and a count of
    /// every save.</summary>
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

    private sealed class FixedVelocity(decimal storyPointsPerWeek) : IPlanningVelocity
    {
        public decimal StoryPointsPerWeek { get; set; } = storyPointsPerWeek;

        public Task<decimal> GetStoryPointsPerWeekAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(StoryPointsPerWeek);
    }
}
