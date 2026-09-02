using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Desktop.UI.UnitTests;

[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksCopilotCliTests : IDisposable
{
    private readonly List<string> _tempDirs = [];
    private readonly List<TasksDesktopState> _states = [];

    public void Dispose()
    {
        // Before the folders below go: the state arms timed saves, and one that
        // elapsed after its folder was deleted is work the test host is still
        // holding when the run is over. See TasksDesktopStateLifetimeTests.
        foreach (var state in _states)
        {
            state.Dispose();
        }

        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task An_entry_launches_copilot_with_its_markdown_as_the_prompt()
    {
        var launcher = new FakeCopilotCliLauncher();
        var integration = new TasksCopilotCli(launcher);
        const string entryText = """
        # Add Copilot CLI support
        `task` `*medium` `!draft` `@backlog`

        Trigger it from backlog items.
        """;

        await integration.StartFromEntryAsync(entryText, "D:\\Backlog");

        Assert.Equal("D:\\Backlog", launcher.Request!.WorkingDirectory);
        Assert.Contains("Work on this Backlog item with GitHub Copilot CLI.", launcher.Request.Prompt);
        Assert.Contains("# Add Copilot CLI support", launcher.Request.Prompt);
        Assert.Contains("`@backlog`", launcher.Request.Prompt);
        Assert.Contains("Trigger it from backlog items.", launcher.Request.Prompt);
    }

    [Fact]
    public async Task A_cli_launch_failure_is_reported_on_the_row()
    {
        var harness = Build(new FailingCopilotCliLauncher("Copilot is not installed."));
        var row = await WriteEntryAsync(harness.State, "# Add Copilot CLI support\n`task` `*medium` `!draft` `@backlog`\n");

        await harness.State.StartCopilotCliAsync(row);

        Assert.Equal("Copilot is not installed.", row.CopilotError);
    }

    [Fact]
    public async Task Starting_copilot_records_usage_on_the_saved_entry()
    {
        var launcher = new FakeCopilotCliLauncher();
        var harness = Build(launcher);
        var row = await WriteEntryAsync(harness.State, "# Add Copilot CLI support\n`task` `*medium` `!draft` `@backlog`\n");

        await harness.State.StartCopilotCliAsync(row);

        Assert.Null(row.CopilotError);
        Assert.NotNull(launcher.Request);

        var reloaded = await TasksTestHost.RepositoryFor(harness.Store).GetAsync(row.Id!.Value);
        Assert.Equal(TasksCopilotCli.UsageAction, Assert.Single(reloaded!.UsageEvents).Action);
    }

    private async Task<EntryRow> WriteEntryAsync(TasksDesktopState state, string text)
    {
        state.NewRow();
        var row = state.Rows[^1];
        state.OnRawTextInput(row, text);
        await state.EndEditAsync(row);
        return row;
    }

    private Harness Build(ICopilotCliLauncher launcher)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-copilot-flow", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);

        var store = new WorkspaceSettingsStore(Path.Combine(root, "settings"));
        Assert.Null(store.TryUseRoot(root));

        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var integration = new GitHubIntegration(settings, new FakeGitHubClient(), new FakeProbe());
        var copilot = new TasksCopilotCli(launcher);

        var state = TasksTestHost.StateFor(store, integration, copilot);
        _states.Add(state);

        return new Harness(state, store);
    }

    private sealed record Harness(TasksDesktopState State, WorkspaceSettingsStore Store);

    private sealed class FakeCopilotCliLauncher : ICopilotCliLauncher
    {
        public CopilotCliRequest? Request { get; private set; }

        public Task LaunchAsync(CopilotCliRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingCopilotCliLauncher(string message) : ICopilotCliLauncher
    {
        public Task LaunchAsync(CopilotCliRequest request, CancellationToken cancellationToken = default) =>
            throw new CopilotCliException(message);
    }

    private sealed class FakeGitHubClient : IGitHubClient
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

    private sealed class FakeProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(true, "Connected."));

        public void Invalidate()
        {
        }
    }
}
