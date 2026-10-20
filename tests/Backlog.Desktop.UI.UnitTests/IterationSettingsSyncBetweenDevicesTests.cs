using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Infrastructure.Sync;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Sync.Abstractions.Services;
using Backlog.SharedKernel.Results;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Two paired desktops sharing their iteration — the length and the two-week start —
/// through the task feed (local ADR 0036, Verification 1, 3, 4 and 5, end to end).
/// <para>
/// Each device is composed the way a host composes itself — its own per-user
/// <c>iteration-settings.json</c>, seeded where a test says from its own shell navigation
/// file, the <see cref="IterationSettings"/> the Goals view and the stand-up read, its own
/// backlog, and the real merge and session — and the devices exchange through one replica
/// that keeps what the deployed one keeps. The store's and the merge's own tests pin each
/// rule; these say desktops built on them end reading the same iteration.
/// </para>
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class IterationSettingsSyncBetweenDevicesTests : IDisposable
{
    private static readonly Guid Owner = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static readonly DateOnly Monday5 = new(2026, 10, 5);
    private static readonly DateOnly Monday12 = new(2026, 10, 12);
    private static readonly DateOnly Monday19 = new(2026, 10, 19);
    private static readonly DateOnly Wednesday21 = new(2026, 10, 21);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "iteration-settings-sync-devices-" + Guid.NewGuid().ToString("N"));

    private readonly FakeTaskReplica _replica = new();

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Guid> _deviceIds = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Verification 1, the owner's report: A chooses two weeks — from the Monday
    /// of this week — and syncs; B syncs and reads two weeks from the same Monday, and its
    /// Goals view hears it without a restart.</summary>
    [Fact]
    public async Task Two_weeks_from_a_monday_chosen_on_one_desktop_reads_the_same_on_the_other()
    {
        using var a = Device("a");
        using var b = Device("b");
        Assert.Equal(IterationLength.Week, b.Settings.Length);

        a.Settings.SetLength(IterationLength.TwoWeeks, Wednesday21);
        Assert.Equal(Monday19, a.Settings.TwoWeekStart);
        Assert.True((await a.SyncAsync()).IsSuccess);

        var travelled = _replica.Held(IterationReplicaDocuments.SettingsId);
        Assert.NotNull(travelled);
        Assert.Equal("iteration-settings", travelled.Task.Type);

        var heard = 0;
        b.Settings.Changed += () => heard++;
        Assert.True((await b.SyncAsync()).IsSuccess);

        Assert.Equal((IterationLength.TwoWeeks, (DateOnly?)Monday19), (b.Settings.Length, b.Settings.TwoWeekStart));
        Assert.Equal(1, heard);
        Assert.Equal(new IterationChoice("two-weeks", Monday19), new IterationSettingsStore(b.SettingsPath).Choice);
    }

    /// <summary>Verification 3: both desktops change the setting before either syncs; the
    /// later change wins on both, length and anchor together, and the earlier device's
    /// push is refused.</summary>
    [Fact]
    public async Task Of_two_changes_between_syncs_the_later_one_wins_whole_on_both_desktops()
    {
        using var a = Device("a");
        using var b = Device("b");

        a.Clock.Advance(TimeSpan.FromMinutes(1));
        a.Settings.SetLength(IterationLength.TwoWeeks, Wednesday21);
        a.Settings.SetTwoWeekStart(Monday5);

        b.Clock.Advance(TimeSpan.FromMinutes(2));
        b.Settings.SetTwoWeekStart(Monday12);
        b.Settings.SetLength(IterationLength.Day, Wednesday21);

        Assert.True((await b.SyncAsync()).IsSuccess);
        Assert.Equal(1, (await a.PushAsync()).Refused);
        Assert.True((await a.SyncAsync()).IsSuccess);

        Assert.Equal((IterationLength.Day, (DateOnly?)Monday12), (a.Settings.Length, a.Settings.TwoWeekStart));
        Assert.Equal((IterationLength.Day, (DateOnly?)Monday12), (b.Settings.Length, b.Settings.TwoWeekStart));
    }

    /// <summary>Verification 4: a newly paired desktop that never chose an iteration
    /// sends nothing of its own, and takes A's rather than replacing it with Week.</summary>
    [Fact]
    public async Task A_new_desktop_that_never_chose_sends_nothing_and_takes_the_others()
    {
        using var a = Device("a");
        a.Settings.SetLength(IterationLength.TwoWeeks, Wednesday21);
        Assert.True((await a.SyncAsync()).IsSuccess);

        using var fresh = Device("fresh");
        var pushed = await fresh.PushAsync();
        Assert.Equal(0, pushed.Pushed);
        Assert.Equal(0, _replica.PushesFrom(fresh.DeviceId));
        Assert.False(File.Exists(fresh.SettingsPath));

        Assert.True((await fresh.SyncAsync()).IsSuccess);

        Assert.Equal((IterationLength.TwoWeeks, (DateOnly?)Monday19), (fresh.Settings.Length, fresh.Settings.TwoWeekStart));
        Assert.Equal((IterationLength.TwoWeeks, (DateOnly?)Monday19), (a.Settings.Length, a.Settings.TwoWeekStart));
    }

    /// <summary>Verification 5: a choice made on the old build seeds the file at the floor
    /// stamp, and with none on the feed it is pushed and becomes the person's value on the
    /// other desktop.</summary>
    [Fact]
    public async Task A_legacy_choice_with_none_on_the_feed_is_pushed_and_reaches_the_other_desktop()
    {
        using var a = Device("a", legacy: ("two-weeks", Monday5));
        using var b = Device("b");
        Assert.Equal((IterationLength.TwoWeeks, (DateOnly?)Monday5), (a.Settings.Length, a.Settings.TwoWeekStart));

        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.Equal(IterationSettingsStore.FloorStamp, _replica.Held(IterationReplicaDocuments.SettingsId)!.UpdatedAt);

        Assert.True((await b.SyncAsync()).IsSuccess);

        Assert.Equal((IterationLength.TwoWeeks, (DateOnly?)Monday5), (b.Settings.Length, b.Settings.TwoWeekStart));
    }

    /// <summary>Verification 5: a desktop that upgraded first and chose since holds the
    /// feed; one that upgrades later with a stale legacy value gives way to it.</summary>
    [Fact]
    public async Task A_document_already_on_the_feed_wins_over_a_seeded_copy()
    {
        using var a = Device("a");
        a.Settings.SetLength(IterationLength.Month, Wednesday21);
        Assert.True((await a.SyncAsync()).IsSuccess);

        using var late = Device("late", legacy: ("two-weeks", Monday5));
        Assert.True((await late.SyncAsync()).IsSuccess);

        Assert.Equal(IterationLength.Month, late.Settings.Length);
        Assert.Equal("month", new IterationSettingsStore(late.SettingsPath).Choice!.Length);
        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.Equal(IterationLength.Month, a.Settings.Length);
    }

    /// <summary>Verification 5: two desktops upgraded with different legacy values both end
    /// on the copy the feed holds.</summary>
    [Fact]
    public async Task Two_seeded_desktops_converge_on_the_copy_the_feed_holds()
    {
        using var a = Device("a", legacy: ("two-weeks", Monday5));
        using var b = Device("b", legacy: ("month", null));

        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);
        Assert.True((await a.SyncAsync()).IsSuccess);

        // A pushed first, so the feed holds A's seeded copy; B's equal-stamped push was
        // refused, and B took A's copy at the floor rather than reading it as an echo.
        var onTheFeed = _replica.Held(IterationReplicaDocuments.SettingsId)!;
        Assert.Equal(IterationSettingsStore.FloorStamp, onTheFeed.UpdatedAt);
        Assert.Contains("\"two-weeks\"", onTheFeed.Task.ContentMd, StringComparison.Ordinal);

        Assert.Equal(new IterationChoice("two-weeks", Monday5), a.Store.Choice);
        Assert.Equal(new IterationChoice("two-weeks", Monday5), b.Store.Choice);
        Assert.Equal((IterationLength.TwoWeeks, (DateOnly?)Monday5), (b.Settings.Length, b.Settings.TwoWeekStart));
    }

    private DesktopDevice Device(string name, (string Length, DateOnly? Start)? legacy = null) =>
        new(Path.Combine(_root, name), _replica, _deviceIds.GetOrAdd(name, _ => Guid.NewGuid()), legacy);

    /// <summary>
    /// One desktop: its own iteration settings — seeded from its own shell navigation file
    /// where the test gave it a legacy choice — and its own backlog and sync state, with a
    /// session over the shared replica.
    /// </summary>
    private sealed class DesktopDevice : IDisposable
    {
        private readonly HttpClient _http;
        private readonly TaskSyncSession _session;
        private readonly string _root;

        public DesktopDevice(string root, FakeTaskReplica replica, Guid deviceId, (string Length, DateOnly? Start)? legacy)
        {
            _root = root;
            DeviceId = deviceId;
            Clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));

            var workspace = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
            Assert.Null(workspace.TryUseRoot(Path.Combine(root, "workspace")));

            var shellPath = Path.Combine(root, "local", "shell-navigation.json");
            if (legacy is { } chosen)
            {
                var earlier = new ShellNavigationStore(shellPath);
                earlier.SetGoalsIteration(chosen.Length, chosen.Start);
                earlier.Flush();
                ShellNavigationStore.WaitForPendingWrites(shellPath);
            }

            SettingsPath = Path.Combine(root, "local", IterationSettingsStore.FileName);
            Store = new IterationSettingsStore(SettingsPath, new ShellNavigationStore(shellPath), Clock);
            Settings = new IterationSettings(Store);

            var tasks = TasksTestHost.RepositoryFor(workspace);

            _http = new HttpClient(replica.For(deviceId)) { BaseAddress = new Uri("https://sync.test") };
            _session = new TaskSyncSession(
                new TaskSyncClient(_http),
                new TaskReplicaMerge(tasks, iteration: Store),
                tasks,
                new FileTaskSyncStateStore(Path.Combine(root, "sync", "task-sync-state.json")),
                new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, deviceId, $"PC {deviceId:N}", "credential")),
                Clock,
                iteration: Store);
        }

        public Guid DeviceId { get; }

        public FakeTimeProvider Clock { get; }

        public string SettingsPath { get; }

        public IterationSettingsStore Store { get; }

        /// <summary>What the Goals view and the stand-up read on this desktop.</summary>
        public IterationSettings Settings { get; }

        public Task<Result<TaskSyncSummary>> SyncAsync() => _session.SyncAsync(TestContext.Current.CancellationToken);

        public async Task<TaskSyncSummary> PushAsync()
        {
            var pushed = await _session.PushAsync(TestContext.Current.CancellationToken);
            Assert.True(pushed.IsSuccess);
            return pushed.Value;
        }

        public void Dispose()
        {
            _http.Dispose();
            TestDatabases.Release(_root);
        }
    }
}
