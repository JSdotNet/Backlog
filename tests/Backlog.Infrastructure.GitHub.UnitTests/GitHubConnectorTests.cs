using Backlog.Infrastructure.GitHub;
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
        Assert.Equal(Since, client.ClosedSince);
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

    [Fact]
    public async Task Who_am_i_is_the_login_every_repository_is_bound_to()
    {
        var settings = new GitHubSettings
        {
            Repositories = [Repository with { Account = "work" }, new GitHubRepositoryRef("other", "octo", "cat") { Account = "Work" }],
        };

        var me = await Connector(new StubClient(), settings: settings, defaultLogin: "personal").WhoAmIAsync(TestContext.Current.CancellationToken);

        Assert.Equal("work", me);
    }

    [Fact]
    public async Task Who_am_i_is_the_signed_in_login_when_no_repository_names_an_account()
    {
        var me = await Connector(new StubClient(), defaultLogin: "personal").WhoAmIAsync(TestContext.Current.CancellationToken);

        Assert.Equal("personal", me);
    }

    [Fact]
    public async Task Who_am_i_is_nobody_when_github_cannot_say()
    {
        var me = await Connector(new StubClient(), defaultLogin: null).WhoAmIAsync(TestContext.Current.CancellationToken);

        Assert.Null(me);
    }

    private static GitHubConnector Connector(
        StubClient client,
        IReadOnlyList<EntryProjectionDto>? projections = null,
        GitHubSettings? settings = null,
        string? defaultLogin = "me")
    {
        settings ??= new GitHubSettings { Repositories = [Repository] };
        return new GitHubConnector(
            client,
            () => settings,
            new StubIdentity(defaultLogin),
            _ => Task.FromResult(projections ?? []));
    }

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

        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubIdentity(string? login) : IGitHubIdentityClient
    {
        public Task<string?> GetLoginAsync(CancellationToken cancellationToken = default) => Task.FromResult(login);
    }
}
