using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The GitHub issues connector: one configured repository's issues, normalised to
/// the items the linked-task sync turns into tasks (local ADR 0020, §3).
/// </summary>
public sealed class GitHubConnectorTests
{
    private const string Target = "JSdotNet/Backlog";

    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    private static readonly DateTimeOffset Since = new(2026, 10, 1, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void It_presents_itself_as_github()
    {
        var connector = Connector(new StubClient());

        Assert.Equal("github", connector.Descriptor.Id);
        Assert.Equal("GitHub", connector.Descriptor.DisplayName);
        Assert.True(connector.Capabilities.HasEffort);
        Assert.False(connector.Capabilities.CanSetStatus);
        Assert.True(connector.Capabilities.CanComplete);
    }

    /// <summary>Issue search lags behind a close. An issue closed just before the
    /// last sync — by the write-back that asked for it — can have read as open to
    /// that sync, so the closed query reaches back before it, or the issue would be
    /// in neither answer and its task archived as vanished.</summary>
    [Fact]
    public async Task The_closed_query_reaches_back_before_the_last_sync()
    {
        var client = new StubClient();

        await Connector(client).FetchAsync(Target, Since, TestContext.Current.CancellationToken);

        Assert.Equal(Since - TimeSpan.FromMinutes(10), client.ClosedSince);
    }

    [Fact]
    public async Task A_first_sync_asks_for_no_closed_issues()
    {
        var client = new StubClient();

        await Connector(client).FetchAsync(Target, since: null, TestContext.Current.CancellationToken);

        Assert.Null(client.ClosedSince);
    }

    // --- Completing at the source ---------------------------------------------------

    /// <summary>Closing again would rewrite the reason it closed with: an issue
    /// closed as not planned would read as completed.</summary>
    [Fact]
    public async Task An_issue_already_closed_is_left_as_it_is_and_answered_as_completed()
    {
        var client = new StubClient { IssueState = GitHubItemState.Closed };

        var refusal = await Connector(client).CompleteAsync(Linked("#412"), TestContext.Current.CancellationToken);

        Assert.Null(refusal);
        Assert.Equal((Repository, 412), Assert.Single(client.Read));
        Assert.Empty(client.Closed);
    }

    [Fact]
    public async Task An_issue_github_will_not_show_is_refused_with_githubs_words()
    {
        var client = new StubClient
        {
            ReadFailure = new GitHubException("GitHub couldn't find that repository.") { Status = System.Net.HttpStatusCode.NotFound },
        };

        var refusal = await Connector(client).CompleteAsync(Linked("#412"), TestContext.Current.CancellationToken);

        Assert.Equal("GitHub refused (404): GitHub couldn't find that repository.", refusal);
        Assert.Empty(client.Closed);
    }

    [Fact]
    public async Task Completing_an_item_closes_its_issue_in_the_configured_repository()
    {
        var client = new StubClient();

        var refusal = await Connector(client).CompleteAsync(Linked("#412"), TestContext.Current.CancellationToken);

        Assert.Null(refusal);
        Assert.Equal((Repository, 412), Assert.Single(client.Read));
        Assert.Equal((Repository, 412), Assert.Single(client.Closed));
    }

    [Fact]
    public async Task The_target_is_matched_to_its_repository_whatever_its_case()
    {
        var client = new StubClient();

        Assert.Null(await Connector(client).CompleteAsync(Linked("#412", target: "jsdotnet/BACKLOG"), TestContext.Current.CancellationToken));
        Assert.Equal(Repository, Assert.Single(client.Closed).Repository);
    }

    /// <summary>The display key is what the sync wrote, but a reference whose key was
    /// lost still has the issue's URL.</summary>
    [Fact]
    public async Task Without_a_number_in_the_key_the_number_is_read_from_the_url()
    {
        var client = new StubClient();

        Assert.Null(await Connector(client).CompleteAsync(Linked("Fix the badge", url: "https://github.com/JSdotNet/Backlog/issues/77/"), TestContext.Current.CancellationToken));
        Assert.Equal(77, Assert.Single(client.Closed).Number);
    }

    [Fact]
    public async Task An_item_that_names_no_issue_number_is_refused_without_asking_github()
    {
        var client = new StubClient();

        var refusal = await Connector(client).CompleteAsync(Linked("PROJ-1", url: "https://example.com/browse/PROJ-1"), TestContext.Current.CancellationToken);

        Assert.Contains("PROJ-1", refusal);
        Assert.Empty(client.Closed);
    }

    [Fact]
    public async Task A_repository_that_is_no_longer_configured_is_refused_in_words()
    {
        var client = new StubClient();

        var refusal = await Connector(client).CompleteAsync(Linked("#412", target: "someone/else"), TestContext.Current.CancellationToken);

        Assert.Contains("someone/else", refusal);
        Assert.Empty(client.Closed);
    }

    [Fact]
    public async Task A_refusal_from_github_says_the_status_and_githubs_own_words()
    {
        var client = new StubClient
        {
            CloseFailure = new GitHubException("Resource not accessible by personal access token") { Status = System.Net.HttpStatusCode.Forbidden },
        };

        var refusal = await Connector(client).CompleteAsync(Linked("#412"), TestContext.Current.CancellationToken);

        Assert.Equal("GitHub refused (403): Resource not accessible by personal access token", refusal);
    }

    [Fact]
    public async Task A_failure_with_no_status_is_refused_with_its_message()
    {
        var client = new StubClient { CloseFailure = new GitHubException("Couldn't reach GitHub: No such host is known.") };

        Assert.Equal(
            "Couldn't reach GitHub: No such host is known.",
            await Connector(client).CompleteAsync(Linked("#412"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task No_way_to_sign_in_is_refused_with_its_message()
    {
        var client = new StubClient { CloseFailure = new GitHubNotConfiguredException("No GitHub account can reach JSdotNet/Backlog.") };

        Assert.Equal(
            "No GitHub account can reach JSdotNet/Backlog.",
            await Connector(client).CompleteAsync(Linked("#412"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_issue_becomes_an_item_named_by_its_node_id_and_number()
    {
        var client = new StubClient(Issue(412, title: "Fix the badge", body: "It is blue", assignee: "octocat"));

        var item = Assert.Single(await Connector(client).FetchAsync(Target, Since, TestContext.Current.CancellationToken));

        Assert.Equal("I_412", item.ExternalId);
        Assert.Equal("#412", item.DisplayKey);
        Assert.Equal("Fix the badge", item.Title);
        Assert.Equal("https://github.com/JSdotNet/Backlog/issues/412", item.Url);
        Assert.Equal("It is blue", item.Body);
        Assert.Equal("octocat", item.Assignee);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero), item.UpdatedAt);
        Assert.Equal(Since - GitHubConnector.ClosedQueryOverlap, client.ClosedSince);
        Assert.Equal(Repository, client.Repository);
    }

    [Theory]
    [InlineData(true, null, NormalisedSourceState.Open, "open")]
    [InlineData(false, "completed", NormalisedSourceState.Done, "closed as completed")]
    [InlineData(false, "not_planned", NormalisedSourceState.Dropped, "closed as not planned")]
    [InlineData(false, "duplicate", NormalisedSourceState.Dropped, "closed as duplicate")]
    [InlineData(false, null, NormalisedSourceState.Done, "closed")]
    public async Task The_state_and_its_reason_normalise(bool open, string? reason, NormalisedSourceState expected, string name)
    {
        var client = new StubClient(Issue(1, open: open, stateReason: reason));

        var item = Assert.Single(await Connector(client).FetchAsync(Target, Since, TestContext.Current.CancellationToken));

        Assert.Equal(expected, item.State);
        Assert.Equal(name, item.SourceStateName);
    }

    [Fact]
    public async Task Labels_pass_through_as_github_spells_them()
    {
        var client = new StubClient(Issue(1, labels: ["+roadmap-plan", "bug", "points:3", "blocked"]));

        var item = Assert.Single(await Connector(client).FetchAsync(Target, Since, TestContext.Current.CancellationToken));

        Assert.Equal(["+roadmap-plan", "bug", "points:3", "blocked"], item.Labels);
    }

    [Theory]
    [InlineData("points:5", 5)]
    [InlineData("Points: 8", 8)]
    [InlineData("points:x", null)]
    [InlineData("points:-1", null)]
    [InlineData("pointsless", null)]
    public async Task A_points_label_gives_the_effort(string label, int? effort)
    {
        var client = new StubClient(Issue(1, labels: [label]));

        var item = Assert.Single(await Connector(client).FetchAsync(Target, Since, TestContext.Current.CancellationToken));

        Assert.Equal(effort, item.Effort);
    }

    [Theory]
    [InlineData("blocked", true)]
    [InlineData("Blocked", true)]
    [InlineData("unblocked", false)]
    public async Task A_blocked_label_marks_the_item_blocked(string label, bool blocked)
    {
        var client = new StubClient(Issue(1, labels: [label]));

        var item = Assert.Single(await Connector(client).FetchAsync(Target, Since, TestContext.Current.CancellationToken));

        Assert.Equal(blocked, item.IsBlocked);
    }

    /// <summary>A task pushed to GitHub records the issue it became; that issue
    /// must not come back as a second, linked task.</summary>
    [Fact]
    public async Task An_issue_a_backlog_task_was_pushed_to_is_skipped()
    {
        var client = new StubClient(Issue(1), Issue(2));
        var projections = new[]
        {
            new EntryProjectionDto("jsdotnet/backlog", "2", EntryProjectionDto.IssueTargetType),
            new EntryProjectionDto("JSdotNet/Other", "1", EntryProjectionDto.IssueTargetType),
        };

        var items = await Connector(client, projections).FetchAsync(Target, Since, TestContext.Current.CancellationToken);

        Assert.Equal("#1", Assert.Single(items).DisplayKey);
    }

    [Fact]
    public async Task A_pull_request_projection_does_not_hide_an_issue_with_the_same_number()
    {
        var client = new StubClient(Issue(5));
        var projections = new[] { new EntryProjectionDto(Target, "5", EntryProjectionDto.PullRequestTargetType) };

        var items = await Connector(client, projections).FetchAsync(Target, Since, TestContext.Current.CancellationToken);

        Assert.Single(items);
    }

    /// <summary>A partial answer is not the repository: the sync would archive every
    /// task whose issue fell off the end as vanished. It fails instead, and the sync
    /// writes nothing.</summary>
    [Fact]
    public async Task A_truncated_search_fails_the_fetch()
    {
        var client = new StubClient(Issue(1)) { Truncated = true };

        await Assert.ThrowsAsync<GitHubException>(() =>
            Connector(client).FetchAsync(Target, Since, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_target_that_is_not_a_configured_repository_is_refused()
    {
        await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
            Connector(new StubClient()).FetchAsync("someone/else", Since, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_configured_repository_is_matched_without_regard_to_case()
    {
        var client = new StubClient(Issue(1));

        var items = await Connector(client).FetchAsync("jsdotnet/BACKLOG", Since, TestContext.Current.CancellationToken);

        Assert.Single(items);
        Assert.Equal(Repository, client.Repository);
    }

    [Fact]
    public async Task The_targets_are_the_configured_repositories()
    {
        var settings = new GitHubSettings { Repositories = [Repository, new("other", "octo", "cat")] };

        var targets = await Connector(new StubClient(), settings: settings).ListTargetsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["JSdotNet/Backlog", "octo/cat"], targets);
    }

    private static GitHubConnector Connector(
        StubClient client,
        IReadOnlyList<EntryProjectionDto>? projections = null,
        GitHubSettings? settings = null)
    {
        settings ??= new GitHubSettings { Repositories = [Repository] };
        return new GitHubConnector(
            client,
            () => settings,
            _ => Task.FromResult(projections ?? []));
    }

    private static SourceRef Linked(string displayKey, string target = Target, string url = "https://github.com/JSdotNet/Backlog/issues/412") =>
        new(GitHubConnector.ConnectorId, target, "I_412", url, displayKey, null, "open", Since, normalisedState: NormalisedSourceState.Open);

    private static GitHubSearchedIssue Issue(
        int number,
        string? title = null,
        string body = "",
        bool open = true,
        string? stateReason = null,
        string? assignee = null,
        string[]? labels = null) =>
        new(
            $"I_{number}",
            number,
            $"https://github.com/JSdotNet/Backlog/issues/{number}",
            title ?? $"Issue {number}",
            body,
            open,
            stateReason,
            assignee,
            new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero),
            labels ?? []);

    private sealed class StubClient(params GitHubSearchedIssue[] issues) : IGitHubClient
    {
        public bool Truncated { get; init; }

        public GitHubRepositoryRef? Repository { get; private set; }

        public DateTimeOffset? ClosedSince { get; private set; }

        public Task<GitHubIssueSearchRead> SearchIssuesAsync(
            GitHubRepositoryRef repository,
            DateTimeOffset? closedSince,
            CancellationToken cancellationToken = default)
        {
            Repository = repository;
            ClosedSince = closedSince;
            return Task.FromResult(new GitHubIssueSearchRead(issues, Truncated));
        }

        /// <summary>The state every issue reads in.</summary>
        public GitHubItemState IssueState { get; init; } = GitHubItemState.Open;

        /// <summary>Thrown by every read instead of answering, when set.</summary>
        public Exception? ReadFailure { get; init; }

        /// <summary>Every issue read, by repository and number.</summary>
        public List<(GitHubRepositoryRef Repository, int Number)> Read { get; } = [];

        /// <summary>Every issue closed, by repository and number.</summary>
        public List<(GitHubRepositoryRef Repository, int Number)> Closed { get; } = [];

        /// <summary>Thrown by every close instead of closing, when set.</summary>
        public Exception? CloseFailure { get; init; }

        public Task CloseIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default)
        {
            if (CloseFailure is not null) return Task.FromException(CloseFailure);
            Closed.Add((repository, number));
            return Task.CompletedTask;
        }

        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default)
        {
            if (ReadFailure is not null) return Task.FromException<GitHubIssueSnapshot>(ReadFailure);
            Read.Add((repository, number));
            return Task.FromResult(new GitHubIssueSnapshot(
                new GitHubIssue(number, $"https://github.com/{repository.FullName}/issues/{number}", $"Issue {number}", IssueState, null),
                [],
                DateTimeOffset.UnixEpoch));
        }

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
