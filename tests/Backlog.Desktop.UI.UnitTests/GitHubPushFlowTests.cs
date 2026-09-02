using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Pushing an entry and watching what happens to it, driven through the list
/// exactly as the screen does — with GitHub itself stubbed out, so the test is
/// about the wiring rather than the network.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class GitHubPushFlowTests : IDisposable
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
    public async Task An_area_that_names_no_repository_does_not_push_silently()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Buy milk\n`task` `*low` `!draft` `@errands`\n");

        Assert.Null(harness.State.RepositoryFor(row));

        await harness.State.PushToGitHubAsync(row);

        Assert.Null(row.IssueLink);
        Assert.Equal(0, harness.Client.CreateCount);
    }

    [Fact]
    public async Task The_link_survives_a_reload_of_the_markdown_file()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `@backlog`\n");
        await harness.State.PushToGitHubAsync(row);

        var reloaded = StateFor(harness.Store, harness.Integration);
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


    /// <summary>
    /// Editing one step touches that chapter and nothing around it.
    /// <para>
    /// The route changed and the claim did not. There used to be a raw textarea per
    /// sub-item card, handed the chapter's whole text; a step's title and its notes
    /// are now two controls in the detail pane, each reporting through the shared
    /// task row. Both still end in a <c>ReplaceSubItemText</c> against the same
    /// index, which is exactly what this has always been about: an edit that reached
    /// the wrong chapter would overwrite a neighbour with no error anywhere.
    /// </para>
    /// </summary>
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

        await harness.State.RenameSubItemAsync(row, 1, "Updated target");
        harness.State.ChangeSubItemNote(row, 1, "New target notes.");

        Assert.Contains("## First\nKeep this.", row.RawText);
        // Same line break the chapter already had. Writing notes back must not
        // introduce a blank line the author did not type.
        Assert.Contains("### Updated target\nNew target notes.", row.RawText);
        Assert.DoesNotContain("Old target notes.", row.RawText);
        Assert.Contains("## Last\nKeep last.", row.RawText);
    }

    [Fact]
    public async Task Editing_parent_entry_updates_only_the_parent_chapter()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await WriteEntryAsync(harness.State,
            "# Parent\n" +
            "`task` `*medium` `!draft` `@backlog`\n\n" +
            "Old parent notes.\n\n" +
            "## Child\n" +
            "Keep child.\n\n" +
            "### Nested\n" +
            "Keep nested.\n");

        harness.State.BeginEdit(row);
        Assert.DoesNotContain("## Child", harness.State.EntryEditText(row));

        harness.State.OnRawTextInput(row,
            "# Parent\n`task` `*medium` `!ready` `@backlog`\n\nNew parent notes.");
        await harness.State.EndEditAsync(row);

        Assert.Contains("New parent notes.", row.RawText);
        Assert.DoesNotContain("Old parent notes.", row.RawText);
        Assert.Contains("## Child\nKeep child.", row.RawText);
        Assert.Contains("### Nested\nKeep nested.", row.RawText);
    }

    /// <summary>
    /// A sub-item push used to be asserted here, filing one chapter as its own issue
    /// in the parent's repository. It has gone with the method behind it, and the
    /// reason is the model rather than the plumbing: <c>.domain/tasks/domain.md</c>
    /// gives <c>ProjectionRef</c> to the entry and says a Sub-Item "may project to
    /// GitHub issue task-list checkboxes" — checkboxes inside the entry's issue. A
    /// step filed as its own issue had nowhere to record the link, so nothing could
    /// tell it had already been filed.
    /// <para>
    /// Deleted rather than left behind, deliberately, and in the same change as the
    /// method: a passing test over an unreachable method reads as coverage of a
    /// feature that no longer exists, which is the state that made these buttons
    /// disappear-and-reappear twice already.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_entry_push_still_uses_the_repository_its_area_names()
    {
        var harness = Build("backlog = JSdotNet/Backlog", "other = someone/else");
        var row = await WriteEntryAsync(harness.State,
            "# Parent\n" +
            "`task` `*medium` `!draft` `@backlog`\n\n" +
            "## Child\n" +
            "`task` `*high` `!ready` `@other` `#child`\n" +
            "Child notes.\n");

        await harness.State.PushToGitHubAsync(row);

        Assert.Null(row.GitHubError);
        Assert.Equal("JSdotNet/Backlog", harness.Client.CreatedRepository);

        // The whole entry: its own title, and the chapter carried inside its body
        // rather than filed separately.
        Assert.Equal("Parent", harness.Client.CreatedTitle);
        Assert.Contains("## Child", harness.Client.CreatedBody);
    }

    [Fact]
    public void Nothing_about_github_shows_until_a_repository_is_configured()
    {
        Assert.False(Build().State.GitHubConfigured);
    }

    [Fact]
    public async Task Feedback_reports_upload_the_screenshot_and_link_the_real_url_in_the_issue()
    {
        var harness = Build("someone/else");
        var screenshot = new GitHubFeedbackScreenshot(
            "data:image/jpeg;base64,AAAA",
            "image/jpeg",
            800,
            600,
            42);

        var link = await harness.Feedback.ReportAsync("Broken view", "The pane is blank.", screenshot);

        // A data: URL embedded straight in the body is stripped by GitHub's
        // markdown sanitizer and never renders — the fix commits the screenshot
        // to the repository first and links the real URL that comes back.
        Assert.Equal("JSdotNet/Backlog", harness.Client.UploadedRepository);
        Assert.Equal("feedback-screenshots", harness.Client.UploadedBranch);
        Assert.Equal("JSdotNet/Backlog", harness.Client.CreatedRepository);
        Assert.Equal("[Feedback][Desktop app] Broken view", harness.Client.CreatedTitle);
        Assert.DoesNotContain("## Desktop app screen area", harness.Client.CreatedBody);
        Assert.Contains("The pane is blank.", harness.Client.CreatedBody);
        Assert.Contains($"![Screenshot]({FakeGitHubClient.UploadedDownloadUrl})", harness.Client.CreatedBody);
        Assert.DoesNotContain("data:image", harness.Client.CreatedBody);
        Assert.Equal("JSdotNet/Backlog", link.RepoFullName);
    }

    [Fact]
    public async Task Feedback_reports_include_the_screenshot_failure_when_capture_fails()
    {
        var harness = Build("JSdotNet/Backlog");

        await harness.Feedback.ReportAsync("Cannot capture", null, null, "Permission denied.");

        Assert.Equal("JSdotNet/Backlog", harness.Client.CreatedRepository);
        Assert.Contains("_No details provided._", harness.Client.CreatedBody);
        Assert.Contains("Screenshot capture failed: Permission denied.", harness.Client.CreatedBody);
    }

    [Fact]
    public async Task A_screenshot_upload_failure_still_files_the_issue()
    {
        var harness = Build("JSdotNet/Backlog");
        harness.Client.UploadFailure = new GitHubException("GitHub refused the request — the token may lack repo scope.");
        var screenshot = new GitHubFeedbackScreenshot("data:image/jpeg;base64,AAAA", "image/jpeg", 800, 600, 42);

        var link = await harness.Feedback.ReportAsync("Broken view", "The pane is blank.", screenshot);

        Assert.Equal("JSdotNet/Backlog", link.RepoFullName);
        Assert.Contains("Screenshot upload failed:", harness.Client.CreatedBody);
        Assert.DoesNotContain("![Screenshot]", harness.Client.CreatedBody);
    }

    private async Task<EntryRow> WriteEntryAsync(TasksDesktopState state, string text)
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

        var store = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
        Assert.Null(store.TryUseRoot(root));

        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var (parsed, _) = GitHubSettings.ParseText(string.Join('\n', repositories));
        settings.SetRepositories(parsed);

        var client = new FakeGitHubClient();
        var integration = new GitHubIntegration(settings, client, new FakeProbe());

        return new Harness(StateFor(store, integration), client, store, integration, new FeedbackReporter(integration));
    }

    /// <summary>The list state, remembered so <see cref="Dispose"/> can hand back
    /// the timed saves it arms.</summary>
    private TasksDesktopState StateFor(WorkspaceSettingsStore store, GitHubIntegration integration)
    {
        var state = TasksTestHost.StateFor(store, integration);
        _states.Add(state);
        return state;
    }

    private sealed record Harness(
        TasksDesktopState State,
        FakeGitHubClient Client,
        WorkspaceSettingsStore Store,
        GitHubIntegration Integration,
        FeedbackReporter Feedback);

    private sealed class FakeGitHubClient : IGitHubClient
    {
        public const string UploadedDownloadUrl = "https://raw.githubusercontent.com/JSdotNet/Backlog/feedback-screenshots/feedback-screenshots/fake.jpg";

        public int CreateCount { get; set; }
        public string? CreatedRepository { get; private set; }
        public string? CreatedTitle { get; private set; }
        public string? CreatedBody { get; private set; }
        public IReadOnlyList<string> CreatedLabels { get; private set; } = [];
        public Exception? Failure { get; set; }
        public Exception? UploadFailure { get; set; }
        public string? UploadedRepository { get; private set; }
        public string? UploadedBranch { get; private set; }
        public string? UploadedPath { get; private set; }
        public byte[]? UploadedContent { get; private set; }

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

        public Task<GitHubUploadedFile> UploadFileAsync(
            GitHubRepositoryRef repository,
            string path,
            string branch,
            byte[] content,
            string commitMessage,
            CancellationToken cancellationToken = default)
        {
            if (UploadFailure is not null) throw UploadFailure;

            UploadedRepository = repository.FullName;
            UploadedBranch = branch;
            UploadedPath = path;
            UploadedContent = content;

            return Task.FromResult(new GitHubUploadedFile(path, UploadedDownloadUrl));
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
