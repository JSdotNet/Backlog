using Backlog.Desktop.UI.PullRequests;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Tests;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Sessions context's pull request activity closing the notifications about a pull
/// request once a reading finds it merged or closed, and answering the startup probe's
/// question about it from GitHub (<c>.devbook/domain/notifications/domain.md#subject</c>).
/// </summary>
public sealed class PullRequestSubjectsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-pull-subjects", Guid.NewGuid().ToString("n"));
    private readonly RecordingNotifications _notifications = new RecordingNotifications();

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_reading_that_finds_it_merged_or_closed_closes_the_notifications_about_it_once(bool merged, bool closed)
    {
        var subjects = new PullRequestSubjects(gitHub: null, () => _notifications);
        var pull = Pull(42, "JSdotNet/Backlog") with { IsMerged = merged, IsClosed = closed };

        await subjects.ObserveAsync(pull, TestContext.Current.CancellationToken);
        await subjects.ObserveAsync(pull, TestContext.Current.CancellationToken);

        var ended = Assert.Single(_notifications.Closed);
        Assert.Equal(new NotificationSubject("sessions", "pull-request", "jsdotnet/backlog#42"), ended.Subject);
        Assert.Equal("Pull request merged", ended.Reason);
    }

    [Fact]
    public async Task An_open_pull_request_closes_nothing()
    {
        var subjects = new PullRequestSubjects(gitHub: null, () => _notifications);

        await subjects.ObserveAsync(Pull(42, "JSdotNet/Backlog"), TestContext.Current.CancellationToken);
        await subjects.ObserveAsync(Pull(43, "JSdotNet/Backlog") with { IsDraft = true }, TestContext.Current.CancellationToken);

        Assert.Empty(_notifications.Closed);
    }

    [Fact]
    public async Task A_close_that_failed_is_tried_again_on_the_next_reading()
    {
        _notifications.Fails = new InvalidOperationException("locked");
        var subjects = new PullRequestSubjects(gitHub: null, () => _notifications);
        var merged = Pull(42, "JSdotNet/Backlog") with { IsMerged = true };

        await subjects.ObserveAsync(merged, TestContext.Current.CancellationToken);
        _notifications.Fails = null;
        await subjects.ObserveAsync(merged, TestContext.Current.CancellationToken);

        Assert.Single(_notifications.Closed);
    }

    [Fact]
    public async Task The_session_card_reading_reports_what_it_found()
    {
        var client = new PinnedClient(Pull(1480, "JSdotNet/Backlog") with { IsMerged = true });
        var subjects = new PullRequestSubjects(GitHub(client, configured: true), () => _notifications);

        var status = await SessionPullRequestStatuses.ReadAsync(
            GitHub(client, configured: true),
            new DeliveryRunReference(DeliveryRunReferenceKind.PullRequest, "PR #1480", Title: null, "https://github.com/JSdotNet/Backlog/pull/1480", "JSdotNet/Backlog"),
            subjects);

        Assert.NotNull(status);
        Assert.Equal("jsdotnet/backlog#1480", Assert.Single(_notifications.Closed).Subject.Id);
    }

    [Fact]
    public async Task The_probes_question_is_answered_by_a_pinned_read_and_no_answer_is_null()
    {
        var client = new PinnedClient(
            Pull(1, "JSdotNet/Backlog"),
            Pull(2, "JSdotNet/Backlog") with { IsMerged = true },
            Pull(3, "JSdotNet/Backlog") with { IsClosed = true });
        var subjects = new PullRequestSubjects(GitHub(client, configured: false), () => _notifications);
        var withoutGitHub = new PullRequestSubjects(gitHub: null, () => _notifications);

        Assert.True(await subjects.IsOpenAsync("jsdotnet/backlog", 1, TestContext.Current.CancellationToken));
        Assert.False(await subjects.IsOpenAsync("jsdotnet/backlog", 2, TestContext.Current.CancellationToken));
        Assert.False(await subjects.IsOpenAsync("jsdotnet/backlog", 3, TestContext.Current.CancellationToken));
        Assert.Null(await subjects.IsOpenAsync("jsdotnet/backlog", 404, TestContext.Current.CancellationToken));
        Assert.Null(await withoutGitHub.IsOpenAsync("jsdotnet/backlog", 1, TestContext.Current.CancellationToken));
    }

    private GitHubIntegration GitHub(IGitHubClient client, bool configured)
    {
        var settings = new GitHubSettingsStore(Path.Combine(_root, Guid.NewGuid().ToString("n"), "github.json"));
        if (configured)
        {
            var (parsed, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
            Assert.Empty(errors);
            Assert.Null(settings.SetRepositories(parsed));
        }

        return new GitHubIntegration(settings, client, new StubProbe(new GitHubConnection(true, "Connected.")));
    }

    private static GitHubOpenPullRequest Pull(int number, string repository) =>
        new(
            Number: number,
            Url: $"https://github.com/{repository}/pull/{number}",
            Title: "Close notifications when their subject ends",
            RepositoryFullName: repository,
            NodeId: $"PR_{number}",
            IsDraft: false,
            HeadRefName: "feature/subjects",
            HeadSha: "abc123",
            BaseRefName: "main",
            AuthorLogin: "octocat",
            ViewerDidAuthor: true,
            Checks: GitHubCheckState.Passing,
            AutoMergeEnabled: false,
            MergeReady: true,
            IsBehind: false,
            HasConflicts: false,
            MergeStateStatus: null,
            PreferredMergeMethod: GitHubMergeMethod.Squash,
            UpdatedAt: DateTimeOffset.Parse("2026-10-22T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Answers a pinned read from the pull requests it was given; nothing else.</summary>
    private sealed class PinnedClient(params GitHubOpenPullRequest[] pulls) : IGitHubClient
    {
        public Task<IReadOnlyList<GitHubOpenPullRequest>> ListPullRequestsAsync(GitHubRepositoryRef repository, IReadOnlyCollection<int> numbers, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GitHubOpenPullRequest>>(
                [.. pulls.Where(pull => numbers.Contains(pull.Number) && string.Equals(pull.RepositoryFullName, repository.FullName, StringComparison.OrdinalIgnoreCase))]);

        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
