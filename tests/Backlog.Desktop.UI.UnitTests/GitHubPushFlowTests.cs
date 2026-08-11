using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.DomainModels;
using Backlog.Infrastructure.GitHub;
using Backlog.Desktop.UI.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Pushing an entry and watching what happens to it, driven through the list
/// exactly as the screen does — with GitHub itself stubbed out, so the test is
/// about the wiring rather than the network.
/// </summary>
[Collection(BacklogStoreCollection.Name)]
public sealed class GitHubPushFlowTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task An_entry_filed_under_a_configured_repository_can_be_pushed()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `@backlog`\n\nDetails here.");

        Assert.NotNull(harness.State.RepositoryFor(row));

        await harness.State.PushToGitHubAsync(row);

        Assert.Null(row.GitHubError);
        Assert.Equal(101, row.IssueLink!.IssueNumber);
        Assert.Equal("JSdotNet/Backlog", row.IssueLink.RepoFullName);
        Assert.Equal("Add GitHub support", harness.Client.CreatedTitle);
        Assert.Contains("Details here.", harness.Client.CreatedBody);
    }

    [Fact]
    public async Task An_area_that_names_no_repository_uses_the_primary_repository()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Buy milk\n`task` `*low` `!draft` `@errands`\n");

        Assert.Equal("JSdotNet/Backlog", harness.State.RepositoryFor(row)!.FullName);

        await harness.State.PushToGitHubAsync(row);

        Assert.Equal("JSdotNet/Backlog", row.IssueLink!.RepoFullName);
    }

    [Fact]
    public async Task The_link_survives_a_reload_of_the_markdown_file()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `@backlog`\n");
        await harness.State.PushToGitHubAsync(row);

        var reloaded = new BacklogDesktopState(harness.Store, harness.Integration);
        await reloaded.InitializeAsync();

        var reloadedRow = Assert.Single(reloaded.Rows);
        Assert.Equal(101, reloadedRow.IssueLink!.IssueNumber);
    }

    [Fact]
    public async Task An_entry_is_never_pushed_twice()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `@backlog`\n");
        await harness.State.PushToGitHubAsync(row);
        harness.Client.CreateCount = 0;

        await harness.State.PushToGitHubAsync(row);

        Assert.Equal(0, harness.Client.CreateCount);
    }

    [Fact]
    public async Task A_refusal_from_github_is_reported_on_the_entry_not_swallowed()
    {
        var harness = Build("JSdotNet/Backlog");
        harness.Client.Failure = new GitHubException("GitHub rejected the token — check it hasn't expired.");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `@backlog`\n");
        await harness.State.PushToGitHubAsync(row);

        Assert.Null(row.IssueLink);
        Assert.Equal("GitHub rejected the token — check it hasn't expired.", row.GitHubError);
    }

    [Fact]
    public async Task Syncing_reads_back_the_issue_and_its_pull_request()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `@backlog`\n");
        await harness.State.PushToGitHubAsync(row);

        harness.Client.Snapshot = new GitHubIssueSnapshot(
            new GitHubIssue(101, "https://github.com/JSdotNet/Backlog/issues/101", "Add GitHub support", GitHubItemState.Closed, null),
            [new GitHubPullRequest(102, "https://github.com/JSdotNet/Backlog/pull/102", "Add GitHub support", GitHubItemState.Merged, "JSdotNet/Backlog")],
            DateTimeOffset.UtcNow);

        await harness.State.SyncGitHubAsync();

        Assert.Equal(GitHubItemState.Closed, row.Snapshot!.Issue.State);
        Assert.Equal(GitHubItemState.Merged, row.Snapshot.Headline!.State);
    }

    [Fact]
    public async Task Monitoring_still_works_for_a_repository_since_removed_from_settings()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `@backlog`\n");
        await harness.State.PushToGitHubAsync(row);

        harness.Integration.Settings.SetRepositories([]);

        await harness.State.RefreshGitHubAsync(row);

        Assert.Null(row.GitHubError);
        Assert.NotNull(row.Snapshot);
    }


    [Fact]
    public async Task Editing_a_sub_item_updates_only_that_chapter()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await WriteEntryAsync(harness.State,
            "# Parent\n" +
            "`task` `*medium` `!draft` `@backlog`\n\n" +
            "## First\n" +
            "Keep this.\n\n" +
            "### Target\n" +
            "Old target notes.\n\n" +
            "## Last\n" +
            "Keep last.\n");

        harness.State.BeginSubItemEdit(row, 1);
        harness.State.OnSubItemRawTextInput(row, 1, "### Updated target\nNew target notes.");
        await harness.State.EndSubItemEditAsync(row, 1);

        Assert.False(harness.State.IsEditingSubItem(row, 1));
        Assert.Contains("## First\nKeep this.", row.RawText);
        Assert.Contains("### Updated target\nNew target notes.", row.RawText);
        Assert.DoesNotContain("Old target notes.", row.RawText);
        Assert.Contains("## Last\nKeep last.", row.RawText);
    }

    [Fact]
    public async Task A_sub_item_push_uses_the_parent_repository()
    {
        var harness = Build("backlog = JSdotNet/Backlog", "other = someone/else");
        var row = await WriteEntryAsync(harness.State,
            "# Parent\n" +
            "`task` `*medium` `!draft` `@backlog`\n\n" +
            "## Child\n" +
            "`task` `*high` `!ready` `@other` `#child`\n" +
            "Child notes.\n");

        await harness.State.PushSubItemToGitHubAsync(row, 0);

        Assert.Null(row.GitHubError);
        Assert.Equal("JSdotNet/Backlog", harness.Client.CreatedRepository);
        Assert.Equal("Child", harness.Client.CreatedTitle);
        Assert.Contains("From backlog entry: Parent", harness.Client.CreatedBody);
        Assert.Contains("Child notes.", harness.Client.CreatedBody);
        Assert.Equal(["child"], harness.Client.CreatedLabels);
    }

    [Fact]
    public void Nothing_about_github_shows_until_a_repository_is_configured()
    {
        Assert.False(Build().State.GitHubConfigured);
    }

    [Fact]
    public async Task Feedback_reports_create_an_issue_in_the_backlog_repository_with_the_screenshot()
    {
        var harness = Build("someone/else");
        var screenshot = new GitHubFeedbackScreenshot(
            "data:image/jpeg;base64,abc123",
            "image/jpeg",
            800,
            600,
            42);

        var link = await harness.Integration.ReportFeedbackAsync("Broken view", "The pane is blank.", "backlog list", screenshot);

        Assert.Equal("JSdotNet/Backlog", harness.Client.CreatedRepository);
        Assert.Equal("[Feedback][Desktop app] Broken view", harness.Client.CreatedTitle);
        Assert.Contains("## Desktop app screen area", harness.Client.CreatedBody);
        Assert.Contains("backlog list", harness.Client.CreatedBody);
        Assert.Contains("The pane is blank.", harness.Client.CreatedBody);
        Assert.Contains("![Screenshot](data:image/jpeg;base64,abc123)", harness.Client.CreatedBody);
        Assert.Equal("JSdotNet/Backlog", link.RepoFullName);
    }

    [Fact]
    public async Task Feedback_reports_include_the_screenshot_failure_when_capture_fails()
    {
        var harness = Build("JSdotNet/Backlog");

        await harness.Integration.ReportFeedbackAsync("Cannot capture", null, null, null, "Permission denied.");

        Assert.Equal("JSdotNet/Backlog", harness.Client.CreatedRepository);
        Assert.Contains("_No details provided._", harness.Client.CreatedBody);
        Assert.Contains("Screenshot capture failed: Permission denied.", harness.Client.CreatedBody);
    }

    private async Task<EntryRow> WriteEntryAsync(BacklogDesktopState state, string text)
    {
        state.NewRow();
        var row = state.Rows[^1];
        state.OnRawTextInput(row, text);
        await state.EndEditAsync(row);
        return row;
    }

    private Harness Build(params string[] repositories)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-github-flow", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);

        var store = new BacklogStore(root, Path.Combine(root, "settings.json"));
        Assert.Null(store.TryUseRoot(root));

        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var (parsed, _) = GitHubSettings.ParseText(string.Join('\n', repositories));
        settings.SetRepositories(parsed);

        var client = new FakeGitHubClient();
        var integration = new GitHubIntegration(settings, client, new FakeProbe());

        return new Harness(new BacklogDesktopState(store, integration), client, store, integration);
    }

    private sealed record Harness(
        BacklogDesktopState State,
        FakeGitHubClient Client,
        BacklogStore Store,
        GitHubIntegration Integration);

    private sealed class FakeGitHubClient : IGitHubClient
    {
        public int CreateCount { get; set; }
        public string? CreatedRepository { get; private set; }
        public string? CreatedTitle { get; private set; }
        public string? CreatedBody { get; private set; }
        public IReadOnlyList<string> CreatedLabels { get; private set; } = [];
        public Exception? Failure { get; set; }

        public GitHubIssueSnapshot Snapshot { get; set; } = new(
            new GitHubIssue(101, "https://github.com/JSdotNet/Backlog/issues/101", "Add GitHub support", GitHubItemState.Open, null),
            [],
            DateTimeOffset.UtcNow);

        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;

            CreateCount++;
            CreatedRepository = repository.FullName;
            CreatedTitle = title;
            CreatedBody = body ?? string.Empty;
            CreatedLabels = labels?.ToList() ?? [];

            return Task.FromResult(new GitHubIssue(
                101,
                $"https://github.com/{repository.FullName}/issues/101",
                title,
                GitHubItemState.Open,
                DateTimeOffset.UtcNow));
        }

        public Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;
            return Task.FromResult(Snapshot);
        }
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
