using System.Net;
using System.Text;
using System.Text.Json;

using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Sqlite.Roadmap;
using Backlog.Infrastructure.Sync;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Extensions;
using Backlog.Modules.Roadmap.UI;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Extensions;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Roadmap;

using Bunit;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Two paired desktops sharing a roadmap through the task feed (local ADR 0018,
/// Verification 1, 2, 4 and 5).
/// <para>
/// Each device is composed the way a host composes itself — the real Roadmap module,
/// the real plan row in its own <c>backlog.db</c>, the real pace file, the real
/// merge and session — and the two exchange through one replica. The replica is a
/// double at the HTTP edge that keeps what the deployed service keeps: one document
/// per id, a push that is not a later version refused, and a feed in write order. The
/// service's own tests say it does exactly that; what these say is that two desktops
/// built on it end holding the same plan and pace.
/// </para>
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class RoadmapSyncBetweenDevicesTests : IDisposable
{
    private static readonly Guid Owner = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "roadmap-sync-devices-" + Guid.NewGuid().ToString("N"));

    private readonly FakeTaskReplica _replica = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>ADR 0018 Verification 1: A saves a plan and syncs; B syncs and loads
    /// the same items and milestones, and its <c>roadmap_plan.updated_at</c> equals
    /// A's.</summary>
    [Fact]
    public async Task Two_paired_devices_share_a_plan()
    {
        using var a = Device("a");
        using var b = Device("b");

        await a.AddItemAsync("Ship sync");
        await a.Planning(planning => planning.AddMilestoneAsync("1.0", new DateOnly(2026, 10, 31)));
        Assert.True((await a.SyncAsync()).IsSuccess);

        var pulled = await b.SyncAsync();
        Assert.True(pulled.IsSuccess);

        var plan = await b.Planning(planning => planning.GetPlanAsync());
        Assert.Equal("Ship sync", Assert.Single(plan.Items).Title);
        Assert.Equal("1.0", Assert.Single(plan.Milestones).Title);

        var sent = await a.PlanCopyAsync();
        var held = await b.PlanCopyAsync();
        Assert.Equal(sent!.UpdatedAt, held!.UpdatedAt);
        Assert.Equal(sent.Content, held.Content);
    }

    /// <summary>ADR 0018 Verification 2: both save before either syncs; after both
    /// sync, both hold the later plan. The older push is refused — a short accepted
    /// count — and an older copy pulled writes nothing.</summary>
    [Fact]
    public async Task The_newer_save_wins_on_both_devices()
    {
        using var a = Device("a");
        using var b = Device("b");

        await a.AddItemAsync("Saved first, on A");
        await b.AddItemAsync("Saved later, on B");
        var later = (await b.PlanCopyAsync())!;
        Assert.True(later.UpdatedAt > (await a.PlanCopyAsync())!.UpdatedAt);

        Assert.True((await b.SyncAsync()).IsSuccess);

        var refused = await a.PushAsync();
        Assert.Equal(1, refused.Refused);

        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);

        Assert.Equal(later, await a.PlanCopyAsync());
        Assert.Equal(later, await b.PlanCopyAsync());
        Assert.Equal("Saved later, on B", Assert.Single((await a.Planning(planning => planning.GetPlanAsync())).Items).Title);
    }

    [Fact]
    public async Task An_older_copy_pulled_writes_nothing()
    {
        using var a = Device("a");
        using var b = Device("b");

        await a.AddItemAsync("Saved first, on A");
        Assert.True((await a.SyncAsync()).IsSuccess);
        await b.AddItemAsync("Saved later, on B");
        var mine = await b.PlanCopyAsync();

        var pulled = await b.PullAsync();

        Assert.Equal(0, pulled.Applied);
        Assert.Equal(mine, await b.PlanCopyAsync());
    }

    /// <summary>ADR 0018 Verification 4: A changes the typed pace, the source and one
    /// repository's pair and syncs; B then reads the same values and places by the
    /// same paces. A pace change on A and a plan edit on B, made in the same interval,
    /// both survive.</summary>
    [Fact]
    public async Task The_pace_follows_and_survives_a_plan_edit_made_elsewhere()
    {
        using var a = Device("a");
        using var b = Device("b");

        Assert.Null(a.Pace.Set(12m));
        Assert.Null(a.Pace.Choose(PaceSource.LastFourWeeks));
        Assert.Null(a.Pace.Set(3.5m, "backlog"));
        await b.AddItemAsync("Edited on B meanwhile");

        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);
        Assert.True((await a.SyncAsync()).IsSuccess);

        Assert.Equal(12m, b.Pace.StoryPointsPerWeek);
        Assert.Equal(PaceSource.LastFourWeeks, b.Pace.Source);
        Assert.Equal(3.5m, b.Pace.StoryPointsPerWeekFor("backlog"));
        Assert.Equal(
            await a.Velocity(velocity => velocity.GetStoryPointsPerWeekAsync(["backlog"])),
            await b.Velocity(velocity => velocity.GetStoryPointsPerWeekAsync(["backlog"])));
        Assert.Equal(
            await a.Velocity(velocity => velocity.GetStoryPointsPerWeekAsync([])),
            await b.Velocity(velocity => velocity.GetStoryPointsPerWeekAsync([])));

        // And B's plan edit reached A, rather than being lost to A's pace change.
        Assert.Equal("Edited on B meanwhile", Assert.Single((await a.Planning(planning => planning.GetPlanAsync())).Items).Title);
    }

    /// <summary>
    /// ADR 0018 Verification 4, driven the way a person drives it: B edits the plan, and
    /// afterwards — so a plan stamp from A would be the newer one — A types a pace into
    /// its open roadmap. A pace change writes the pace's document and nothing else, so
    /// after both sync B's edit stands on both; and B, whose roadmap is open when the
    /// pace arrives, redraws the bar still sized by its effort exactly as A draws it.
    /// </summary>
    [Fact]
    public async Task A_pace_typed_on_the_roadmap_leaves_a_plan_edit_made_elsewhere_and_redraws_both()
    {
        using var a = Device("a");
        using var b = Device("b");

        // One plan sized by its effort: 14 points gathered by its tag, nobody started.
        await a.EntryAsync("# Ship the sync\n`task` `!ready` `+ship` `effort:14`\n");
        var imported = await a.Planning(planning => planning.ImportPlanItemsAsync(
            [new PlanImportEntryDto("Ship", "ship", RepositoryAliases: ["backlog"])],
            cancellationToken: Cancellation));
        Assert.True(imported.IsSuccess);
        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);

        var ship = Assert.Single((await b.Planning(planning => planning.GetPlanAsync())).Items);
        var onB = b.Roadmap();
        // 14 points at the untouched 7 a week: two working weeks.
        onB.WaitForAssertion(() => Assert.Equal(Window(ship.Start, 14, 7m), DrawnWindow(onB, ship.Id)));

        await b.AddItemAsync("Edited on B meanwhile");

        var onA = a.Roadmap();
        onA.WaitForElement("[data-testid='roadmap-pace-manual'] input").Change("14");
        onA.WaitForAssertion(() => Assert.Equal(Window(ship.Start, 14, 14m), DrawnWindow(onA, ship.Id)));

        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);
        Assert.True((await a.SyncAsync()).IsSuccess);

        // B's edit survived on both: A's pace change sent no plan to win over it.
        Assert.Equal(await b.PlanCopyAsync(), await a.PlanCopyAsync());
        Assert.Contains(
            (await a.Planning(planning => planning.GetPlanAsync())).Items,
            item => item.Title == "Edited on B meanwhile");

        // And B draws what A draws, from the pace alone.
        Assert.Equal(14m, b.Pace.StoryPointsPerWeek);
        onB.WaitForAssertion(() => Assert.Equal(Window(ship.Start, 14, 14m), DrawnWindow(onB, ship.Id)));
        Assert.Equal(ship.End, Assert.Single(
            (await b.Planning(planning => planning.GetPlanAsync())).Items,
            item => item.Id == ship.Id).End);
    }

    /// <summary>
    /// ADR 0019 Verification 7: A edits the working week and syncs; B pulls, its
    /// <c>working-hours.json</c> holds A's week, it draws the same bars as A, and the
    /// working week its dashboard reads outlines the new hours.
    /// </summary>
    [Fact]
    public async Task The_working_week_follows_and_both_devices_draw_the_same_bars()
    {
        using var a = Device("a");
        using var b = Device("b");

        await a.EntryAsync("# Ship the sync\n`task` `!ready` `+ship` `effort:14`\n");
        var imported = await a.Planning(planning => planning.ImportPlanItemsAsync(
            [new PlanImportEntryDto("Ship", "ship", RepositoryAliases: ["backlog"])],
            cancellationToken: Cancellation));
        Assert.True(imported.IsSuccess);
        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);

        var ship = Assert.Single((await b.Planning(planning => planning.GetPlanAsync())).Items);
        var onA = a.Roadmap();
        var onB = b.Roadmap();

        // A keeps a pace of its own, so it has a pace document to carry the week: a
        // device that never set one sends nothing (ADR 0018 §3, ADR 0019 §3).
        Assert.Null(a.Pace.Set(14m));

        // A works Monday mornings only, 08:00 to 12:00.
        var morning = (Start: new TimeOnly(8, 0), End: new TimeOnly(12, 0));
        Assert.Null(a.Week.SetDay(DayOfWeek.Monday, true, morning.Start, morning.End));
        foreach (var day in new[] { DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            Assert.Null(a.Week.SetDay(day, false, morning.Start, morning.End));
        }

        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);

        Assert.True(WorkingHoursSettingsStore.SameWeek(a.Week.Current, b.Week.Current));
        Assert.True(WorkingHoursSettingsStore.SameWeek(
            a.Week.Current, new WorkingHoursSettingsStore(b.WeekPath).Current));

        // The week the dashboard's grid outlines on B is the new one.
        var dashboardWeek = b.Service<IWorkingHoursSettings>().Current;
        Assert.True(dashboardWeek.Covers(DayOfWeek.Monday, 8));
        Assert.False(dashboardWeek.Covers(DayOfWeek.Monday, 14));
        Assert.False(dashboardWeek.Covers(DayOfWeek.Wednesday, 10));

        // The bar is counted in A's week and at A's pace on both: 14 points at 14 a week
        // is one of A's weeks — a single Monday — which the default week would not draw.
        Assert.Equal(14m, b.Pace.StoryPointsPerWeek);
        var expected = Window(ship.Start, 14, 14m, a.Week.Current);
        Assert.NotEqual(Window(ship.Start, 14, 14m), expected);
        onA.WaitForAssertion(() => Assert.Equal(expected, DrawnWindow(onA, ship.Id)));
        onB.WaitForAssertion(() => Assert.Equal(expected, DrawnWindow(onB, ship.Id)));

        // And A, having pulled nothing newer, still holds its own week at its own stamp.
        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True(WorkingHoursSettingsStore.SameWeek(a.Week.Current, b.Week.Current));
    }

    /// <summary>The window an item sized by its effort draws: its first worked day and
    /// how many calendar days the hours it needs run to, in the default week unless
    /// another is given.</summary>
    private static (DateOnly Start, int Days) Window(DateOnly start, int effort, decimal pace, WorkingHours? week = null)
    {
        var counted = week ?? WorkingHours.Default;
        var from = EffortWindow.FirstWorkedDay(start, counted);
        return (from, EffortWindow.EndFrom(from, effort, pace, counted).DayNumber - from.DayNumber + 1);
    }

    /// <summary>Where a device's chart draws an item: its first day and how many days
    /// its bars cover.</summary>
    private static (DateOnly Start, int Days) DrawnWindow(IRenderedComponent<RoadmapBand> band, Guid itemId)
    {
        var bars = band.FindComponent<RoadmapTimeline>().Instance.Bars
            .Where(bar => RoadmapPlanView.NodeIdOf(bar.Id) == itemId)
            .ToList();
        Assert.NotEmpty(bars);

        var start = bars.Min(bar => bar.Start);
        return (start, bars.Max(bar => bar.End).DayNumber - start.DayNumber + 1);
    }

    /// <summary>ADR 0018 Verification 5: B has no plan row and no velocity file, pairs
    /// and syncs. It pushes nothing, and afterwards holds A's plan and pace; A still
    /// holds its own.</summary>
    [Fact]
    public async Task A_device_with_nothing_saved_never_overwrites()
    {
        using var a = Device("a");
        using var b = Device("b");

        await a.AddItemAsync("A's plan");
        Assert.Null(a.Pace.Set(9m));
        Assert.True((await a.SyncAsync()).IsSuccess);
        var aPlan = await a.PlanCopyAsync();

        var synced = await b.SyncAsync();

        Assert.True(synced.IsSuccess);
        Assert.Equal(0, synced.Value.Pushed);
        Assert.Equal(0, _replica.PushesFrom(b.DeviceId));
        Assert.Equal(aPlan, await b.PlanCopyAsync());
        Assert.Equal(9m, b.Pace.StoryPointsPerWeek);

        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.Equal(aPlan, await a.PlanCopyAsync());
        Assert.Equal(9m, a.Pace.StoryPointsPerWeek);
    }

    private DesktopDevice Device(string name) =>
        new(Path.Combine(_root, name), _replica, Guid.NewGuid());

    /// <summary>
    /// One desktop: its own storage root, pace file and sync state, composed through
    /// the same registrations the hosts call, with a session over the shared replica.
    /// </summary>
    private sealed class DesktopDevice : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly HttpClient _http;
        private readonly TaskSyncSession _session;

        public DesktopDevice(string root, FakeTaskReplica replica, Guid deviceId)
        {
            DeviceId = deviceId;

            var workspace = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
            WeekPath = Path.Combine(root, "velocity", "working-hours.json");
            Week = new WorkingHoursSettingsStore(WeekPath);
            Pace = new PlanningVelocitySettingsStore(Path.Combine(root, "velocity", "planning-velocity.json"), time: null, Week);

            var services = new ServiceCollection();
            services.AddSingleton(workspace);
            services.AddSingleton<ITaskRepository>(_ => new RootedSqliteTaskRepository(() => workspace.RootDirectory));
            services.AddSingleton(_ => new RootedSqliteRoadmapPlanRepository(() => workspace.RootDirectory));
            services.AddSingleton<IRoadmapPlanRepository>(sp => sp.GetRequiredService<RootedSqliteRoadmapPlanRepository>());
            services.AddSingleton<IRoadmapReplicaStore>(sp => sp.GetRequiredService<RootedSqliteRoadmapPlanRepository>());
            // Both devices configure the one repository the shared plan is filed under:
            // each draws it in that repository's band rather than under no repository.
            var repositories = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
            var (configured, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
            Assert.Empty(errors);
            Assert.Null(repositories.SetRepositories(configured));
            services.AddSingleton(repositories);
            services.AddSingleton(Pace);
            services.AddSingleton(Week);
            services.AddSingleton<IWorkingHoursSettings>(Week);
            services.AddTasksAdapters();
            services.AddTasksModule();
            services.AddRoadmapModule();
            services.AddRoadmapCrossContextAdapters();

            _provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });

            var tasks = _provider.GetRequiredService<ITaskRepository>();
            var roadmap = _provider.GetRequiredService<IRoadmapReplication>();

            _http = new HttpClient(replica.For(deviceId)) { BaseAddress = new Uri("https://sync.test") };
            _session = new TaskSyncSession(
                new TaskSyncClient(_http),
                new TaskReplicaMerge(tasks, roadmap: roadmap),
                tasks,
                new FileTaskSyncStateStore(Path.Combine(root, "sync", "task-sync-state.json")),
                new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, deviceId, $"PC {deviceId:N}", "credential")),
                new FakeTimeProvider(DateTimeOffset.UtcNow),
                roadmap: roadmap);
        }

        public Guid DeviceId { get; }

        public PlanningVelocitySettingsStore Pace { get; }

        /// <summary>The device's working week, carried with the pace.</summary>
        public WorkingHoursSettingsStore Week { get; }

        public string WeekPath { get; }

        public T Service<T>() where T : notnull => _provider.GetRequiredService<T>();

        public async Task<T> Planning<T>(Func<IRoadmapPlanning, Task<T>> use)
        {
            using var scope = _provider.CreateScope();
            return await use(scope.ServiceProvider.GetRequiredService<IRoadmapPlanning>());
        }

        public async Task<T> Velocity<T>(Func<IPlanningVelocity, Task<T>> use)
        {
            using var scope = _provider.CreateScope();
            return await use(scope.ServiceProvider.GetRequiredService<IPlanningVelocity>());
        }

        /// <summary>
        /// The roadmap open on this device: the real band, resolving what it injects
        /// from this device's own composition, so what it draws is what this device
        /// holds and what it writes is written here. Disposed with the device.
        /// </summary>
        public IRenderedComponent<RoadmapBand> Roadmap()
        {
            var scope = _provider.CreateScope();
            var ui = new BunitContext();
            ui.JSInterop.Mode = JSRuntimeMode.Loose;
            ui.Services.AddFallbackServiceProvider(scope.ServiceProvider);
            _open.Add(ui);
            _open.Add(scope);

            var band = ui.Render<RoadmapBand>();
            band.WaitForElement("[data-testid='roadmap-timeline']");
            return band;
        }

        private readonly List<IDisposable> _open = [];

        public async Task EntryAsync(string text)
        {
            using var scope = _provider.CreateScope();
            var saved = await scope.ServiceProvider.GetRequiredService<ITaskItems>()
                .SaveFromTextAsync(null, text, 0, cancellationToken: Cancellation);
            Assert.True(saved.IsSuccess);
        }

        public async Task AddItemAsync(string title)
        {
            var added = await Planning(planning => planning.AddItemAsync(
                title, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9)));
            Assert.True(added.IsSuccess);
        }

        public Task<RoadmapReplicaCopyDto?> PlanCopyAsync() =>
            _provider.GetRequiredService<IRoadmapReplication>().ReadAsync(RoadmapReplicaDocument.Plan, Cancellation);

        public Task<Result<TaskSyncSummary>> SyncAsync() => _session.SyncAsync(Cancellation);

        public async Task<TaskSyncSummary> PushAsync()
        {
            var pushed = await _session.PushAsync(Cancellation);
            Assert.True(pushed.IsSuccess);
            return pushed.Value;
        }

        public async Task<TaskSyncSummary> PullAsync()
        {
            var pulled = await _session.PullAsync(Cancellation);
            Assert.True(pulled.IsSuccess);
            return pulled.Value;
        }

        public void Dispose()
        {
            // The open roadmaps first: each holds a scope of the provider below.
            foreach (var open in Enumerable.Reverse(_open)) open.Dispose();
            _http.Dispose();
            _provider.Dispose();
        }
    }
}
