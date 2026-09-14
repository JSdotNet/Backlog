using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A draft added from outside the list — the Inbox's Add — lands in the store
/// the way a typed one does, without the list opening on it.
/// <para>
/// <c>NewRow</c> is the other way to get a draft and it is deliberately not
/// this one: it appends an unsaved row, selects it and points the caret at its
/// title, which is right for somebody about to type and wrong for somebody who
/// has just finished typing in a dialog.
/// </para>
/// </summary>
public sealed class TasksDesktopStateAddDraftTests : IDisposable
{
    private readonly List<string> _tempDirs = [];
    private readonly List<TasksDesktopState> _states = [];

    [Fact]
    public async Task A_titled_draft_is_saved_and_the_rows_are_reloaded()
    {
        var state = Build();
        await state.InitializeAsync();

        var result = await state.AddDraftAsync("Ask about the trial length", "Before Friday.");

        Assert.True(result.IsSuccess);

        var row = Assert.Single(state.Rows);
        Assert.Equal("Ask about the trial length", row.PreviewTitle);
        Assert.Equal(EntryStatus.Draft, row.PreviewStatus);
        Assert.NotNull(row.Id);
        Assert.Contains("Before Friday.", row.RawText, StringComparison.Ordinal);
        Assert.Equal(AppSaveState.Saved, state.SaveState);
    }

    [Fact]
    public async Task Adding_does_not_select_the_row()
    {
        var state = Build();
        await state.InitializeAsync();

        _ = await state.AddDraftAsync("Ask about the trial length");

        Assert.Null(state.SelectedRow);
        Assert.Null(state.EditingRow);
    }

    [Fact]
    public async Task Notes_are_optional()
    {
        var state = Build();
        await state.InitializeAsync();

        var result = await state.AddDraftAsync("Just a title", null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Just a title", Assert.Single(state.Rows).PreviewTitle);
    }

    [Fact]
    public async Task A_blank_title_is_refused_and_nothing_is_saved()
    {
        var state = Build();
        await state.InitializeAsync();

        var result = await state.AddDraftAsync("   ", "Some notes");

        Assert.True(result.IsFailure);
        Assert.Equal("entry.title_required", result.Error.Code);
        Assert.Empty(state.Rows);
        Assert.Equal(AppSaveState.Idle, state.SaveState);
    }

    [Fact]
    public async Task Adding_tells_whoever_is_showing_the_list()
    {
        var state = Build();
        await state.InitializeAsync();

        var raised = 0;
        state.Changed += () => raised++;

        _ = await state.AddDraftAsync("Ask about the trial length");

        Assert.True(raised > 0, "The Inbox reads the rows, so it has to be told they changed.");
    }

    private TasksDesktopState Build()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-add-draft", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);

        var store = new WorkspaceSettingsStore(Path.Combine(root, "settings"));
        Assert.Null(store.TryUseRoot(root));

        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var integration = new GitHubIntegration(settings, new UnusedGitHubClient(), new UnusedProbe());

        var state = TasksTestHost.StateFor(store, integration);
        _states.Add(state);
        return state;
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

    private sealed class UnusedGitHubClient : IGitHubClient
    {
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
