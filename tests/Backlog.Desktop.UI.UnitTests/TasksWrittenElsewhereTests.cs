using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The list hearing task writes it did not make — and only those.
/// <para>
/// Composed the way the hosts compose it: one repository raising one
/// <see cref="ITaskChangeSignal"/>, and one set of use cases that both the list
/// and the out-of-band writer hold, since the MCP server's tools and the pane
/// share the singleton. A write from a second use-case graph would pass whether
/// or not the list could tell its own writes apart, because the question only
/// exists when both go through the same object.
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
        await WaitUntilAsync(async () =>
            (await entries.ListAsync()).Single().Title == "Written in the list, then retitled");

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

    // --- Composition --------------------------------------------------------

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

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The debounced save never landed.");
            await Task.Delay(50);
        }
    }

    public void Dispose()
    {
        foreach (var state in _states)
        {
            state.Dispose();
        }

        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
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
