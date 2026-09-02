using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the list state leaves running once nobody is looking at it any more.
/// <para>
/// The state schedules two kinds of work that outlive the gesture that started
/// it: a 750 ms debounced save per row, and a 900 ms "just saved" flash. Both
/// are timed, both call back into <c>Changed</c>, and both used to keep going
/// after the state was thrown away — a save writing into a folder the test had
/// already deleted, and a flash resuming inside a renderer that was gone. In an
/// app that is a disposed-object exception nobody sees; in the test host it is
/// work still queued on the runner's own threads when the assembly has finished,
/// which is what turned a fully green desktop UI run red at random (issue #211).
/// </para>
/// <para>
/// So these tests assert the negative: after disposal, nothing more happens.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksDesktopStateLifetimeTests : IDisposable
{
    private readonly List<string> _tempDirs = [];
    private readonly List<TasksDesktopState> _states = [];

    /// <summary>
    /// Typing arms a debounce. Closing the pane before it elapses has to
    /// disarm it — and the pane is what owns the state, so the pane's own
    /// disposal is the trigger, not a separate call a test has to remember.
    /// </summary>
    [Fact]
    public async Task Disposing_the_pane_host_disarms_a_debounced_save()
    {
        var host = await TasksPaneHost.CreateAsync();
        var changes = 0;
        host.State.Changed += () => changes++;

        host.State.NewRow();
        var row = host.State.Rows[^1];
        host.State.OnRawTextInput(row, "# Typed and abandoned\n`task` `!draft`\n");

        host.Dispose();
        var afterDisposal = changes;

        await Task.Delay(1500);

        Assert.Null(row.Id);
        Assert.Equal(afterDisposal, changes);
    }

    /// <summary>
    /// The save flash is a 900 ms delay with a render on either side of it. A
    /// state that has been disposed still had that delay in flight, and it used
    /// to come back and re-render regardless.
    /// </summary>
    [Fact]
    public async Task Disposing_the_state_cancels_the_save_flash()
    {
        var state = State(TempRoot());
        await state.InitializeAsync();

        var row = await WriteEntryAsync(state, "# Saved and dropped\n`task` `!draft`\n");
        Assert.True(row.JustSaved);

        var changes = 0;
        state.Changed += () => changes++;

        state.Dispose();

        await Task.Delay(1500);

        Assert.Equal(0, changes);
    }

    /// <summary>
    /// A debounce disposed as it comes due must not save: the store it would
    /// write to belongs to a workspace the owner has finished with. Disposing the
    /// timer alone does not settle this — the callback may already be scheduled —
    /// so the callback has to be told the state is gone.
    /// </summary>
    [Fact]
    public async Task A_debounce_disposed_as_it_comes_due_does_not_save()
    {
        var state = State(TempRoot());
        await state.InitializeAsync();

        state.NewRow();
        var row = state.Rows[^1];
        state.OnRawTextInput(row, "# Elapsing right now\n`task` `!draft`\n");

        // Right up against the 750 ms, so the disposal and the callback are
        // racing rather than comfortably ordered.
        await Task.Delay(700);
        state.Dispose();

        await Task.Delay(1500);

        Assert.Null(row.Id);
    }

    /// <summary>
    /// The timer map is written by the typing thread and read-modified by every
    /// timer callback that fires. Typing across the debounce boundary puts both
    /// on it at once, and an unsynchronised <c>Dictionary</c> there does not only
    /// throw — it can drop the entry for a timer that was just armed, leaving a
    /// timer nothing can cancel and a save that lands after the text moved on.
    /// </summary>
    [Fact]
    public async Task Arming_debounces_across_the_boundary_keeps_the_timer_map_intact()
    {
        var state = State(TempRoot());
        await state.InitializeAsync();

        var rows = new List<EntryRow>();
        for (var index = 0; index < 32; index++)
        {
            state.NewRow();
            rows.Add(state.Rows[^1]);
        }

        // Re-arm every row continuously for well past the 750 ms debounce, so
        // the arming runs alongside the callbacks the earlier arms scheduled.
        var deadline = DateTime.UtcNow.AddMilliseconds(1600);
        var revision = 0;
        while (DateTime.UtcNow < deadline)
        {
            revision++;
            foreach (var row in rows)
            {
                state.OnRawTextInput(row, $"# Row {rows.IndexOf(row)} revision {revision}\n`task` `!draft`\n");
            }
        }

        // Every row is then flushed by hand, which is the gesture that has to be
        // able to cancel whatever the last arm left behind.
        foreach (var row in rows)
        {
            await state.EndEditAsync(row);
        }

        await Task.Delay(1200);

        foreach (var row in rows)
        {
            Assert.Contains($"revision {revision}", row.RawText, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The Home screens hand the state to bUnit as a scoped service rather than
    /// disposing it themselves, so the claim that the context's disposal reaches
    /// it is worth stating as a test rather than assuming.
    /// </summary>
    [Fact]
    public async Task A_state_registered_with_the_bunit_container_is_disposed_with_the_context()
    {
        var state = State(TempRoot());
        await state.InitializeAsync();

        var context = new BunitContext();
        context.Services.AddScoped(_ => state);
        Assert.Same(state, context.Services.GetRequiredService<TasksDesktopState>());

        var changes = 0;
        state.Changed += () => changes++;

        state.NewRow();
        var row = state.Rows[^1];
        state.OnRawTextInput(row, "# Left to the container\n`task` `!draft`\n");

        context.Dispose();
        var afterDisposal = changes;

        await Task.Delay(1500);

        Assert.Null(row.Id);
        Assert.Equal(afterDisposal, changes);
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

    private static async Task<EntryRow> WriteEntryAsync(TasksDesktopState state, string text)
    {
        state.NewRow();
        var row = state.Rows[^1];
        state.OnRawTextInput(row, text);
        await state.EndEditAsync(row);
        return row;
    }

    private string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-state-lifetime", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);
        return root;
    }

    private TasksDesktopState State(string root)
    {
        var store = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
        Assert.Null(store.TryUseRoot(Path.Combine(root, "local")));

        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var state = TasksTestHost.StateFor(store, new GitHubIntegration(settings, new StubGitHubClient(), new StubProbe()));
        _states.Add(state);
        return state;
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
            Task.FromResult(new GitHubConnection(false, "Not configured."));

        public void Invalidate()
        {
        }
    }
}
