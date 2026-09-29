using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Services;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// A window still sized by its effort is read, not stored: its end is derived from the
/// effort its tasks register now and the pace in use now, from the start the plan keeps
/// (ADR 0013, ruling 5 as amended; local ADR 0018). A pace change therefore redraws it
/// without writing the plan, and a device that pulls the pace draws the same bar.
/// </summary>
public class EffortWindowTests
{
    private static readonly DateOnly Start = new(2026, 3, 2);

    private static readonly PacesInUseDto SevenAWeek = new(7m, new Dictionary<string, decimal>());

    private static RoadmapItemDto Item(
        int days = 5,
        ImportPlacement? placement = ImportPlacement.Effort,
        params string[] repositories) =>
        new(
            Guid.NewGuid(),
            "Plan",
            Start,
            Start.AddDays(days - 1),
            PlanningPriority.Medium,
            repositories,
            null,
            null,
            [],
            PlacedByImport: placement);

    private static RoadmapItemRollupDto Gathers(int effort, RoadmapProgress progress = RoadmapProgress.Ready) =>
        new([new RoadmapGatheredLink("task-1", "Task", effort, RollupOrigin.Tag, progress)], []);

    private static PacesInUseDto Paces(decimal global, params (string Alias, decimal Pace)[] repositories) =>
        new(global, repositories.ToDictionary(pair => pair.Alias, pair => pair.Pace, StringComparer.OrdinalIgnoreCase));

    [Theory]
    [InlineData(0, 7)]
    [InlineData(10, 7)]
    [InlineData(3, 14)]
    [InlineData(1, 28)]
    [InlineData(5, 3.5)]
    [InlineData(4, 4)]
    [InlineData(20, 10)]
    public void TheImportAndTheReadingShareOneFormula(int effort, double velocity)
    {
        Assert.Equal(ImportedPlanPlacement.Days(effort, (decimal)velocity), EffortWindow.Days(effort, (decimal)velocity));
        Assert.Equal(ImportedPlanPlacement.DefaultSpanDays, EffortWindow.DefaultSpanDays);
        Assert.Equal(ImportedPlanPlacement.MinimumSpanDays, EffortWindow.MinimumSpanDays);
    }

    [Fact]
    public void AnEffortPlacedItem_IsReadAtThePaceInUse_KeepingItsStart()
    {
        var item = Item(days: 14);

        var derived = EffortWindow.Derive(item, Gathers(14), Paces(14m));

        Assert.Equal(Start, derived.Start);
        Assert.Equal(Start.AddDays(6), derived.End); // 14 points at 14 a week
        Assert.Equal(ImportPlacement.Effort, derived.PlacedByImport);
    }

    [Fact]
    public void TheDerivedWindowIsTheOneTheImportsOwnPlacementWouldStore()
    {
        var item = Item(days: 3, repositories: "backlog");
        var paces = Paces(7m, ("backlog", 4m));

        var derived = EffortWindow.Derive(item, Gathers(9), paces);
        var (placed, _) = ImportedPlanPlacement.Place(item.Start, due: null, 9, paces.For(item.RepositoryAliases));

        Assert.Equal(placed.Start, derived.Start);
        Assert.Equal(placed.End, derived.End);
    }

    [Fact]
    public void EachItemIsReadAtItsOwnRepositorysPace()
    {
        var paces = Paces(7m, ("backlog", 14m), ("site", 2m));

        Assert.Equal(7, EffortWindow.Derive(Item(days: 14, repositories: "backlog"), Gathers(14), paces).Days);
        Assert.Equal(49, EffortWindow.Derive(Item(days: 14, repositories: "site"), Gathers(14), paces).Days);
        Assert.Equal(49, EffortWindow.Derive(Item(days: 14, repositories: ["backlog", "site"]), Gathers(14), paces).Days); // the slower
        Assert.Equal(14, EffortWindow.Derive(Item(days: 3), Gathers(14), paces).Days); // the global pace
    }

    [Theory]
    [InlineData(ImportPlacement.DueDate)]
    [InlineData(null)]
    public void ADueDateOrAHandPlacedWindow_IsReadAsStored(ImportPlacement? placement)
    {
        var item = Item(days: 10, placement: placement);

        Assert.Same(item, EffortWindow.Derive(item, Gathers(40), Paces(1m)));
    }

    [Fact]
    public void AnItemThatGathersNoTask_IsReadAsStored()
    {
        var item = Item(days: 10);

        Assert.Same(item, EffortWindow.Derive(item, null, SevenAWeek));
        Assert.Same(item, EffortWindow.Derive(item, RoadmapItemRollupDto.Empty, SevenAWeek));
    }

    [Fact]
    public void AFinishedItem_IsReadAsStored()
    {
        var item = Item(days: 10);

        Assert.Same(item, EffortWindow.Derive(item, Gathers(1, RoadmapProgress.Done), SevenAWeek));
    }

    [Fact]
    public void AnItemWithNothingEstimated_SpansTheDefault()
    {
        var derived = EffortWindow.Derive(Item(days: 30), Gathers(0), SevenAWeek);

        Assert.Equal(EffortWindow.DefaultSpanDays, derived.Days);
    }

    [Fact]
    public void APaceNobodyCouldDivideBy_LeavesTheStoredWindow()
    {
        var item = Item(days: 10);

        Assert.Same(item, EffortWindow.Derive(item, Gathers(3), Paces(0m)));
    }

    [Fact]
    public async Task APlanIsReadWithEveryEffortPlacedWindowDerived_GatheringOnlyThoseAndReadingThePacesOnce()
    {
        var sized = Item(days: 3);
        var hand = Item(days: 3, placement: null);
        var plan = new RoadmapPlanDto([sized, hand], [], []);
        var rollups = new CountingRollup(Gathers(14));
        var velocity = new FixedVelocity(14);

        var read = await plan.WithDerivedWindowsAsync(rollups, velocity, TestContext.Current.CancellationToken);

        Assert.Equal(7, Assert.Single(read.Items, item => item.Id == sized.Id).Days);
        Assert.Equal(3, Assert.Single(read.Items, item => item.Id == hand.Id).Days);
        Assert.Equal([sized.Id], rollups.Gathered);
        Assert.Equal(1, velocity.Reads);
    }

    [Fact]
    public async Task APlanWithNoEffortPlacedWindow_ReadsNothingMore()
    {
        var plan = new RoadmapPlanDto([Item(placement: null)], [], []);
        var rollups = new CountingRollup(Gathers(14));
        var velocity = new FixedVelocity(14);

        var read = await plan.WithDerivedWindowsAsync(rollups, velocity, TestContext.Current.CancellationToken);

        Assert.Same(plan, read);
        Assert.Empty(rollups.Gathered);
        Assert.Equal(0, velocity.Reads);
    }

    /// <summary>Answers every item with one rollup, and remembers which items it was
    /// asked to gather.</summary>
    private sealed class CountingRollup(RoadmapItemRollupDto rollup) : IRoadmapItemRollup
    {
        public List<Guid> Gathered { get; } = [];

        public Task<RoadmapItemRollupDto> GatherAsync(RoadmapItemDto item, CancellationToken cancellationToken = default) =>
            Task.FromResult(rollup);

        public Task<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>> GatherPlanAsync(
            RoadmapPlanDto plan,
            CancellationToken cancellationToken = default)
        {
            Gathered.AddRange(plan.Items.Select(item => item.Id));
            return Task.FromResult<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>>(
                plan.Items.ToDictionary(item => item.Id, _ => rollup));
        }
    }
}
