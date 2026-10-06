using Backlog.Desktop.UI.PullRequests;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Which task a pull request belongs to, found in order of how sure the match is:
/// the task's own record of the pull request, then a GitHub issue the pull request
/// closes, then the task's external key in the pull request's title or branch, then
/// the task that linked the session the pull request came out of.
/// </summary>
public sealed class PullRequestTaskMatcherTests
{
    private static readonly Guid Recorded = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Issue = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Keyed = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Session = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // --- Each relation on its own ---------------------------------------------

    [Fact]
    public void A_task_that_recorded_the_pull_request_is_its_task()
    {
        var matcher = new PullRequestTaskMatcher([Candidate(Recorded, pullRequests: [new("jsdotnet/backlog", 712)])]);

        var match = matcher.Match(Subject());

        Assert.Equal(Recorded, match?.Candidate.EntryId);
        Assert.Equal(PullRequestTaskRelation.Recorded, match?.Relation);
    }

    [Fact]
    public void A_linked_github_issue_the_pull_request_closes_is_its_task()
    {
        var matcher = new PullRequestTaskMatcher([Candidate(Issue, gitHubIssue: new("JSdotNet/Backlog", 42))]);

        var match = matcher.Match(Subject(closing: [new GitHubIssueReference("jsdotnet/backlog", 42)]));

        Assert.Equal(Issue, match?.Candidate.EntryId);
        Assert.Equal(PullRequestTaskRelation.ClosesIssue, match?.Relation);
    }

    /// <summary>The issue's repository counts: issue 42 of another repository is
    /// another issue.</summary>
    [Fact]
    public void An_issue_with_the_same_number_in_another_repository_is_not_a_match()
    {
        var matcher = new PullRequestTaskMatcher([Candidate(Issue, gitHubIssue: new("JSdotNet/Archify", 42))]);

        Assert.Null(matcher.Match(Subject(closing: [new GitHubIssueReference("JSdotNet/Backlog", 42)])));
    }

    [Theory]
    [InlineData("FIN-8428: Show the invoice total", "claude/unrelated")]
    [InlineData("Show the invoice total", "feature/FIN-8428-invoice-total")]
    [InlineData("fin-8428 lower case", "x")]
    [InlineData("[FIN-8428] bracketed", "x")]
    public void The_tasks_external_key_in_the_title_or_branch_is_a_match(string title, string branch)
    {
        var matcher = new PullRequestTaskMatcher([Candidate(Keyed, externalKeys: ["FIN-8428"])]);

        var match = matcher.Match(Subject(title: title, branch: branch));

        Assert.Equal(Keyed, match?.Candidate.EntryId);
        Assert.Equal(PullRequestTaskRelation.ExternalKey, match?.Relation);
    }

    /// <summary>A key is a whole token: FIN-84 is not FIN-8428, and FIN-8428 is not
    /// XFIN-8428 or FIN-84281.</summary>
    [Theory]
    [InlineData("FIN-84", "FIN-8428: total")]
    [InlineData("FIN-8428", "XFIN-8428 total")]
    [InlineData("FIN-8428", "FIN-84281 total")]
    [InlineData("FIN-8428", "X_FIN-8428 total")]
    public void A_key_inside_a_longer_key_is_not_a_match(string key, string title)
    {
        var matcher = new PullRequestTaskMatcher([Candidate(Keyed, externalKeys: [key])]);

        Assert.Null(matcher.Match(Subject(title: title, branch: "x")));
    }

    [Fact]
    public void The_task_that_linked_the_pull_requests_session_is_its_task()
    {
        var matcher = new PullRequestTaskMatcher([Candidate(Session, sessionIds: ["session-1"])]);

        var match = matcher.Match(Subject(sessionId: "SESSION-1"));

        Assert.Equal(Session, match?.Candidate.EntryId);
        Assert.Equal(PullRequestTaskRelation.Session, match?.Relation);
    }

    [Fact]
    public void A_pull_request_nothing_relates_to_has_no_task()
    {
        var matcher = new PullRequestTaskMatcher([Candidate(Keyed, externalKeys: ["FIN-1"])]);

        Assert.Null(matcher.Match(Subject()));
    }

    // --- The order -------------------------------------------------------------

    /// <summary>Every candidate below relates to the pull request; the surest relation
    /// wins whatever order the tasks come in.</summary>
    [Fact]
    public void The_surest_relation_wins()
    {
        var candidates = new[]
        {
            Candidate(Session, sessionIds: ["session-1"]),
            Candidate(Keyed, externalKeys: ["FIN-8428"]),
            Candidate(Issue, gitHubIssue: new("JSdotNet/Backlog", 42)),
            Candidate(Recorded, pullRequests: [new("JSdotNet/Backlog", 712)]),
        };
        var subject = Subject(title: "FIN-8428 total", closing: [new GitHubIssueReference("JSdotNet/Backlog", 42)], sessionId: "session-1");

        Assert.Equal(Recorded, new PullRequestTaskMatcher(candidates).Match(subject)?.Candidate.EntryId);
        Assert.Equal(Issue, new PullRequestTaskMatcher(candidates[..3]).Match(subject)?.Candidate.EntryId);
        Assert.Equal(Keyed, new PullRequestTaskMatcher(candidates[..2]).Match(subject)?.Candidate.EntryId);
        Assert.Equal(Session, new PullRequestTaskMatcher(candidates[..1]).Match(subject)?.Candidate.EntryId);
    }

    /// <summary>"Mine" asks about my tasks only: a surer match on somebody else's task
    /// does not hide a match on mine.</summary>
    [Fact]
    public void Mine_only_considers_my_tasks()
    {
        var matcher = new PullRequestTaskMatcher(
        [
            Candidate(Recorded, pullRequests: [new("JSdotNet/Backlog", 712)], isMine: false),
            Candidate(Keyed, externalKeys: ["FIN-8428"]),
        ]);
        var subject = Subject(title: "FIN-8428 total");

        Assert.Equal(Recorded, matcher.Match(subject)?.Candidate.EntryId);
        Assert.Equal(Keyed, matcher.Match(subject, mineOnly: true)?.Candidate.EntryId);
    }

    // --- Building candidates ----------------------------------------------------

    [Theory]
    [InlineData("#123 · FIN-8428", "FIN-8428")]
    [InlineData("#123", null)]
    [InlineData("", null)]
    [InlineData("#123 · not a key", null)]
    public void The_external_key_is_read_from_a_display_key(string displayKey, string? expected)
    {
        var keys = PullRequestTaskMatcher.ExternalKeysOf(displayKey);

        if (expected is null) Assert.Empty(keys);
        else Assert.Equal([expected], keys);
    }

    [Theory]
    [InlineData("github", "JSdotNet/Backlog", "#42", "https://github.com/JSdotNet/Backlog/issues/42", 42)]
    [InlineData("github", "JSdotNet/Backlog", "", "https://github.com/JSdotNet/Backlog/issues/43", 43)]
    [InlineData("spec-manager", "fin", "#42 · FIN-1", "https://example.com/42", null)]
    public void A_github_linked_task_names_its_issue(string connector, string target, string displayKey, string url, int? expected)
    {
        var issue = PullRequestTaskMatcher.GitHubIssueOf(connector, target, displayKey, url);

        Assert.Equal(expected, issue?.Number);
        if (expected is not null) Assert.Equal(target, issue!.RepositoryFullName);
    }

    // --- Helpers ----------------------------------------------------------------

    private static PullRequestTaskCandidate Candidate(
        Guid id,
        IReadOnlyList<RecordedPullRequest>? pullRequests = null,
        GitHubIssueReference? gitHubIssue = null,
        IReadOnlyList<string>? externalKeys = null,
        IReadOnlyList<string>? sessionIds = null,
        bool isMine = true) =>
        new(id, pullRequests ?? [], gitHubIssue, externalKeys ?? [], sessionIds ?? [], isMine);

    private static PullRequestTaskSubject Subject(
        string title = "Pull requests page",
        string branch = "claude/pr-page",
        IReadOnlyList<GitHubIssueReference>? closing = null,
        string? sessionId = null) =>
        new("JSdotNet/Backlog", 712, title, branch, closing ?? [], sessionId);
}
