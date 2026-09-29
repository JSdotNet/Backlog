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
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Extensions;
using Backlog.SharedKernel.Results;

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
public sealed class RoadmapSyncBetweenDevicesTests : IDisposable
{
    private static readonly Guid Owner = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "roadmap-sync-devices-" + Guid.NewGuid().ToString("N"));

    private readonly FakeReplica _replica = new();

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

        public DesktopDevice(string root, FakeReplica replica, Guid deviceId)
        {
            DeviceId = deviceId;

            var workspace = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
            Pace = new PlanningVelocitySettingsStore(Path.Combine(root, "velocity", "planning-velocity.json"));

            var services = new ServiceCollection();
            services.AddSingleton(workspace);
            services.AddSingleton<ITaskRepository>(_ => new RootedSqliteTaskRepository(() => workspace.RootDirectory));
            services.AddSingleton(_ => new RootedSqliteRoadmapPlanRepository(() => workspace.RootDirectory));
            services.AddSingleton<IRoadmapPlanRepository>(sp => sp.GetRequiredService<RootedSqliteRoadmapPlanRepository>());
            services.AddSingleton<IRoadmapReplicaStore>(sp => sp.GetRequiredService<RootedSqliteRoadmapPlanRepository>());
            services.AddSingleton(new GitHubSettingsStore(Path.Combine(root, "github", "github.json")));
            services.AddSingleton(Pace);
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
            _http.Dispose();
            _provider.Dispose();
        }
    }

    /// <summary>
    /// The task replica at the HTTP edge, holding what the deployed one holds: one
    /// document per id for one owner, a push taken only when it is a later version —
    /// the rule <c>TaskChangePrecedence</c> keeps — and a feed in the order documents
    /// were written, the cursor being the position in it.
    /// </summary>
    private sealed class FakeReplica
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private readonly Lock _gate = new();
        private readonly Dictionary<Guid, (TaskChange Change, Guid DeviceId, long Sequence)> _documents = [];
        private readonly Dictionary<Guid, int> _pushes = [];
        private long _sequence;

        public HttpMessageHandler For(Guid deviceId) => new Handler(this, deviceId);

        public int PushesFrom(Guid deviceId)
        {
            lock (_gate) return _pushes.GetValueOrDefault(deviceId);
        }

        private string Push(Guid deviceId, string body)
        {
            var request = JsonSerializer.Deserialize<PushTasksRequest>(body, Web)!;
            var accepted = 0;

            lock (_gate)
            {
                _pushes[deviceId] = _pushes.GetValueOrDefault(deviceId) + 1;

                foreach (var change in request.Tasks)
                {
                    if (_documents.TryGetValue(change.Id, out var held) && !Supersedes(change, held.Change)) continue;

                    _documents[change.Id] = (change, deviceId, ++_sequence);
                    accepted++;
                }
            }

            return JsonSerializer.Serialize(new PushTasksResponse(accepted), Web);
        }

        private string Pull(string? since)
        {
            var after = long.TryParse(since, out var position) ? position : 0;

            lock (_gate)
            {
                var page = _documents.Values
                    .Where(document => document.Sequence > after)
                    .OrderBy(document => document.Sequence)
                    .Select(document => new TaskChangeRecord(document.Change, document.DeviceId, document.Sequence))
                    .ToList();

                return JsonSerializer.Serialize(
                    new PullTasksResponse(page, _sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), HasMore: false),
                    Web);
            }
        }

        private static bool Supersedes(TaskChange inbound, TaskChange stored) =>
            inbound.UpdatedAt != stored.UpdatedAt
                ? inbound.UpdatedAt > stored.UpdatedAt
                : inbound.DeletedAt is not null && stored.DeletedAt is null;

        private sealed class Handler(FakeReplica replica, Guid deviceId) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var body = request.Method == HttpMethod.Post
                    ? replica.Push(deviceId, await request.Content!.ReadAsStringAsync(cancellationToken))
                    : replica.Pull(System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["since"]);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            }
        }
    }
}
