using Backlog.Desktop.UI.Services;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Backlog.DomainModels;

namespace Backlog.Desktop.UI.UnitTests;

[Collection(BacklogStoreCollection.Name)]
public sealed class CopilotCliIntegrationTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        new BacklogStore().ResetToDefault();

        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task An_entry_launches_copilot_with_its_markdown_as_the_prompt()
    {
        var launcher = new FakeCopilotCliLauncher();
        var integration = new CopilotCliIntegration(launcher);
        var entry = new BacklogEntry("Add Copilot CLI support", "Trigger it from backlog items.", EntryType.Task);
        entry.SetArea("backlog");

        await integration.StartFromEntryAsync(entry, "D:\\Backlog");

        Assert.Equal("D:\\Backlog", launcher.Request!.WorkingDirectory);
        Assert.Contains("Work on this Backlog item with GitHub Copilot CLI.", launcher.Request.Prompt);
        Assert.Contains("# Add Copilot CLI support", launcher.Request.Prompt);
        Assert.Contains("`@backlog`", launcher.Request.Prompt);
        Assert.Contains("Trigger it from backlog items.", launcher.Request.Prompt);
        Assert.Equal(CopilotCliIntegration.UsageAction, Assert.Single(entry.UsageEvents).Action);
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

        var reloaded = await harness.Store.Repository.GetAsync(row.Id!.Value);
        Assert.Equal(CopilotCliIntegration.UsageAction, Assert.Single(reloaded!.UsageEvents).Action);
    }

    private async Task<EntryRow> WriteEntryAsync(BacklogDesktopState state, string text)
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

        var store = new BacklogStore();
        Assert.Null(store.TryUseRoot(root));

        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var integration = new GitHubIntegration(settings, new FakeGitHubClient(), new FakeProbe());
        var copilot = new CopilotCliIntegration(launcher);

        return new Harness(new BacklogDesktopState(store, integration, copilot), store);
    }

    private sealed record Harness(BacklogDesktopState State, BacklogStore Store);

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
