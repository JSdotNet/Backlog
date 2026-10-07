using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Features.ImportPlanItems;
using Backlog.Modules.Roadmap.Features.KeepUpWithWork;
using Backlog.Modules.Roadmap.Services;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// The keep-up writer: an item the import sized by its effort is laid out again from
/// its unfinished work and the moved window is stored (ADR 0013, ruling 5 as amended on
/// 2026-09-27 and 2026-10-07; local ADR 0018, "The daily re-projection counts as a
/// save"). Through the handler and a store that hands out a fresh plan on every load,
/// so a run that moved nothing can be seen to have saved nothing.
/// <para>
/// Today is Monday 12 October 2026, on the default working week. <c>app</c> runs at 8
/// points a week; anything else at the global 7.
/// </para>
/// </summary>
public class KeepUpWithWorkTests
{
    private static readonly DateOnly Today = new(2026, 10, 12);

    private readonly SnapshotPlanRepository _plans = new();
    private readonly FixedVelocity _velocity = new(7);
    private readonly TaggedWork _work = new();
    private readonly RoadmapPlanGate _gate = new();

    public KeepUpWithWorkTests() => _velocity.ByRepository["app"] = 8;

    private Task<IReadOnlyList<RoadmapItemScheduledDto>> KeepUpAsync(DateOnly? today = null, IRoadmapCatchUp? catchUp = null) =>
        new KeepUpWithWorkCommandHandler(_plans, _velocity, _work, _gate, catchUp)
            .Handle(new KeepUpWithWorkCommand(today ?? Today), TestContext.Current.CancellationToken);

    /// <summary>An item as an import leaves it, filed under <c>app</c>, over
    /// <paramref name="start"/>–<paramref name="end"/>.</summary>
    private Guid Imported(
        string tag,
        DateOnly start,
        DateOnly end,
        ImportPlacement placement = ImportPlacement.Effort,
        params Guid[] after)
    {
        var plan = _plans.Current;
        var added = plan.AddImportedItem(
            tag,
            PlanningTag.Of(tag),
            PlannedWindow.Of(start, end),
            placement,
            scope: RepositoryScope.Of(["app"]));
        Assert.True(added.IsSuccess);
        foreach (var dependsOn in after) Assert.True(plan.AddDependency(added.Value.Id, dependsOn).IsSuccess);
        _plans.Current = plan;
        return added.Value.Id;
    }

    private static RoadmapGatheredLink Task(
        string key,
        int? effort,
        RoadmapProgress progress = RoadmapProgress.Ready,
        DateOnly? started = null,
        DateOnly? completed = null) =>
        new(key, key, effort, RollupOrigin.Tag, progress, [], ["app"], started, completed);

    private RoadmapItem Stored(Guid id) => Assert.Single(_plans.Current.Items, item => item.Id == id);

    // --- What moves ---------------------------------------------------------

    [Fact]
    public async Task AnEffortPlacedItemLeftBehind_IsStoredFromToday_StillPlacedByEffort()
    {
        var id = Imported("plan-a", new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2));
        _work.File("plan-a", Task("a-1", 8));

        var scheduled = await KeepUpAsync();

        var item = Stored(id);
        Assert.Equal(PlannedWindow.Of(Today, new DateOnly(2026, 10, 16)), item.Window);
        Assert.Equal(ImportPlacement.Effort, item.PlacedByImport);
        Assert.Equal(1, _plans.Saves);

        var moved = Assert.Single(scheduled);
        Assert.Equal(id, moved.RoadmapItemId);
        Assert.Equal((Today, new DateOnly(2026, 10, 16)), (moved.Start, moved.End));
        Assert.Equal((new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2)), (moved.PreviousStart, moved.PreviousEnd));
    }

    [Fact]
    public async Task ASecondRunTheSameDay_SavesNothing_AndReportsNothing()
    {
        Imported("plan-a", new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2));
        _work.File("plan-a", Task("a-1", 8));
        await KeepUpAsync();

        var again = await KeepUpAsync();

        Assert.Empty(again);
        Assert.Equal(1, _plans.Saves);
    }

    [Fact]
    public async Task ATaskMarkedDone_PullsTheEndIn_ToWhatIsLeft()
    {
        var id = Imported("plan-a", Today, Today);
        _work.File("plan-a", Task("a-1", 4), Task("a-2", 4), Task("a-3", 4));
        await KeepUpAsync();
        var before = Stored(id).Window;

        _work.File("plan-a", Task("a-1", 4, RoadmapProgress.Done, started: Today, completed: Today), Task("a-2", 4), Task("a-3", 4));
        var scheduled = await KeepUpAsync();

        // 8 points left at 8 a week, from today: the rest of this week.
        var after = Stored(id).Window;
        Assert.True(before.End > new DateOnly(2026, 10, 16));
        Assert.Equal(PlannedWindow.Of(Today, new DateOnly(2026, 10, 16)), after);
        Assert.Equal(before.End, Assert.Single(scheduled).PreviousEnd);
    }

    [Fact]
    public async Task ABegunItem_KeepsTheDayItsWorkBegan()
    {
        var id = Imported("plan-a", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9));
        _work.File("plan-a", Task("a-1", 4, RoadmapProgress.InProgress, started: new DateOnly(2026, 10, 6)), Task("a-2", 4));

        await KeepUpAsync();

        // Not today, and not the stored start either: the day the first task began.
        Assert.Equal(new DateOnly(2026, 10, 6), Stored(id).Window.Start);
    }

    [Fact]
    public async Task AnUnstartedSuccessor_StartsTheWorkedDayAfterItsPredecessorsNewEnd()
    {
        var before = Imported("plan-a", new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2));
        var after = Imported("plan-b", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9), after: before);
        _work.File("plan-a", Task("a-1", 8));
        _work.File("plan-b", Task("b-1", 8));

        var scheduled = await KeepUpAsync();

        Assert.Equal(PlannedWindow.Of(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 23)), Stored(after).Window);
        Assert.Equal(2, scheduled.Count);
        Assert.Equal(1, _plans.Saves);
    }

    // --- What never moves ----------------------------------------------------

    [Fact]
    public async Task AnItemPlacedByItsDueDate_IsNeverWritten()
    {
        var id = Imported("plan-a", new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2), ImportPlacement.DueDate);
        _work.File("plan-a", Task("a-1", 8));

        var scheduled = await KeepUpAsync();

        Assert.Empty(scheduled);
        Assert.Equal(PlannedWindow.Of(new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2)), Stored(id).Window);
        Assert.Equal(0, _plans.Saves);
    }

    [Fact]
    public async Task AnItemPlacedByHand_IsNeverWritten()
    {
        var plan = _plans.Current;
        var added = plan.AddItem("plan-a", PlannedWindow.Of(new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2)), tag: PlanningTag.Of("plan-a"));
        Assert.True(added.IsSuccess);
        _plans.Current = plan;
        _work.File("plan-a", Task("a-1", 8));

        var scheduled = await KeepUpAsync();

        Assert.Empty(scheduled);
        Assert.Equal(0, _plans.Saves);
    }

    [Fact]
    public async Task AnItemWhoseEndWasPinned_IsNeverWritten()
    {
        var id = Imported("plan-a", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9));
        var plan = _plans.Current;
        Assert.True(plan.PinEnd(id, new DateOnly(2026, 10, 30)).IsSuccess);
        _plans.Current = plan;
        _work.File("plan-a", Task("a-1", 4, RoadmapProgress.InProgress, started: new DateOnly(2026, 10, 6)), Task("a-2", 8));

        var scheduled = await KeepUpAsync();

        Assert.Empty(scheduled);
        Assert.Equal(new DateOnly(2026, 10, 30), Stored(id).Window.End);
        Assert.Equal(0, _plans.Saves);
    }

    [Fact]
    public async Task AFinishedItem_IsNeverWritten()
    {
        var id = Imported("plan-a", new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2));
        _work.File("plan-a", Task("a-1", 8, RoadmapProgress.Done, started: new DateOnly(2026, 9, 29), completed: new DateOnly(2026, 10, 7)));

        var scheduled = await KeepUpAsync();

        Assert.Empty(scheduled);
        Assert.Equal(PlannedWindow.Of(new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2)), Stored(id).Window);
        Assert.Equal(0, _plans.Saves);
    }

    [Fact]
    public async Task APlanWithNothingSizedByEffort_IsNotGatheredAtAll()
    {
        Imported("plan-a", new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2), ImportPlacement.DueDate);

        await KeepUpAsync();

        Assert.Equal(0, _work.Reads);
        Assert.Equal(0, _velocity.Reads);
    }

    [Fact]
    public async Task ThePlanIsPulledBeforeItIsRead()
    {
        List<string> order = [];
        _plans.Loaded = () => order.Add("load");
        Imported("plan-a", new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2));

        await KeepUpAsync(catchUp: new RecordingCatchUp(order));

        Assert.Equal(["catch-up", "load"], order);
    }

    // --- Beside the plan's other writers -------------------------------------

    /// <summary>
    /// A task write starts keep-up, and the import that wrote the task writes the plan
    /// next. Keep-up holds the plan it loaded while it gathers; an import that lands in
    /// that gap must not be saved over when keep-up saves (review, slice 4 round 1).
    /// </summary>
    [Fact]
    public async Task AnImportThatArrivesWhileKeepUpGathers_LandsBesideTheKeptUpWindow()
    {
        var id = Imported("plan-a", new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2));
        _work.File("plan-a", Task("a-1", 8));
        var clock = new FakeTimeProvider();
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        clock.SetUtcNow(new DateTimeOffset(Today, new TimeOnly(9, 0), TimeSpan.Zero));
        var gathering = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.HoldNextGather = gathering.Task;

        var keepUp = KeepUpAsync();
        var import = new ImportPlanItemsCommandHandler(_plans, _velocity, _work, clock, _gate).Handle(
            new ImportPlanItemsCommand([new PlanImportEntryDto("Plan B", "plan-b")]),
            TestContext.Current.CancellationToken);
        gathering.SetResult();
        await System.Threading.Tasks.Task.WhenAll(keepUp, import);

        Assert.True((await import).IsSuccess);
        Assert.Equal(PlannedWindow.Of(Today, new DateOnly(2026, 10, 16)), Stored(id).Window);
        Assert.Single(_plans.Current.Items, item => item.Tag == PlanningTag.Of("plan-b"));
    }

    // --- Fakes ---------------------------------------------------------------

    private sealed class RecordingCatchUp(List<string> order) : IRoadmapCatchUp
    {
        public Task CatchUpAsync(CancellationToken cancellationToken = default)
        {
            order.Add("catch-up");
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    /// <summary>Work filed by tag, gathered the way the roadmap gathers it, counting its
    /// reads.</summary>
    private sealed class TaggedWork : IRoadmapItemRollup
    {
        private readonly Dictionary<string, RoadmapGatheredLink[]> _byTag = new(StringComparer.Ordinal);

        public int Reads { get; private set; }

        public void File(string tag, params RoadmapGatheredLink[] tasks) => _byTag[PlanningTag.Of(tag).Value] = tasks;

        public Task<RoadmapItemRollupDto> GatherAsync(RoadmapItemDto item, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(Of(item));

        /// <summary>When set, the next gather waits for it before it answers — a backlog
        /// read slow enough for another writer to arrive meanwhile.</summary>
        public Task? HoldNextGather { get; set; }

        public async Task<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>> GatherPlanAsync(
            RoadmapPlanDto plan,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            var hold = HoldNextGather;
            HoldNextGather = null;
            if (hold is not null) await hold;
            return plan.Items.ToDictionary(item => item.Id, Of);
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

        public Action? Loaded { get; set; }

        public RoadmapPlan Current
        {
            get => Copy(_stored);
            set => _stored = Copy(value);
        }

        public Task<RoadmapPlan> LoadAsync(CancellationToken cancellationToken = default)
        {
            Loaded?.Invoke();
            return System.Threading.Tasks.Task.FromResult(Copy(_stored));
        }

        public Task SaveAsync(RoadmapPlan plan, CancellationToken cancellationToken = default)
        {
            _stored = Copy(plan);
            Saves++;
            return System.Threading.Tasks.Task.CompletedTask;
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
