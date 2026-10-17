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
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// Two desktops paired through one real sync service, each the real Progress module over
/// its own <c>backlog.db</c>, the real merge and session between it and the API — what
/// <see cref="GoalSyncBetweenDevicesTests"/> and <see cref="ImpedimentSyncBetweenDevicesTests"/>
/// both run on (<c>.devbook/domain/progress/requirements.md#syncing-goals-and-impediments</c>).
/// </summary>
internal sealed class PairedDesktops : IDisposable
{
    /// <summary>The day every desktop's clock reads. Stamps come off the wall clock, so
    /// two writes in a row are still ordered.</summary>
    public static readonly DateTimeOffset Morning = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly SyncServiceFactory _service = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "desktop-sync-" + Guid.NewGuid().ToString("N"));
    private readonly List<Desktop> _desktops = [];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Registers the study desktop, pairs the laptop to it with a code, and
    /// answers both.</summary>
    public async Task<(Desktop A, Desktop B)> PairAsync()
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

    public void Dispose()
    {
        foreach (var desktop in _desktops) desktop.Dispose();
        _service.Dispose();

        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>One desktop: its own <c>backlog.db</c> and sync state, the Progress module
    /// and the SQLite stores composed through the registrations a host calls.</summary>
    internal sealed class Desktop : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly HttpClient _http;
        private readonly TaskSyncSession _session;
        private readonly IServiceScope _screen;
        private readonly SqliteTaskRepository _tasks;

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
            var impediments = _provider.GetRequiredService<IImpedimentReplication>();
            _tasks = new SqliteTaskRepository(root);

            _session = new TaskSyncSession(
                new TaskSyncClient(http),
                new TaskReplicaMerge(_tasks, goals: goals, impediments: impediments),
                _tasks,
                new FileTaskSyncStateStore(Path.Combine(root, "sync-state.json")),
                new InMemoryDeviceCredentialStore(credential),
                TimeProvider.System,
                goals: goals,
                impediments: impediments);
        }

        /// <summary>The HTTP client this desktop talks to the service through, already
        /// bearing its token.</summary>
        public HttpClient Http => _http;

        /// <summary>What a screen holding the goals port hears.</summary>
        public event Action? GoalsChanged
        {
            add => _screen.ServiceProvider.GetRequiredService<IGoals>().Changed += value;
            remove => _screen.ServiceProvider.GetRequiredService<IGoals>().Changed -= value;
        }

        /// <summary>What a screen holding the impediments port hears.</summary>
        public event Action? ImpedimentsChanged
        {
            add => _screen.ServiceProvider.GetRequiredService<IImpediments>().Changed += value;
            remove => _screen.ServiceProvider.GetRequiredService<IImpediments>().Changed -= value;
        }

        public Task<T> Goals<T>(Func<IGoals, Task<Result<T>>> use) => UseAsync(use);

        public Task<T> Impediments<T>(Func<IImpediments, Task<Result<T>>> use) => UseAsync(use);

        public async Task DeleteGoalAsync(Guid id)
        {
            using var scope = _provider.CreateScope();
            var deleted = await scope.ServiceProvider.GetRequiredService<IGoals>().DeleteAsync(id, Cancellation);
            Assert.True(deleted.IsSuccess, deleted.IsFailure ? deleted.Error.Code : null);
        }

        public async Task<GoalReplicaCopyDto?> GoalCopyAsync(Guid id) =>
            (await _provider.GetRequiredService<IGoalReplication>().ReadAllAsync(Cancellation))
                .SingleOrDefault(copy => copy.Id == id);

        public async Task<ImpedimentReplicaCopyDto?> ImpedimentCopyAsync(Guid id) =>
            (await _provider.GetRequiredService<IImpedimentReplication>().ReadAllAsync(Cancellation))
                .SingleOrDefault(copy => copy.Id == id);

        /// <summary>Whether this desktop's task store holds a row under the id.</summary>
        public async Task<bool> HoldsTaskAsync(Guid id) => await _tasks.GetAsync(id, Cancellation) is not null;

        public Task<Result<TaskSyncSummary>> SyncAsync() => _session.SyncAsync(Cancellation);

        public void Dispose()
        {
            _screen.Dispose();
            _provider.Dispose();
            _http.Dispose();
        }

        private async Task<T> UseAsync<TPort, T>(Func<TPort, Task<Result<T>>> use)
            where TPort : notnull
        {
            using var scope = _provider.CreateScope();
            var result = await use(scope.ServiceProvider.GetRequiredService<TPort>());
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
            return result.Value;
        }
    }

    private sealed class NoTasks : IProgressTaskSource
    {
        public Task<IReadOnlyList<ProgressTaskDto>> ReadAsync(ProgressTaskQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProgressTaskDto>>([]);
    }
}
