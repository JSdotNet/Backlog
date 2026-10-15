using System.Globalization;

using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sync;
using Backlog.Modules.Sync.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Two paired desktops sharing Clear My Day overnight through the task feed (local ADR
/// 0032, Verification 1 and 3, end to end).
/// <para>
/// Each device is composed the way a host composes itself — its own per-user
/// <c>my-day-preferences.json</c>, its own backlog, the real merge and session, and
/// the Tasks list over that backlog — and the two exchange through one replica that
/// keeps what the deployed one keeps. The store's and the merge's own tests pin each
/// rule; these say two devices built on them end reading the same My Day.
/// </para>
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class MyDayPreferencesSyncBetweenDevicesTests : IDisposable
{
    private static readonly Guid Owner = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "my-day-preferences-sync-devices-" + Guid.NewGuid().ToString("N"));

    private readonly FakeTaskReplica _replica = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private static string Token(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Verification 1: A turns the switch off and syncs; B, whose list is open
    /// on My Day, syncs and reads it off, and the task A put in yesterday's My Day is
    /// in B's today — with no task written on either device.</summary>
    [Fact]
    public async Task A_switch_turned_off_reaches_the_other_desktop_and_its_my_day_keeps_the_carried_task()
    {
        using var a = Device("a");
        using var b = Device("b");

        await a.WriteAsync($"# Deploy it\n`task` `!ready` `myday:{Token(Today.AddDays(-1))}`\n");
        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);

        using var list = await b.OpenListAsync();
        list.SetMyDayFilter(Today);
        Assert.Empty(list.FilteredRows);

        Assert.Null(a.Preferences.SetClearsOvernight(false));
        Assert.True((await a.SyncAsync()).IsSuccess);

        var travelled = _replica.Held(MyDayReplicaDocuments.PreferencesId);
        Assert.NotNull(travelled);
        Assert.Equal("my-day-preferences", travelled.Task.Type);

        Assert.True((await b.SyncAsync()).IsSuccess);

        Assert.False(b.Preferences.ClearsOvernight);
        Assert.False(list.ClearsMyDayOvernight);
        Assert.Equal("Deploy it", Assert.Single(list.FilteredRows).PreviewTitle);
        Assert.False(new MyDayPreferencesStore(b.PreferencesPath).ClearsOvernight);
    }

    /// <summary>Verification 3: a newly paired desktop that never touched the switch
    /// sends nothing of its own, and takes A's value rather than replacing it with the
    /// default.</summary>
    [Fact]
    public async Task A_new_desktop_with_no_preferences_sends_nothing_and_takes_the_others()
    {
        using var a = Device("a");
        Assert.Null(a.Preferences.SetClearsOvernight(false));
        Assert.True((await a.SyncAsync()).IsSuccess);

        using var fresh = Device("fresh");
        var pushed = await fresh.PushAsync();
        Assert.Equal(0, pushed.Pushed);
        Assert.Equal(0, _replica.PushesFrom(fresh.DeviceId));
        Assert.False(File.Exists(fresh.PreferencesPath));

        Assert.True((await fresh.SyncAsync()).IsSuccess);

        Assert.False(fresh.Preferences.ClearsOvernight);
        Assert.False(a.Preferences.ClearsOvernight);
    }

    /// <summary>Both desktops flip the switch before either syncs: the later flip wins
    /// on both, and the earlier device's push is refused.</summary>
    [Fact]
    public async Task Of_two_flips_between_syncs_the_later_one_wins_on_both_desktops()
    {
        using var a = Device("a");
        using var b = Device("b");

        a.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(a.Preferences.SetClearsOvernight(false));

        b.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(b.Preferences.SetClearsOvernight(false));
        Assert.Null(b.Preferences.SetClearsOvernight(true));

        Assert.True((await b.SyncAsync()).IsSuccess);
        Assert.Equal(1, (await a.PushAsync()).Refused);
        Assert.True((await a.SyncAsync()).IsSuccess);

        Assert.True(a.Preferences.ClearsOvernight);
        Assert.True(b.Preferences.ClearsOvernight);
    }

    private DesktopDevice Device(string name) =>
        new(Path.Combine(_root, name), _replica, DeviceIds.GetOrAdd(name, _ => Guid.NewGuid()));

    private System.Collections.Concurrent.ConcurrentDictionary<string, Guid> DeviceIds { get; } = new();

    /// <summary>
    /// One desktop: its own My Day preferences and its own backlog and sync state,
    /// with a session over the shared replica and a Tasks list over the backlog.
    /// </summary>
    private sealed class DesktopDevice : IDisposable
    {
        private readonly HttpClient _http;
        private readonly TaskSyncSession _session;
        private readonly WorkspaceSettingsStore _workspace;
        private readonly string _root;

        public DesktopDevice(string root, FakeTaskReplica replica, Guid deviceId)
        {
            _root = root;
            DeviceId = deviceId;
            Clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));

            _workspace = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
            Assert.Null(_workspace.TryUseRoot(Path.Combine(root, "workspace")));

            PreferencesPath = Path.Combine(root, "local", MyDayPreferencesStore.FileName);
            Preferences = new MyDayPreferencesStore(PreferencesPath, Clock);

            var tasks = TasksTestHost.RepositoryFor(_workspace);

            _http = new HttpClient(replica.For(deviceId)) { BaseAddress = new Uri("https://sync.test") };
            _session = new TaskSyncSession(
                new TaskSyncClient(_http),
                new TaskReplicaMerge(tasks, myDay: Preferences),
                tasks,
                new FileTaskSyncStateStore(Path.Combine(root, "sync", "task-sync-state.json")),
                new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, deviceId, $"PC {deviceId:N}", "credential")),
                Clock,
                myDay: Preferences);
        }

        public Guid DeviceId { get; }

        public FakeTimeProvider Clock { get; }

        public string PreferencesPath { get; }

        public MyDayPreferencesStore Preferences { get; }

        public async Task WriteAsync(string text)
        {
            var saved = await TasksTestHost.EntriesFor(_workspace).SaveFromTextAsync(null, text, 0, cancellationToken: Cancellation);
            Assert.True(saved.IsSuccess);
        }

        /// <summary>The Tasks list open on this device, reading this device's backlog
        /// under this device's switch. Disposed by the caller.</summary>
        public async Task<TasksDesktopState> OpenListAsync()
        {
            var gitHub = new GitHubIntegration(
                new GitHubSettingsStore(Path.Combine(_root, "github", "github.json")),
                new TasksPaneHost.FakeGitHubClient(),
                new NoProbe());

            var list = TasksTestHost.StateFor(_workspace, gitHub, myDayPreferences: Preferences);
            await list.InitializeAsync();
            return list;
        }

        public Task<Result<TaskSyncSummary>> SyncAsync() => _session.SyncAsync(Cancellation);

        public async Task<TaskSyncSummary> PushAsync()
        {
            var pushed = await _session.PushAsync(Cancellation);
            Assert.True(pushed.IsSuccess);
            return pushed.Value;
        }

        public void Dispose()
        {
            _http.Dispose();
            TestDatabases.Release(_root);
        }
    }

    private sealed class NoProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }
}
