using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The list hearing task writes it did not make — and only those.
/// <para>
/// One repository raising one <see cref="ITaskChangeSignal"/>, and for most cases
/// one set of use cases that both the list and the out-of-band writer hold. A
/// write from a second use-case graph would pass whether or not the list could
/// tell its own writes apart, because the question only exists when both go
/// through the same object. The two-window case is the other arrangement: a
/// use-case graph per window, as the hosts' scoped window state composes it
/// (that composition itself is <c>MauiDesktopCompositionTests</c>).
/// </para>
/// <para>
/// The shell's half — reloading on the dispatcher — is
/// <c>HomeInboxWiringTests.A_task_written_outside_the_pane_reloads_the_tasks_pane</c>.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksWrittenElsewhereTests : IDisposable
{
    private readonly List<string> _tempDirs = [];
    private readonly List<TasksDesktopState> _states = [];

    [Fact]
    public async Task A_write_made_outside_the_list_is_announced_and_the_reload_shows_it()
    {
        var (state, entries, _) = await BuildAsync();
        var row = await WriteEntryAsync(state, "# Moved along by an agent\n`task` `!ready`\n");

        var announced = 0;
        state.WrittenElsewhere += () => Interlocked.Increment(ref announced);

        // Off the list's thread and outside its flow, as a request to the MCP
        // endpoint is.
        var moved = await Task.Run(() => entries.SaveFromTextAsync(
            row.Id, "# Moved along by an agent\n`task` `!done`\n", 0));
        Assert.True(moved.IsSuccess);

        Assert.Equal(1, Volatile.Read(ref announced));

        var changed = 0;
        state.Changed += () => changed++;
        await state.ReloadFromStoreAsync();

        Assert.Equal(EntryStatus.Done, Assert.Single(state.Rows).Status);
        Assert.Equal(1, changed);
    }

    /// <summary>
    /// Every way the list writes — an editor closing, a status control, a
    /// keystroke's debounced save on a timer thread — raises the signal, because
    /// sync has to hear them, and none of them is announced back to the list: it
    /// already holds the row it saved, and a reload would replace every row under
    /// the reader for nothing.
    /// </summary>
    [Fact]
    public async Task The_lists_own_writes_are_not_announced_back_to_it()
    {
        var (state, entries, signal) = await BuildAsync();

        var raised = 0;
        signal.Changed += () => Interlocked.Increment(ref raised);
        var announced = 0;
        state.WrittenElsewhere += () => Interlocked.Increment(ref announced);

        var row = await WriteEntryAsync(state, "# Written in the list\n`task` `!ready`\n");
        await state.ChangeStatusAsync(row, EntryStatus.InProgress);

        state.OnRawTextInput(row, "# Written in the list, then retitled\n`task` `!in-progress`\n");
        await Polling.WaitUntilAsync(
            async () => (await entries.ListAsync()).Single().Title == "Written in the list, then retitled",
            () => "The debounced save never landed.");

        Assert.True(Volatile.Read(ref raised) >= 3, $"The signal was raised {raised} time(s); sync would not have heard the list.");
        Assert.Equal(0, Volatile.Read(ref announced));
    }

    [Fact]
    public async Task A_disposed_list_stops_listening()
    {
        var (state, entries, _) = await BuildAsync();

        var announced = 0;
        state.WrittenElsewhere += () => announced++;

        state.Dispose();

        var saved = await entries.SaveFromTextAsync(null, "# Written after the list closed\n`task`\n", 0, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(saved.IsSuccess);

        Assert.Equal(0, announced);
    }

    /// <summary>
    /// Two windows, each with its own list (local ADR 0042, §5): window state is
    /// scoped, so each window composes its own <see cref="TasksDesktopState"/> over
    /// its own use cases, and the one process-wide <see cref="ITaskChangeSignal"/>
    /// is what carries a write from one to the other. A save in either list is
    /// announced to the other one only, and the other's reload shows it.
    /// </summary>
    [Fact]
    public async Task Two_windows_lists_reload_for_each_others_writes_through_the_task_signal()
    {
        var (first, second) = await BuildTwoWindowsAsync();

        var firstAnnounced = 0;
        var secondAnnounced = 0;
        first.WrittenElsewhere += () => Interlocked.Increment(ref firstAnnounced);
        second.WrittenElsewhere += () => Interlocked.Increment(ref secondAnnounced);

        var row = await WriteEntryAsync(first, "# Written in the first window\n`task` `!ready`\n");

        Assert.True(Volatile.Read(ref secondAnnounced) >= 1, "The second window never heard the first window's write.");
        Assert.Equal(0, Volatile.Read(ref firstAnnounced));

        await second.ReloadFromStoreAsync();
        var seen = Assert.Single(second.Rows);
        Assert.Equal(row.Id, seen.Id);
        Assert.StartsWith("# Written in the first window", seen.RawText, StringComparison.Ordinal);

        var secondBefore = Volatile.Read(ref secondAnnounced);
        await second.ChangeStatusAsync(seen, EntryStatus.Done);

        Assert.True(Volatile.Read(ref firstAnnounced) >= 1, "The first window never heard the second window's write.");
        Assert.Equal(secondBefore, Volatile.Read(ref secondAnnounced));

        await first.ReloadFromStoreAsync();
        Assert.Equal(EntryStatus.Done, Assert.Single(first.Rows).Status);
    }

    // --- Composition --------------------------------------------------------

    /// <summary>Two windows' lists in the shape a head with scoped window state gives
    /// them, built by hand: one workspace, one repository and one change signal for the
    /// process, and a list and use cases per window.</summary>
    private async Task<(TasksDesktopState First, TasksDesktopState Second)> BuildTwoWindowsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-written-elsewhere", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);

        var store = new WorkspaceSettingsStore(Path.Combine(root, "settings"));
        Assert.Null(store.TryUseRoot(root));

        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var integration = new GitHubIntegration(settings, new UnusedGitHubClient(), new UnusedProbe());

        var signal = new TaskChangeSignal();
        var repository = TasksTestHost.RepositoryFor(store, signal);
        var taskStore = TasksTestHost.TaskStoreFor(store);

        TasksDesktopState Window() => new(
            taskStore,
            TasksTestHost.EntriesFor(repository),
            integration,
            taskWrites: signal);

        var first = Window();
        var second = Window();
        _states.Add(first);
        _states.Add(second);
        await first.InitializeAsync();
        await second.InitializeAsync();

        return (first, second);
    }

    private async Task<(TasksDesktopState State, ITaskItems Entries, TaskChangeSignal Signal)> BuildAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-written-elsewhere", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);

        var store = new WorkspaceSettingsStore(Path.Combine(root, "settings"));
        Assert.Null(store.TryUseRoot(root));

        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var integration = new GitHubIntegration(settings, new UnusedGitHubClient(), new UnusedProbe());

        var signal = new TaskChangeSignal();
        var entries = TasksTestHost.EntriesFor(TasksTestHost.RepositoryFor(store, signal));
        var state = new TasksDesktopState(
            TasksTestHost.TaskStoreFor(store),
            entries,
            integration,
            taskWrites: signal);

        _states.Add(state);
        await state.InitializeAsync();

        return (state, entries, signal);
    }

    private static async Task<EntryRow> WriteEntryAsync(TasksDesktopState state, string text)
    {
        state.NewRow();
        var row = state.Rows[^1];
        state.OnRawTextInput(row, text);
        await state.EndEditAsync(row);
        return row;
    }

    public void Dispose()
    {
        foreach (var state in _states)
        {
            state.Dispose();
        }

        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            TestDatabases.Release(dir);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Nothing here goes near GitHub; the integration only exists because
    /// the state takes one.</summary>
    private sealed class UnusedGitHubClient : IGitHubClient
    {
        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not configured."));

        public void Invalidate()
        {
        }
    }
}
