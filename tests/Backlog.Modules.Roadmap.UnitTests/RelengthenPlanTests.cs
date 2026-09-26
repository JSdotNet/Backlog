using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Features.RelengthenPlan;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// A person changing their pace: every item whose window is still sized by its effort
/// is re-lengthened at the new pace, and nothing else moves (ADR 0013, ruling 5 as
/// amended).
/// </summary>
public class RelengthenPlanTests
{
    private static readonly DateOnly Start = new(2026, 3, 2);

    private readonly SnapshotPlanRepository _plans = new();
    private readonly FixedVelocity _velocity = new(7);

    private Task<Result<IReadOnlyList<RoadmapItemDto>>> RelengthenAsync(Dictionary<Guid, int> gathered) =>
        new RelengthenPlanCommandHandler(_plans, _velocity)
            .Handle(new RelengthenPlanCommand(gathered), TestContext.Current.CancellationToken);

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

    private RoadmapItem Stored(Guid id) => Assert.Single(_plans.Current.Items, item => item.Id == id);

    [Fact]
    public async Task EveryEffortPlacedItem_IsRedrawnAtTheNewPace_KeepingItsStart()
    {
        var a = Imported("plan-a", days: 14);   // 14 points at 7 a week
        var b = Imported("plan-b", days: 7);    // 7 points at 7 a week
        _velocity.StoryPointsPerWeek = 14;

        var result = await RelengthenAsync(new() { [a] = 14, [b] = 7 });

        Assert.True(result.IsSuccess);
        Assert.Equal(PlannedWindow.Of(Start, Start.AddDays(6)), Stored(a).Window);
        Assert.Equal(PlannedWindow.Of(Start, Start.AddDays(3)), Stored(b).Window); // 3.5 days, rounded up
        Assert.Equal(ImportPlacement.Effort, Stored(a).PlacedByImport);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(1, _plans.Saves);
    }

    [Fact]
    public async Task ADueDateOrAHandPlacedWindow_IsLeftAlone()
    {
        var due = Imported("plan-due", days: 10, placement: ImportPlacement.DueDate);
        var plan = _plans.Current;
        var hand = plan.AddItem("by hand", PlannedWindow.Of(Start, Start.AddDays(2)));
        Assert.True(hand.IsSuccess);
        _plans.Current = plan;
        _velocity.StoryPointsPerWeek = 1;

        var result = await RelengthenAsync(new() { [due] = 40, [hand.Value.Id] = 40 });

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        Assert.Equal(PlannedWindow.Of(Start, Start.AddDays(9)), Stored(due).Window);
        Assert.Equal(0, _plans.Saves);
    }

    [Fact]
    public async Task AnItemNobodySaidTheGatheringOf_IsLeftAlone()
    {
        var id = Imported("plan-a", days: 5);
        _velocity.StoryPointsPerWeek = 1;

        var result = await RelengthenAsync([]);

        Assert.Empty(result.Value);
        Assert.Equal(PlannedWindow.Of(Start, Start.AddDays(4)), Stored(id).Window);
    }

    [Fact]
    public async Task APaceThatMakesTheSameWindows_SavesNothing()
    {
        var id = Imported("plan-a", days: 7);

        var result = await RelengthenAsync(new() { [id] = 7 });

        Assert.Empty(result.Value);
        Assert.Equal(0, _plans.Saves);
    }

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
