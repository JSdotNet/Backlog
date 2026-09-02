using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Backlog.Abstractions.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Two machines can share one <c>backlog.db</c> through a synced folder, and the
/// second one has no way to be told about the first one's writes. Until the store
/// can push, the list looks: it compares the newest timestamp across the database
/// and its write-ahead log sidecars against the one it last read the store at, and
/// starts over when they differ.
/// <para>
/// The tick is driven directly rather than waited out. A timer test is slow when
/// it passes and flaky when it does not, and what is worth pinning here is the
/// decision a tick makes — not that <see cref="Timer"/> fires.
/// </para>
/// </summary>
public sealed class BacklogExternalChangePollingTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    [Fact]
    public async Task A_store_written_to_by_something_else_is_read_again()
    {
        var root = TempRoot();
        var settings = new BacklogRefreshSettingsStore(Path.Combine(root, "refresh.json"));

        using var state = State(root, settings);
        await state.InitializeAsync();

        Assert.Empty(state.Rows);

        // The other machine, writing to the same folder.
        using var elsewhere = State(root);
        await elsewhere.InitializeAsync();
        await WriteEntryAsync(elsewhere, "# Written on the other machine\n`task` `!ready`\n");

        // The synced folder lands the other machine's file here.
        TouchDatabase(root);

        await state.CheckForExternalChangesAsync();

        var row = Assert.Single(state.Rows);
        Assert.Contains("Written on the other machine", row.RawText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The store runs in WAL journal mode, where an ordinary write lands in
    /// <c>backlog.db-wal</c> and leaves <c>backlog.db</c>'s own timestamp exactly
    /// where it was until a checkpoint — which does not happen per save. A check
    /// that watched the main file alone therefore never fired for a real write at
    /// all, and the second machine sat on a stale list indefinitely.
    /// <para>
    /// Everything but one sidecar is pinned back to the timestamp the baseline was
    /// taken at, so the only thing that moved is that sidecar. Both are covered:
    /// which of the two a given write shows up in first is SQLite's business, not
    /// something this list may assume.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("-wal")]
    [InlineData("-shm")]
    public async Task A_write_that_only_reaches_a_sidecar_is_still_seen(string sidecar)
    {
        var root = TempRoot();
        var database = Path.Combine(root, "backlog.db");

        using var state = State(root);
        await state.InitializeAsync();

        var baseline = File.GetLastWriteTimeUtc(database);

        // The other machine, writing to the same folder. The rows really are in
        // the database afterwards — reading the change was never the broken half.
        using var elsewhere = State(root);
        await elsewhere.InitializeAsync();
        await WriteEntryAsync(elsewhere, "# Written on the other machine\n`task` `!ready`\n");

        OnlyTheSidecarMoved(database, sidecar, baseline);

        await state.CheckForExternalChangesAsync();

        var row = Assert.Single(state.Rows);
        Assert.Contains("Written on the other machine", row.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_store_nobody_touched_is_not_read_again()
    {
        var root = TempRoot();
        var tags = new CountingRoadmapTags();

        using var state = State(root, roadmapTags: tags);
        await state.InitializeAsync();

        var readsAfterLoading = tags.Calls;

        await state.CheckForExternalChangesAsync();
        await state.CheckForExternalChangesAsync();

        Assert.Equal(readsAfterLoading, tags.Calls);
    }

    /// <summary>The first look has nothing to compare against. Recording the
    /// timestamp is all it may do — reloading because there was no previous value
    /// would make every fresh list reload itself for no reason.</summary>
    [Fact]
    public async Task The_first_look_records_rather_than_reloads()
    {
        var root = TempRoot();
        var tags = new CountingRoadmapTags();

        // No InitializeAsync, so nothing has read the store and there is no
        // baseline. The database is there, because the other machine's is.
        using var elsewhere = State(root);
        await elsewhere.InitializeAsync();
        await WriteEntryAsync(elsewhere, "# Already there\n`task` `!ready`\n");

        using var state = State(root, roadmapTags: tags);

        await state.CheckForExternalChangesAsync();

        Assert.Equal(0, tags.Calls);
        Assert.Empty(state.Rows);

        // And the tick after it, with the file unchanged, still does nothing.
        await state.CheckForExternalChangesAsync();

        Assert.Equal(0, tags.Calls);
    }

    /// <summary>A reload replaces every row object, so it must not happen under a
    /// live caret. The change is not dropped either: nothing is recorded, so the
    /// next tick after the editor closes still sees it.</summary>
    [Fact]
    public async Task A_change_that_arrives_mid_edit_waits_for_the_editor_to_close()
    {
        var root = TempRoot();

        using var state = State(root);
        await state.InitializeAsync();

        using var elsewhere = State(root);
        await elsewhere.InitializeAsync();
        await WriteEntryAsync(elsewhere, "# Written on the other machine\n`task` `!ready`\n");

        var draft = new EntryRow { RawText = "# Half typed\n" };
        state.Rows.Add(draft);
        state.BeginEdit(draft);

        TouchDatabase(root);

        await state.CheckForExternalChangesAsync();

        Assert.Contains(state.Rows, row => ReferenceEquals(row, draft));
        Assert.DoesNotContain(state.Rows, row => row.RawText.Contains("other machine", StringComparison.Ordinal));

        await state.EndEditAsync(draft);
        await state.CheckForExternalChangesAsync();

        Assert.Contains(state.Rows, row => row.RawText.Contains("other machine", StringComparison.Ordinal));
    }

    /// <summary>A store on a slow share can take longer to read than the interval
    /// between two checks. The second tick has to stand down rather than start a
    /// second reload on top of the first.</summary>
    [Fact]
    public async Task A_tick_that_lands_on_a_running_reload_stands_down()
    {
        var root = TempRoot();
        var tags = new GatedRoadmapTags();

        using var state = State(root, roadmapTags: tags);
        await state.InitializeAsync();

        tags.Close();
        TouchDatabase(root);

        var first = state.CheckForExternalChangesAsync();
        await tags.Entered;

        // The next tick, while the first reload is still inside the store.
        await state.CheckForExternalChangesAsync();

        tags.Open();
        await first;

        Assert.Equal(1, tags.CallsWhileClosed);
    }

    [Fact]
    public async Task The_check_does_not_run_at_all_while_it_is_switched_off()
    {
        var root = TempRoot();
        var settings = new BacklogRefreshSettingsStore(Path.Combine(root, "refresh.json"));
        settings.SetPollingEnabled(false);

        using var state = State(root, settings);
        await state.InitializeAsync();

        Assert.False(state.IsPollingForExternalChanges);
    }

    [Fact]
    public async Task Switching_the_check_on_and_off_takes_effect_without_a_restart()
    {
        var root = TempRoot();
        var settings = new BacklogRefreshSettingsStore(Path.Combine(root, "refresh.json"));

        using var state = State(root, settings);
        await state.InitializeAsync();

        Assert.True(state.IsPollingForExternalChanges);

        settings.SetPollingEnabled(false);
        Assert.False(state.IsPollingForExternalChanges);

        settings.SetPollingEnabled(true);
        Assert.True(state.IsPollingForExternalChanges);

        // Rescaling keeps the same timer rather than leaving a second one behind.
        settings.SetPollingIntervalSeconds(30);
        Assert.True(state.IsPollingForExternalChanges);
    }

    [Fact]
    public async Task Disposing_stops_the_check()
    {
        var root = TempRoot();
        var settings = new BacklogRefreshSettingsStore(Path.Combine(root, "refresh.json"));

        var state = State(root, settings);
        await state.InitializeAsync();

        Assert.True(state.IsPollingForExternalChanges);

        state.Dispose();

        Assert.False(state.IsPollingForExternalChanges);

        // And a setting changed afterwards does not bring it back.
        settings.SetPollingIntervalSeconds(20);

        Assert.False(state.IsPollingForExternalChanges);
    }

    /// <summary>Disposing the timer only stops ticks that have not started.
    /// A tick already on a thread pool thread when the workspace closes has to
    /// stand down by itself, which is what the lifetime token is for.</summary>
    [Fact]
    public async Task A_tick_that_starts_after_disposal_reloads_nothing()
    {
        var root = TempRoot();
        var tags = new CountingRoadmapTags();

        var state = State(root, roadmapTags: tags);
        await state.InitializeAsync();

        var reloadsWhileAlive = tags.Calls;
        TouchDatabase(root);

        state.Dispose();

        await state.CheckForExternalChangesAsync();

        Assert.Equal(reloadsWhileAlive, tags.Calls);
    }

    /// <summary>
    /// The other half of the same fact. A reload is a trip to the store, and the
    /// workspace can close while it is in flight — so the tick asks again on the
    /// way out rather than only on the way in.
    /// <para>
    /// <c>Changed</c> is what is asserted because it is the part that leaves this
    /// class: it is how the pane is told to render, and a render into a circuit
    /// that has been torn down is the failure this guards.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_reload_disposed_before_it_finishes_does_not_announce_itself()
    {
        var root = TempRoot();
        var tags = new GatedRoadmapTags();

        var state = State(root, roadmapTags: tags);
        await state.InitializeAsync();

        var changed = 0;
        state.Changed += () => Interlocked.Increment(ref changed);

        tags.Close();
        TouchDatabase(root);

        var tick = state.CheckForExternalChangesAsync();

        // The tick is inside the store now, past every guard on the way in.
        await tags.Entered;

        state.Dispose();

        tags.Open();
        await tick;

        Assert.Equal(0, Volatile.Read(ref changed));
    }

    /// <summary>A host that wires no refresh settings has said nothing about
    /// polling, and a list that started a timer anyway would be deciding for
    /// it.</summary>
    [Fact]
    public async Task A_list_nobody_gave_the_setting_to_never_polls()
    {
        var root = TempRoot();

        using var state = State(root);
        await state.InitializeAsync();

        Assert.False(state.IsPollingForExternalChanges);
    }

    private static async Task WriteEntryAsync(BacklogDesktopState state, string text)
    {
        var row = new EntryRow { RawText = text };
        state.Rows.Add(row);
        state.BeginEdit(row);
        await state.EndEditAsync(row);
    }

    /// <summary>What a synced folder does when the other machine's copy arrives:
    /// the bytes are already there, and the timestamp is what says so. Stamped a
    /// second into the future because a file written moments ago can otherwise
    /// carry the timestamp it already had.</summary>
    private static void TouchDatabase(string root)
    {
        var database = Path.Combine(root, "backlog.db");
        File.SetLastWriteTimeUtc(database, File.GetLastWriteTimeUtc(database).AddSeconds(1));
    }

    /// <summary>Replays a WAL-mode write's effect on the filesystem: the named
    /// sidecar moves forward, and the database file and the other sidecar are put
    /// back where the baseline read found them. Nothing about the bytes changes —
    /// only which file's timestamp says a write happened.</summary>
    private static void OnlyTheSidecarMoved(string database, string sidecar, DateTime baseline)
    {
        var moved = database + sidecar;
        var other = database + (sidecar == "-wal" ? "-shm" : "-wal");

        Assert.True(
            File.Exists(moved),
            $"The store is expected to keep {Path.GetFileName(moved)} alongside its database in WAL mode.");

        File.SetLastWriteTimeUtc(database, baseline);
        if (File.Exists(other)) File.SetLastWriteTimeUtc(other, baseline);

        // A second ahead, because a file written moments ago can otherwise carry
        // the timestamp it already had.
        File.SetLastWriteTimeUtc(moved, baseline.AddSeconds(1));
    }

    private string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-refresh-poll", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);
        return root;
    }

    private static BacklogDesktopState State(
        string root,
        IBacklogRefreshSettings? refreshSettings = null,
        IRoadmapTagSource? roadmapTags = null)
    {
        var store = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github.json"));

        return BacklogTestHost.StateFor(
            store,
            new GitHubIntegration(gitHub, new StubGitHubClient(), new StubProbe()),
            copilot: null,
            roadmapTags: roadmapTags,
            refreshSettings: refreshSettings);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Counts reloads. Every reload asks the plan for its tags first, so
    /// this is the cheapest honest answer to "did the list start over".</summary>
    private class CountingRoadmapTags : IRoadmapTagSource
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public virtual Task<IReadOnlyList<string>> TagsInUseAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }

    /// <summary>A reload that can be held open, so a second tick can be taken
    /// while the first one is still inside the store.</summary>
    private sealed class GatedRoadmapTags : CountingRoadmapTags
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource? _gate;
        private int _callsWhileClosed;

        public Task Entered => _entered.Task;

        public int CallsWhileClosed => Volatile.Read(ref _callsWhileClosed);

        public void Close() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() => _gate?.TrySetResult();

        public override async Task<IReadOnlyList<string>> TagsInUseAsync(CancellationToken cancellationToken = default)
        {
            var gate = _gate;
            if (gate is not null)
            {
                Interlocked.Increment(ref _callsWhileClosed);
                _entered.TrySetResult();
                await gate.Task;
            }

            return await base.TagsInUseAsync(cancellationToken);
        }
    }

    private sealed class StubGitHubClient : IGitHubClient
    {
        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(
            GitHubRepositoryRef repository,
            string path,
            string branch,
            byte[] content,
            string commitMessage,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }
}
