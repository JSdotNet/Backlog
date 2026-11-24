using System.Text.Json;

using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.Services;
using Backlog.SharedKernel;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The follower that keeps every alias-keyed store on the registry's aliases (local
/// ADR 0060 §3).
/// <list type="bullet">
/// <item>The device that relabels re-keys the replicated stores.</item>
/// <item>Every device re-keys its own local stores once per move group.</item>
/// <item>A backstop moves what an orphaned alias still holds onto the row it resolves
/// to.</item>
/// </list>
/// The fake stores always complete asynchronously, so every pass runs off the thread
/// that raised it, as it does against a real store's file I/O.
/// </summary>
public sealed class RepositoryAliasFollowerTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _temp = new("repository-alias-follower-tests");

    private readonly List<RepositoryAliasFollower> _followers = [];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var follower in _followers) follower.Dispose();
        _temp.Dispose();
    }

    // --- The editing device --------------------------------------------------------

    [Fact]
    public async Task An_edit_re_keys_each_replicated_store_once_and_each_local_store_once()
    {
        var device = Device("a");
        Assert.Null(device.Store.SetRepositories(Parse("backlog = JSdotNet/Backlog")));
        var replicated = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Replicated), ("backlog", "pace"));
        var local = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Local), ("backlog", "last surface"));
        var follower = await StartedAsync(device, replicated, local);

        Assert.Null(device.Store.SetRepositories(Parse("bl = JSdotNet/Backlog")));
        await follower.WhenIdleAsync();

        Assert.Equal(new Dictionary<string, string> { ["bl"] = "pace" }, replicated.Data);
        Assert.Equal(new Dictionary<string, string> { ["bl"] = "last surface" }, local.Data);
        Assert.Equal(1, replicated.Changes);
        Assert.Equal(1, local.Changes);
    }

    [Fact]
    public async Task A_swap_is_applied_as_one_mapping()
    {
        var device = Device("a");
        Assert.Null(device.Store.SetRepositories(Parse("a = JSdotNet/Alpha", "b = JSdotNet/Beta")));
        var replicated = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Replicated), ("a", "alpha's"), ("b", "beta's"));
        var local = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Local), ("a", "alpha's"), ("b", "beta's"));
        var follower = await StartedAsync(device, replicated, local);

        Assert.Null(device.Store.SetRepositories(Parse("b = JSdotNet/Alpha", "a = JSdotNet/Beta")));
        await follower.WhenIdleAsync();

        // A swap applied twice is undone, so this also proves each store took it once:
        // the edit's Changed reaches the follower before its moves do.
        var swapped = new Dictionary<string, string> { ["a"] = "beta's", ["b"] = "alpha's" };
        Assert.Equal(swapped, replicated.Data);
        Assert.Equal(swapped, local.Data);
    }

    [Fact]
    public async Task A_chain_in_one_edit_moves_each_repository_data_with_it()
    {
        var device = Device("a");
        Assert.Null(device.Store.SetRepositories(Parse("x = JSdotNet/Ex", "y = JSdotNet/Why")));
        var replicated = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Replicated), ("x", "ex's"), ("y", "why's"));
        var follower = await StartedAsync(device, replicated);

        // x → y and y → z in one edit: the backstop must not run x's data into y's
        // ahead of the edit's single mapping.
        Assert.Null(device.Store.SetRepositories(Parse("y = JSdotNet/Ex", "z = JSdotNet/Why")));
        await follower.WhenIdleAsync();

        Assert.Equal(new Dictionary<string, string> { ["y"] = "ex's", ["z"] = "why's" }, replicated.Data);
    }

    [Fact]
    public async Task A_merge_keeps_the_kept_repository_data_where_both_aliases_hold_some()
    {
        var device = Device("a");
        Assert.Null(device.Store.SetRepositories(Parse("old = JSdotNet/Old", "kept = JSdotNet/Kept")));
        var replicated = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Replicated), ("old", "old's colour"), ("kept", "kept's colour"));
        var follower = await StartedAsync(device, replicated);

        Assert.Null(device.Store.RenameRepository("old", "JSdotNet/Kept", out _));
        await follower.WhenIdleAsync();

        Assert.Equal(new Dictionary<string, string> { ["kept"] = "kept's colour" }, replicated.Data);
    }

    [Fact]
    public async Task A_relabel_whose_registry_save_failed_re_keys_nothing_until_a_save_lands()
    {
        var device = Device("a");
        Assert.Null(device.Store.SetRepositories(Parse("backlog = JSdotNet/Backlog")));
        var replicated = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Replicated), ("backlog", "pace"));
        var local = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Local), ("backlog", "last surface"));
        var follower = await StartedAsync(device, replicated, local);

        File.SetAttributes(device.Store.RegistryPath, FileAttributes.ReadOnly);
        try
        {
            Assert.NotNull(device.Store.SetRepositories(Parse("bl = JSdotNet/Backlog")));

            // Another change that does not land either still announces Changed.
            Assert.NotNull(device.Store.SetShowRepositoryColours(true));
            await follower.WhenIdleAsync();

            // The next start would read backlog: nothing may sit under bl yet.
            Assert.Equal(new Dictionary<string, string> { ["backlog"] = "pace" }, replicated.Data);
            Assert.Equal(new Dictionary<string, string> { ["backlog"] = "last surface" }, local.Data);
        }
        finally
        {
            File.SetAttributes(device.Store.RegistryPath, FileAttributes.Normal);
        }

        // A later save writes the move with the rest, and the data follows it then.
        Assert.Null(device.Store.SetShowRepositoryColours(false));
        await follower.WhenIdleAsync();

        Assert.Equal(new Dictionary<string, string> { ["bl"] = "pace" }, replicated.Data);
        Assert.Equal(new Dictionary<string, string> { ["bl"] = "last surface" }, local.Data);
    }

    [Fact]
    public async Task Edits_and_changes_queued_behind_a_store_still_working_apply_each_group_once()
    {
        var device = Device("a");
        Assert.Null(device.Store.SetRepositories(Parse("a = JSdotNet/Alpha", "b = JSdotNet/Beta")));
        var replicated = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Replicated), ("a", "alpha's"), ("b", "beta's"));
        var local = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Local), ("a", "alpha's"), ("b", "beta's"));
        var follower = await StartedAsync(device, replicated, local);

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        local.Gate = release.Task;

        // The swap's pass waits inside the local store; a plain change and its
        // Changed pass queue behind it, as does the swap's own Changed.
        Assert.Null(device.Store.SetRepositories(Parse("b = JSdotNet/Alpha", "a = JSdotNet/Beta")));
        Assert.Null(device.Store.SetShowRepositoryColours(true));
        await local.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        release.SetResult();
        await follower.WhenIdleAsync();

        var swapped = new Dictionary<string, string> { ["a"] = "beta's", ["b"] = "alpha's" };
        Assert.Equal(swapped, replicated.Data);
        Assert.Equal(swapped, local.Data);
        Assert.Equal(1, replicated.Changes);
        Assert.Equal(1, local.Changes);
    }

    // --- Another device: the local stores catch up ---------------------------------

    [Fact]
    public async Task A_registry_arriving_with_two_move_groups_applies_them_in_order_and_never_again_after_a_restart()
    {
        var a = Device("a");
        Assert.Null(a.Store.SetRepositories(Parse("backlog = JSdotNet/Backlog")));
        var b = Device("b");
        await Carry(a, b);

        var local = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Local), ("backlog", "collapsed"));
        var follower = await StartedAsync(b, local);

        Assert.Null(a.Store.SetRepositories(Parse("bl = JSdotNet/Backlog")));
        a.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(a.Store.SetRepositories(Parse("b2 = JSdotNet/Backlog")));
        await Carry(a, b);
        await follower.WhenIdleAsync();

        // In order: backlog → bl, then bl → b2. Out of order would leave it under bl.
        Assert.Equal(new Dictionary<string, string> { ["b2"] = "collapsed" }, local.Data);
        Assert.Equal(Morning.AddMinutes(1), b.Store.AliasMovesAppliedThrough(KeyOf<FakeAliasKeyedStore>()));

        // After a restart nothing applies again: a fresh mark under the first old alias
        // stays where it was written.
        var restarted = Device("b");
        var again = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Local), ("backlog", "written since"));
        await StartedAsync(restarted, again);

        Assert.Equal(new Dictionary<string, string> { ["backlog"] = "written since" }, again.Data);
        Assert.Equal(0, again.Changes);
    }

    [Fact]
    public void The_cursor_is_this_device_own_per_store_and_never_travels_in_the_registry()
    {
        var device = Device("a");
        Assert.Null(device.Store.SetRepositories(Parse("backlog = JSdotNet/Backlog")));
        Assert.Null(device.Store.SetRepositories(Parse("bl = JSdotNet/Backlog")));
        Assert.Null(device.Store.AliasMovesAppliedThrough("ledger"));

        Assert.Null(device.Store.MarkAliasMovesApplied("ledger", Morning));
        Assert.Null(device.Store.MarkAliasMovesApplied("ledger", Morning.AddDays(-1)));

        var reopened = Device("a").Store;
        Assert.Equal(Morning, reopened.AliasMovesAppliedThrough("ledger"));
        Assert.Null(reopened.AliasMovesAppliedThrough("navigation"));
        Assert.DoesNotContain("appliedThrough", File.ReadAllText(device.Store.RegistryPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_failing_local_store_holds_back_only_itself_and_start_still_completes()
    {
        var device = Device("a");
        Assert.Null(device.Store.SetRepositories(Parse("backlog = JSdotNet/Backlog")));
        Assert.Null(device.Store.SetRepositories(Parse("bl = JSdotNet/Backlog")));
        var failing = Holding(new LedgerStore(), ("backlog", "ledger"));
        failing.Fails = true;
        var healthy = Holding(new NavigationStore(), ("backlog", "navigation"));

        var follower = await StartedAsync(device, failing, healthy);

        Assert.Equal(new Dictionary<string, string> { ["bl"] = "navigation" }, healthy.Data);
        Assert.Null(device.Store.AliasMovesAppliedThrough(KeyOf<LedgerStore>()));

        // A later group still reaches the healthy store.
        device.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(device.Store.SetRepositories(Parse("b3 = JSdotNet/Backlog")));
        await follower.WhenIdleAsync();

        Assert.Equal(new Dictionary<string, string> { ["b3"] = "navigation" }, healthy.Data);
        Assert.Equal(Morning.AddMinutes(1), device.Store.AliasMovesAppliedThrough(KeyOf<NavigationStore>()));

        // The next pass offers the failing store its groups again, in order.
        failing.Fails = false;
        await StartedAsync(Device("a"), failing, healthy);

        Assert.Equal(new Dictionary<string, string> { ["b3"] = "ledger" }, failing.Data);
        Assert.Equal(Morning.AddMinutes(1), Device("a").Store.AliasMovesAppliedThrough(KeyOf<LedgerStore>()));
    }

    [Fact]
    public async Task A_store_without_a_cursor_holding_only_current_aliases_starts_at_the_newest_move()
    {
        var device = Device("a");
        Assert.Null(device.Store.SetRepositories(Parse("a = JSdotNet/Alpha", "b = JSdotNet/Beta")));
        Assert.Null(device.Store.SetRepositories(Parse("b = JSdotNet/Alpha", "a = JSdotNet/Beta")));

        // Written under the aliases as they are now: replaying the swap would undo it.
        var local = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Local), ("a", "beta's"), ("b", "alpha's"));
        var restarted = Device("a");
        await StartedAsync(restarted, local);

        Assert.Equal(new Dictionary<string, string> { ["a"] = "beta's", ["b"] = "alpha's" }, local.Data);
        Assert.Equal(Morning, restarted.Store.AliasMovesAppliedThrough(KeyOf<FakeAliasKeyedStore>()));
    }

    // --- Start ---------------------------------------------------------------------

    [Fact]
    public async Task A_start_cancelled_part_way_can_be_started_again()
    {
        var device = Device("a");
        Assert.Null(device.Store.SetRepositories(Parse("backlog = JSdotNet/Backlog")));
        Assert.Null(device.Store.SetRepositories(Parse("bl = JSdotNet/Backlog")));
        var local = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Local), ("backlog", "last surface"));
        using var cancel = new CancellationTokenSource();
        local.BeforeMove = cancel.Cancel;

        var follower = new RepositoryAliasFollower(device.Store, [local]);
        _followers.Add(follower);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => follower.StartAsync(cancel.Token));
        Assert.Equal(new Dictionary<string, string> { ["backlog"] = "last surface" }, local.Data);

        local.BeforeMove = null;
        await follower.StartAsync(Cancellation);

        Assert.Equal(new Dictionary<string, string> { ["bl"] = "last surface" }, local.Data);
    }

    [Fact]
    public async Task A_shutdown_during_a_pass_stops_before_the_next_store_and_logs_no_failure()
    {
        var device = Device("a");
        Assert.Null(device.Store.SetRepositories(Parse("backlog = JSdotNet/Backlog")));
        Assert.Null(device.Store.SetRepositories(Parse("bl = JSdotNet/Backlog")));
        var first = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Replicated), ("backlog", "pace"));
        var second = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Replicated), ("backlog", "subject state"));
        var log = new LevelLogger();

        var follower = new RepositoryAliasFollower(device.Store, [first, second], log);
        _followers.Add(follower);

        // The host disposes the container while the backstop is in its first store,
        // which finishes its write; the follower must not go on to the next.
        first.BeforeMove = follower.Dispose;
        first.HonoursCancellation = false;
        await follower.StartAsync(Cancellation);

        Assert.Empty(second.Origins);
        Assert.Equal(new Dictionary<string, string> { ["backlog"] = "subject state" }, second.Data);
        Assert.DoesNotContain(log.Levels, level => level >= LogLevel.Warning);
    }

    // --- The backstop --------------------------------------------------------------

    [Fact]
    public async Task The_backstop_moves_an_orphaned_alias_and_a_second_pass_changes_nothing()
    {
        var device = Device("a");
        Assert.Null(device.Store.SetRepositories(Parse("backlog = JSdotNet/Backlog")));
        Assert.Null(device.Store.SetRepositories(Parse("bl = JSdotNet/Backlog")));

        // A crash between the registry save and the re-key leaves the data behind.
        var replicated = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Replicated), ("backlog", "pace"), ("gone", "unresolved"));
        await StartedAsync(device, replicated);

        Assert.Equal(new Dictionary<string, string> { ["bl"] = "pace", ["gone"] = "unresolved" }, replicated.Data);

        await StartedAsync(Device("a"), replicated);
        Assert.Equal(1, replicated.Changes);
    }

    [Fact]
    public async Task An_edit_moves_as_an_edit_and_the_backstop_as_a_backstop()
    {
        var device = Device("a");
        Assert.Null(device.Store.SetRepositories(Parse("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs")));
        Assert.Null(device.Store.SetRepositories(Parse("bl = JSdotNet/Backlog", "docs = JSdotNet/Docs")));

        // Left under backlog by a crash before the re-key: the backstop's to move.
        var replicated = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Replicated), ("backlog", "pace"), ("docs", "docs pace"));
        var local = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Local), ("docs", "collapsed"));
        var follower = await StartedAsync(device, replicated, local);

        Assert.Equal([AliasMoveOrigin.Backstop], replicated.Origins);

        device.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(device.Store.SetRepositories(Parse("bl = JSdotNet/Backlog", "documentation = JSdotNet/Docs")));
        await follower.WhenIdleAsync();

        Assert.Equal([AliasMoveOrigin.Backstop, AliasMoveOrigin.Edit], replicated.Origins);
        Assert.Equal([AliasMoveOrigin.Edit], local.Origins);
        Assert.Equal(new Dictionary<string, string> { ["bl"] = "pace", ["documentation"] = "docs pace" }, replicated.Data);
        Assert.Equal(new Dictionary<string, string> { ["documentation"] = "collapsed" }, local.Data);
    }

    [Fact]
    public async Task An_alias_a_row_carries_is_never_moved_by_the_backstop()
    {
        var device = Device("a");
        Assert.Null(device.Store.SetRepositories(Parse("backlog = JSdotNet/Backlog")));
        Assert.Null(device.Store.SetRepositories(Parse("bl = JSdotNet/Backlog")));
        Assert.Null(device.Store.SetRepositories(Parse("bl = JSdotNet/Backlog", "backlog = JSdotNet/Backlog-Next")));
        var replicated = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Replicated), ("backlog", "the new repository's"));

        await StartedAsync(device, replicated);

        Assert.Equal(new Dictionary<string, string> { ["backlog"] = "the new repository's" }, replicated.Data);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Both_arrival_orders_end_in_the_same_state(bool registryFirst)
    {
        var a = Device("a");
        Assert.Null(a.Store.SetRepositories(Parse("backlog = JSdotNet/Backlog")));
        var b = Device("b");
        await Carry(a, b);
        var onB = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Replicated), ("backlog", "older on b"));
        var follower = await StartedAsync(b, onB);

        Assert.Null(a.Store.SetRepositories(Parse("bl = JSdotNet/Backlog")));

        // A's re-keyed document is newer than B's copy under the old alias, so the
        // sync takes it whichever alias B holds the older one under.
        if (registryFirst)
        {
            await Carry(a, b);
            await follower.WhenIdleAsync();
            onB.Receive("bl", "re-keyed on a");
        }
        else
        {
            onB.Receive("bl", "re-keyed on a");
            await Carry(a, b);
            await follower.WhenIdleAsync();
        }

        Assert.Equal(new Dictionary<string, string> { ["bl"] = "re-keyed on a" }, onB.Data);
    }

    // --- §5 at start -----------------------------------------------------------------

    [Fact]
    public async Task Start_relabels_a_repository_renamed_before_the_decision_and_its_data_follows()
    {
        // What a store from before local ADR 0060 left: the rename kept the alias.
        WriteRegistry("a", """
            {
              "repositories": [ { "id": "JSdotNet/app-defaults", "alias": "project-guidelines-mcp" } ],
              "renames": [ { "from": "JSdotNet/Project-Guidelines-MCP", "to": "JSdotNet/app-defaults", "at": "2026-11-20T09:00:00+00:00" } ]
            }
            """);
        var restarted = Device("a");
        var replicated = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Replicated), ("project-guidelines-mcp", "band colour"));
        var local = Holding(new FakeAliasKeyedStore(AliasKeyedStoreKind.Local), ("project-guidelines-mcp", "imported"));

        await StartedAsync(restarted, replicated, local);

        Assert.Equal(new Dictionary<string, string> { ["app-defaults"] = "band colour" }, replicated.Data);
        Assert.Equal(new Dictionary<string, string> { ["app-defaults"] = "imported" }, local.Data);
        Assert.Equal(1, replicated.Changes);
        Assert.Equal(1, local.Changes);
    }

    // --- Helpers ---------------------------------------------------------------------

    private sealed record DeviceUnderTest(GitHubSettingsStore Store, IGitHubSettingsReplication Replication, FakeTimeProvider Clock);

    /// <summary>One device; asking for the same name again is that device after a
    /// restart.</summary>
    private DeviceUnderTest Device(string name)
    {
        var clock = new FakeTimeProvider(Morning);
        var store = new GitHubSettingsStore(
            _temp.Combine(name, "local", "github.json"),
            () => _temp.Combine(name, "workspace"),
            clock);

        return new DeviceUnderTest(store, new GitHubSettingsReplication(store), clock);
    }

    private async Task<RepositoryAliasFollower> StartedAsync(DeviceUnderTest device, params FakeAliasKeyedStore[] stores)
    {
        var follower = new RepositoryAliasFollower(device.Store, stores);
        _followers.Add(follower);

        await follower.StartAsync(Cancellation);
        return follower;
    }

    private static T Holding<T>(T store, params (string Alias, string Value)[] data)
        where T : FakeAliasKeyedStore
    {
        foreach (var (alias, value) in data) store.Data[alias] = value;
        return store;
    }

    /// <summary>The key the follower keeps a local store's cursor under.</summary>
    private static string KeyOf<T>() => typeof(T).FullName!;

    private static async Task Carry(DeviceUnderTest from, DeviceUnderTest to)
    {
        var copy = await from.Replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation);
        Assert.NotNull(copy);

        var travelled = JsonSerializer.Deserialize<GitHubReplicaCopyDto>(JsonSerializer.Serialize(copy))!;
        await to.Replication.ApplyAsync(GitHubReplicaDocument.Registry, travelled, Cancellation);
    }

    private void WriteRegistry(string device, string json)
    {
        var path = Path.Combine(_temp.Combine(device, "workspace"), GitHubSettingsStore.RegistryFolderName, "repos.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private static List<GitHubRepositoryRef> Parse(params string[] lines)
    {
        var (repositories, errors) = GitHubSettings.ParseText(string.Join('\n', lines));
        Assert.Empty(errors);
        return repositories;
    }

    /// <summary>The levels the follower logged at, in order.</summary>
    // Differs from RecordingLogger: thread-safe, since the follower logs from its own passes while the test reads.
    private sealed class LevelLogger : ILogger<RepositoryAliasFollower>
    {
        private readonly Lock _lock = new();
        private readonly List<LogLevel> _levels = [];

        public IReadOnlyList<LogLevel> Levels
        {
            get
            {
                lock (_lock) return [.. _levels];
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lock) _levels.Add(logLevel);
        }
    }

    /// <summary>Two local stores of their own types, so each keeps its own
    /// cursor.</summary>
    private sealed class LedgerStore() : FakeAliasKeyedStore(AliasKeyedStoreKind.Local);

    private sealed class NavigationStore() : FakeAliasKeyedStore(AliasKeyedStoreKind.Local);

    /// <summary>
    /// An alias-keyed store held in memory, with the port's rules: the whole mapping
    /// at once, the target's data kept where both aliases hold some, and an alias the
    /// mapping does not name left where it is. It always completes asynchronously,
    /// and can be held at a gate the test releases.
    /// </summary>
    private class FakeAliasKeyedStore(AliasKeyedStoreKind kind) : IRepositoryAliasKeyedStore
    {
        private readonly Lock _lock = new();

        public Dictionary<string, string> Data { get; private set; } = new(StringComparer.Ordinal);

        /// <summary>How many calls changed anything.</summary>
        public int Changes { get; private set; }

        public bool Fails { get; set; }

        /// <summary>What a move waits for before it applies, when set.</summary>
        public Task? Gate { get; set; }

        /// <summary>Completed when a move first reaches the gate.</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Run as a move starts, before it checks for cancellation.</summary>
        public Action? BeforeMove { get; set; }

        /// <summary>Whether a move throws once its token is cancelled; off, it finishes
        /// the move, as a store whose last write was already under way does.</summary>
        public bool HonoursCancellation { get; set; } = true;

        public AliasKeyedStoreKind Kind => kind;

        public IReadOnlyCollection<string> HeldAliases()
        {
            lock (_lock) return [.. Data.Keys];
        }

        /// <summary>A document arriving from another device.</summary>
        public void Receive(string alias, string value)
        {
            lock (_lock) Data[alias] = value;
        }

        /// <summary>Why each move that reached the store was asked for, in order.</summary>
        public List<AliasMoveOrigin> Origins { get; } = [];

        public Task<bool> MoveAliasesAsync(IReadOnlyDictionary<string, string> oldToNew, AliasMoveOrigin origin, CancellationToken cancellationToken)
        {
            lock (_lock) Origins.Add(origin);
            return MoveAliasesAsync(oldToNew, cancellationToken);
        }

        public async Task<bool> MoveAliasesAsync(IReadOnlyDictionary<string, string> oldToNew, CancellationToken cancellationToken)
        {
            await Task.Yield();

            BeforeMove?.Invoke();
            if (HonoursCancellation) cancellationToken.ThrowIfCancellationRequested();
            if (Fails) throw new IOException("The store could not be written.");

            if (Gate is { } gate)
            {
                Entered.TrySetResult();
                await gate;
            }

            lock (_lock)
            {
                var next = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (alias, value) in Data.Where(d => !oldToNew.ContainsKey(d.Key))) next[alias] = value;
                foreach (var (alias, value) in Data.Where(d => oldToNew.ContainsKey(d.Key))) next.TryAdd(oldToNew[alias], value);

                var changed = next.Count != Data.Count || next.Any(n => !Data.TryGetValue(n.Key, out var was) || was != n.Value);
                Data = next;
                if (changed) Changes++;
                return changed;
            }
        }
    }
}
