using System.Net.Http.Json;

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
/// Two paired desktops sharing their goals through this service, end to end — each the
/// real Progress module over real <c>progress_goals</c> rows in its own <c>backlog.db</c>,
/// the real merge and session, and the real sync API between them
/// (<c>.devbook/domain/progress/requirements.md#syncing-goals-and-impediments</c>).
/// </summary>
public sealed class GoalSyncBetweenDevicesTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Due = new(2026, 10, 31);

    private readonly SyncServiceFactory _service = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "goal-sync-" + Guid.NewGuid().ToString("N"));
    private readonly List<Desktop> _desktops = [];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var desktop in _desktops) desktop.Dispose();
        _service.Dispose();

        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Scenario "A goal reaches the other desktop": set on A with its work
    /// attached, it is on B after B's next pull, the same document at the same stamp, and
    /// B's screens hear it without a Refresh.</summary>
    [Fact]
    public async Task A_goal_set_on_one_desktop_appears_on_the_other()
    {
        var (a, b) = await PairAsync();
        var task = Guid.NewGuid();
        var goal = await a.Use(goals => goals.CreateAsync(new SetGoalRequest("Customers get paid faster", "Invoices settle in a day", Due), Cancellation));
        await a.Use(goals => goals.AttachAsync(goal.Id, GoalItemDto.ForPlan("+bank-integration"), cancellationToken: Cancellation));
        await a.Use(goals => goals.AttachAsync(goal.Id, GoalItemDto.ForTask(task), cancellationToken: Cancellation));
        await a.Use(goals => goals.AttachAsync(goal.Id, GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1352), cancellationToken: Cancellation));

        Assert.True((await a.SyncAsync()).IsSuccess);
        var heard = 0;
        b.Changed += () => heard++;

        var pulled = await b.SyncAsync();

        Assert.Equal(1, pulled.Value.Applied);
        var there = await b.Use(goals => goals.GetAsync(goal.Id, Cancellation));
        Assert.Equal(goal.Title, there.Title);
        Assert.Equal(goal.Outcome, there.Outcome);
        Assert.Equal(
            [GoalItemDto.ForPlan("bank-integration"), GoalItemDto.ForTask(task), GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1352)],
            there.Attachments.Select(attachment => attachment.Item));
        Assert.Equal(await a.CopyAsync(goal.Id), await b.CopyAsync(goal.Id));
        Assert.True(heard > 0);
    }

    /// <summary>Both edit one goal before either syncs; once both have synced both hold
    /// the later edit.</summary>
    [Fact]
    public async Task The_newer_change_wins_per_goal_on_both_desktops()
    {
        var (a, b) = await PairAsync();
        var goal = await a.Use(goals => goals.CreateAsync(new SetGoalRequest("Customers get paid faster", null, Due), Cancellation));
        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);

        await a.Use(goals => goals.EditAsync(goal.Id, new SetGoalRequest("Written first, on A", null, Due), Cancellation));
        await b.Use(goals => goals.EditAsync(goal.Id, new SetGoalRequest("Written later, on B", null, Due), Cancellation));
        var later = await b.CopyAsync(goal.Id);

        for (var round = 0; round < 2; round++)
        {
            Assert.True((await b.SyncAsync()).IsSuccess);
            Assert.True((await a.SyncAsync()).IsSuccess);
        }

        Assert.Equal(later, await a.CopyAsync(goal.Id));
        Assert.Equal("Written later, on B", (await a.Use(goals => goals.GetAsync(goal.Id, Cancellation))).Title);
    }

    /// <summary>A deleted goal is deleted on every desktop, and the tombstone both keep is
    /// the same document at the same stamp.</summary>
    [Fact]
    public async Task A_goal_deleted_on_one_desktop_is_deleted_on_the_other()
    {
        var (a, b) = await PairAsync();
        var goal = await a.Use(goals => goals.CreateAsync(new SetGoalRequest("Customers get paid faster", null, Due), Cancellation));
        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);
        Assert.Single(await b.Use(goals => goals.ListAsync(cancellationToken: Cancellation)));

        await a.DeleteAsync(goal.Id);
        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);

        Assert.Empty(await b.Use(goals => goals.ListAsync(cancellationToken: Cancellation)));
        Assert.Equal(await a.CopyAsync(goal.Id), await b.CopyAsync(goal.Id));
    }

    private async Task<(Desktop A, Desktop B)> PairAsync()
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
            new DeviceCredential(registration.OwnerId, registration.DeviceId, "Study desktop", registration.Credential));
        var b = new Desktop(Path.Combine(_root, "b"), bHttp,
            new DeviceCredential(laptop.OwnerId, laptop.DeviceId, "Laptop", laptop.Credential));
        _desktops.Add(a);
        _desktops.Add(b);

        return (a, b);
    }

    /// <summary>One desktop: its own <c>backlog.db</c> and sync state, the Progress module
    /// and the SQLite stores composed through the registrations a host calls.</summary>
    private sealed class Desktop : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly HttpClient _http;
        private readonly TaskSyncSession _session;
        private readonly IServiceScope _screen;

        public Desktop(string root, HttpClient http, DeviceCredential credential)
        {
            Directory.CreateDirectory(root);
            _http = http;

            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Morning));
            services.AddSingleton<IProgressTaskSource, NoTasks>();
            services.AddSqlite(_ => root);
            services.AddProgressModule();

            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            _screen = _provider.CreateScope();

            var goals = _provider.GetRequiredService<IGoalReplication>();
            var tasks = new SqliteTaskRepository(root);

            _session = new TaskSyncSession(
                new TaskSyncClient(http),
                new TaskReplicaMerge(tasks, goals: goals),
                tasks,
                new FileTaskSyncStateStore(Path.Combine(root, "sync-state.json")),
                new InMemoryDeviceCredentialStore(credential),
                TimeProvider.System,
                goals: goals);
        }

        /// <summary>What a screen holding the port hears.</summary>
        public event Action? Changed
        {
            add => _screen.ServiceProvider.GetRequiredService<IGoals>().Changed += value;
            remove => _screen.ServiceProvider.GetRequiredService<IGoals>().Changed -= value;
        }

        public async Task<T> Use<T>(Func<IGoals, Task<Result<T>>> use)
        {
            using var scope = _provider.CreateScope();
            var result = await use(scope.ServiceProvider.GetRequiredService<IGoals>());
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
            return result.Value;
        }

        public async Task DeleteAsync(Guid id)
        {
            using var scope = _provider.CreateScope();
            var deleted = await scope.ServiceProvider.GetRequiredService<IGoals>().DeleteAsync(id, Cancellation);
            Assert.True(deleted.IsSuccess, deleted.IsFailure ? deleted.Error.Code : null);
        }

        public async Task<GoalReplicaCopyDto?> CopyAsync(Guid id) =>
            (await _provider.GetRequiredService<IGoalReplication>().ReadAllAsync(Cancellation))
                .SingleOrDefault(copy => copy.Id == id);

        public Task<Result<TaskSyncSummary>> SyncAsync() => _session.SyncAsync(Cancellation);

        public void Dispose()
        {
            _screen.Dispose();
            _provider.Dispose();
            _http.Dispose();
        }
    }

    private sealed class NoTasks : IProgressTaskSource
    {
        public Task<IReadOnlyList<ProgressTaskDto>> ReadAsync(ProgressTaskQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProgressTaskDto>>([]);
    }
}
