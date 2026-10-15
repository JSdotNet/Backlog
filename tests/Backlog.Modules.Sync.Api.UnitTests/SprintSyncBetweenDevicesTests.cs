using System.Net.Http.Json;
using System.Text.Json;

using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Sqlite.Progress;
using Backlog.Infrastructure.Sync;
using Backlog.Modules.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Extensions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// Two paired desktops sharing their sprint series through this service, end to end:
/// each composed the way a host composes itself — the real Progress module over the
/// real series rows in its own <c>backlog.db</c>, the real merge and session — and the
/// two exchanging through the real sync API. A change on one device appears on the other
/// after a sync, and the newer write wins per series, as the roadmap's plan does (local
/// ADR 0018).
/// </summary>
public sealed class SprintSyncBetweenDevicesTests : IDisposable
{
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DateTimeOffset Morning = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly SyncServiceFactory _service = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sprint-sync-" + Guid.NewGuid().ToString("N"));
    private readonly List<Desktop> _desktops = [];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var desktop in _desktops) desktop.Dispose();
        _service.Dispose();

        // The pooled connections may still hold the files; what cannot be deleted now is
        // left in the temp folder, as PhoneTaskEditSyncTests leaves its own.
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>A sets up a series, starts a sprint and fills it — membership, a linked
    /// plan, a goal mark, a note and an open question — and syncs. B syncs and holds the
    /// same series, at the same stamp, reading the same through its own port.</summary>
    [Fact]
    public async Task A_series_set_up_on_one_desktop_appears_on_the_other()
    {
        var (a, b) = await PairAsync();
        var task = Guid.NewGuid();

        var series = await a.CreateAsync("Platform");
        await a.Plan(planning => planning.StartSprintAsync(series, Monday, "Ship sync", Cancellation));
        await a.Plan(planning => planning.AddTaskAsync(series, 1, task, Cancellation));
        await a.Plan(planning => planning.LinkPlanAsync(series, 1, "+progress-sprints", Cancellation));
        await a.Plan(planning => planning.MarkGoalAsync(series, 1, task, true, Cancellation));
        await a.Plan(planning => planning.SetNoteAsync(series, 1, task, "Waiting on review", Cancellation));
        await a.Plan(planning => planning.SetOpenQuestionAsync(series, 1, task, true, Cancellation));

        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.Empty(await b.ListAsync());

        var pulled = await b.SyncAsync();

        Assert.True(pulled.IsSuccess);
        Assert.Equal(1, pulled.Value.Applied);
        Assert.Equal(Json(await a.GetAsync(series)), Json(await b.GetAsync(series)));
        Assert.Equal(await a.CopyAsync(series), await b.CopyAsync(series));

        var item = Assert.Single(Assert.Single((await b.GetAsync(series)).Sprints).Items);
        Assert.True(item.IsGoal);
        Assert.Equal("Waiting on review", item.Note);
        Assert.True(item.OpenQuestion);
    }

    /// <summary>Both edit one series before either syncs; after both sync, both hold the
    /// later edit. The older push is refused, and the older copy pulled writes
    /// nothing.</summary>
    [Fact]
    public async Task The_newer_write_wins_per_series_on_both_desktops()
    {
        var (a, b) = await PairAsync();
        var task = Guid.NewGuid();
        var series = await a.CreateAsync("Platform");
        await a.Plan(planning => planning.StartSprintAsync(series, Monday, "Ship sync", Cancellation));
        await a.Plan(planning => planning.AddTaskAsync(series, 1, task, Cancellation));
        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);

        await a.Plan(planning => planning.SetNoteAsync(series, 1, task, "Written first, on A", Cancellation));
        await b.Plan(planning => planning.SetNoteAsync(series, 1, task, "Written later, on B", Cancellation));
        var later = await b.CopyAsync(series);
        Assert.True(later!.UpdatedAt > (await a.CopyAsync(series))!.UpdatedAt);

        Assert.True((await b.SyncAsync()).IsSuccess);
        var refused = await a.PushAsync();
        Assert.Equal(1, refused.Refused);

        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);

        Assert.Equal(later, await a.CopyAsync(series));
        Assert.Equal(later, await b.CopyAsync(series));
        Assert.Equal("Written later, on B", Assert.Single(Assert.Single((await a.GetAsync(series)).Sprints).Items).Note);
    }

    /// <summary>Each series is its own document: one edited on A and another edited on B
    /// in the same interval both survive on both.</summary>
    [Fact]
    public async Task Edits_to_two_series_on_two_desktops_both_survive()
    {
        var (a, b) = await PairAsync();
        var platform = await a.CreateAsync("Platform", "backlog");
        var docs = await a.CreateAsync("Docs", "docs");
        await a.Plan(planning => planning.StartSprintAsync(platform, Monday, "Ship sync", Cancellation));
        await a.Plan(planning => planning.StartSprintAsync(docs, Monday, "Write it down", Cancellation));
        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);

        await a.Plan(planning => planning.SetSprintGoalAsync(platform, 1, "Goal set on A", Cancellation));
        await b.Plan(planning => planning.SetSprintGoalAsync(docs, 1, "Goal set on B", Cancellation));

        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);
        Assert.True((await a.SyncAsync()).IsSuccess);

        foreach (var desktop in new[] { a, b })
        {
            Assert.Equal("Goal set on A", (await desktop.GetAsync(platform)).Sprints[0].Goal);
            Assert.Equal("Goal set on B", (await desktop.GetAsync(docs)).Sprints[0].Goal);
        }
    }

    /// <summary>A desktop that never set up a series pushes nothing, so it never replaces
    /// another desktop's series with nothing; and a series it only pulled is never taken
    /// back from it as a change of its own — the one offer it makes is refused as the
    /// echo it is.</summary>
    [Fact]
    public async Task A_desktop_with_no_series_never_overwrites_and_never_echoes()
    {
        var (a, b) = await PairAsync();
        var series = await a.CreateAsync("Platform");
        Assert.True((await a.SyncAsync()).IsSuccess);
        var held = await a.CopyAsync(series);

        var synced = await b.SyncAsync();
        Assert.Equal(0, synced.Value.Pushed);

        var again = await b.PushAsync();
        Assert.Equal(0, again.Pushed);
        Assert.Equal(held, await b.CopyAsync(series));
        Assert.Equal(held, await a.CopyAsync(series));
    }

    /// <summary>A and B each set up a series for `backlog` while offline, A an hour before
    /// B, and B records a sprint in its own. Whichever syncs first, once both have synced
    /// both desktops keep `backlog` in A's series and hold B's switched off with the line
    /// saying why, its sprint kept — and both hold that series alike
    /// (<c>.devbook/domain/progress/requirements.md#requirement-a-repository-two-desktops-gave-to-two-series-stays-in-the-first</c>).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Two_desktops_that_set_up_a_series_for_one_repository_offline_keep_it_in_the_first(bool laterSyncsFirst)
    {
        var (a, b) = await PairAsync(bNow: Morning.AddHours(1));
        var core = await a.CreateAsync("Core");
        var main = await b.CreateAsync("Main");
        await b.Plan(planning => planning.StartSprintAsync(main, Monday, "Recorded on B", Cancellation));

        var order = laterSyncsFirst ? new[] { b, a } : [a, b];
        for (var round = 0; round < 3; round++)
        {
            foreach (var desktop in order) Assert.True((await desktop.SyncAsync()).IsSuccess);
        }

        foreach (var desktop in new[] { a, b })
        {
            Assert.Equal(core, (await desktop.FindAsync("backlog"))?.Id);
            Assert.True((await desktop.GetAsync(core)).IsOn);

            var switchedOff = await desktop.GetAsync(main);
            Assert.False(switchedOff.IsOn);
            Assert.Equal("Switched off: backlog already works to \"Core\", which was set up first.", switchedOff.OffReason);
            Assert.Equal(["backlog"], switchedOff.Repositories);
            Assert.Equal("Recorded on B", Assert.Single(switchedOff.Sprints).Goal);
        }

        Assert.Equal(Json(await a.GetAsync(main)), Json(await b.GetAsync(main)));
        Assert.Equal(await a.CopyAsync(main), await b.CopyAsync(main));

        var refused = await b.TryPlan(planning => planning.SwitchAsync(main, true, Cancellation));
        Assert.Equal("progress.repository_in_other_series", refused.Error.Code);
    }

    private static string Json(SprintSeriesDto series) => JsonSerializer.Serialize(series, Web);

    private async Task<(Desktop A, Desktop B)> PairAsync(DateTimeOffset? aNow = null, DateTimeOffset? bNow = null)
    {
        var aHttp = _service.CreateClient();
        var registration = await aHttp.RegisterDevice("Study desktop");
        aHttp.Bearing(await aHttp.DeviceToken(registration));

        var code = await aHttp.MintPairingCode();
        var bHttp = _service.CreateClient();
        var paired = await bHttp.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, "Laptop"),
            Cancellation);
        var laptop = (await paired.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!;
        bHttp.Bearing(await bHttp.DeviceToken(laptop));

        var a = new Desktop(Path.Combine(_root, "a"), aHttp,
            new DeviceCredential(registration.OwnerId, registration.DeviceId, "Study desktop", registration.Credential),
            aNow ?? Morning);
        var b = new Desktop(Path.Combine(_root, "b"), bHttp,
            new DeviceCredential(laptop.OwnerId, laptop.DeviceId, "Laptop", laptop.Credential),
            bNow ?? Morning);
        _desktops.Add(a);
        _desktops.Add(b);

        return (a, b);
    }

    /// <summary>One desktop: its own <c>backlog.db</c> and sync state, the Progress
    /// module composed through the registrations a host calls, and a session over the
    /// shared service.</summary>
    private sealed class Desktop : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly HttpClient _http;
        private readonly TaskSyncSession _session;

        public Desktop(string root, HttpClient http, DeviceCredential credential, DateTimeOffset now)
        {
            Directory.CreateDirectory(root);
            _http = http;

            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(now));
            services.AddSingleton<IAppFeatureSettings, ProgressOn>();
            services.AddSingleton<ISprintTaskSource, NoTasks>();
            services.AddSingleton(_ => new RootedSqliteSprintSeriesRepository(() => root));
            services.AddSingleton<ISprintSeriesRepository>(sp => sp.GetRequiredService<RootedSqliteSprintSeriesRepository>());
            services.AddSingleton<ISprintSeriesReplicaStore>(sp => sp.GetRequiredService<RootedSqliteSprintSeriesRepository>());
            services.AddProgressModule();

            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

            var sprints = _provider.GetRequiredService<ISprintSeriesReplication>();
            var tasks = new SqliteTaskRepository(root);

            _session = new TaskSyncSession(
                new TaskSyncClient(http),
                new TaskReplicaMerge(tasks, sprints: sprints),
                tasks,
                new FileTaskSyncStateStore(Path.Combine(root, "sync-state.json")),
                new InMemoryDeviceCredentialStore(credential),
                TimeProvider.System,
                sprints: sprints);
        }

        public async Task<Result<T>> TryPlan<T>(Func<ISprintSeriesPlanning, Task<Result<T>>> use)
        {
            using var scope = _provider.CreateScope();
            return await use(scope.ServiceProvider.GetRequiredService<ISprintSeriesPlanning>());
        }

        public async Task<T> Plan<T>(Func<ISprintSeriesPlanning, Task<Result<T>>> use)
        {
            using var scope = _provider.CreateScope();
            var result = await use(scope.ServiceProvider.GetRequiredService<ISprintSeriesPlanning>());
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
            return result.Value;
        }

        public async Task<Guid> CreateAsync(string name, string repository = "backlog") =>
            (await Plan(planning => planning.CreateSeriesAsync(new CreateSprintSeries([repository], name), Cancellation))).Id;

        public Task<SprintSeriesDto?> FindAsync(string repository) =>
            Plan(planning => planning.FindSeriesOfAsync(repository, Cancellation));

        public Task<SprintSeriesDto> GetAsync(Guid id) => Plan(planning => planning.GetSeriesAsync(id, Cancellation));

        public Task<IReadOnlyList<SprintSeriesDto>> ListAsync() => Plan(planning => planning.ListSeriesAsync(Cancellation));

        public async Task<SprintSeriesReplicaCopyDto?> CopyAsync(Guid id) =>
            (await _provider.GetRequiredService<ISprintSeriesReplication>().ReadAllAsync(Cancellation))
                .SingleOrDefault(copy => copy.Id == id);

        public Task<Result<TaskSyncSummary>> SyncAsync() => _session.SyncAsync(Cancellation);

        public async Task<TaskSyncSummary> PushAsync()
        {
            var pushed = await _session.PushAsync(Cancellation);
            Assert.True(pushed.IsSuccess);
            return pushed.Value;
        }

        public void Dispose()
        {
            _provider.Dispose();
            _http.Dispose();
        }
    }

    private sealed class ProgressOn : IAppFeatureSettings
    {
        public event Action? Changed { add { } remove { } }

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "features.json";

        public bool IsEnabled(string key) => key == ProgressFeatures.Progress;

        public string? SetEnabled(string key, bool enabled) => null;
    }

    private sealed class NoTasks : ISprintTaskSource
    {
        public Task<IReadOnlyList<SprintTaskDto>> ReadAsync(SprintTaskQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SprintTaskDto>>([]);
    }
}
