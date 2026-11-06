using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Tests;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Progress's pull request reader, answered through Repository Management's contract
/// (<see cref="RepositoryManagementGoalPullRequests"/>, local ADR 0042 §5, V6): state,
/// merge day, the check counts and failing check names, and the approvals against what
/// the base branch requires (<c>.devbook/domain/progress/features.md#pull-request-status</c>).
/// What it keeps, and for how long, is the contract's one cache; this reader asks it by
/// number and maps the answer.
/// </summary>
public sealed class RepositoryManagementGoalPullRequestsTests
{
    private const string Backlog = "JSdotNet/Backlog";

    private static readonly DateTimeOffset Noon = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task It_answers_state_merge_day_checks_and_approvals_through_one_read_of_the_contract()
    {
        var contract = new InMemoryPullRequests().Holds(
            Pull(4, PullRequestState.Merged) with { MergedAt = Noon },
            Pull(5) with
            {
                CheckCounts = new PullRequestCheckCounts(2, 1, 11),
                HeadChecks =
                [
                    new PullRequestCheck("Build and test .NET", PullRequestCheckState.Failing, null, null, 7),
                    new PullRequestCheck("Build and test .NET", PullRequestCheckState.Failing, null, null, 8),
                    new PullRequestCheck("Lint", PullRequestCheckState.Passing, null, null, null)
                ],
                Approvals = new PullRequestApprovals(PullRequestReviewDecision.ReviewRequired, 1, 0) { Required = 2, RequiredRead = true }
            });
        var reader = new RepositoryManagementGoalPullRequests(() => contract);

        var answer = await reader.ReadAsync([new(Backlog, "4"), new(Backlog, "5"), new(Backlog, "6")], TestContext.Current.CancellationToken);

        Assert.Equal([LinkedPullRequestState.Merged, LinkedPullRequestState.Open, LinkedPullRequestState.Unknown], answer.Select(pull => pull.State));
        Assert.Equal(new DateOnly(2026, 10, 7), answer[0].MergedOn);
        Assert.Equal((2, 1, 11, 14), (answer[1].Checks!.Passed, answer[1].Checks!.Failed, answer[1].Checks!.Pending, answer[1].Checks!.Total));
        Assert.Equal(["Build and test .NET"], answer[1].Checks!.FailingNames);
        Assert.Equal(new GoalPullRequestApprovalsDto(1, 2), answer[1].Approvals);
        Assert.Equal([$"read {Backlog}#4 {Backlog}#5 {Backlog}#6"], contract.Calls);
        Assert.Equal(PullRequestReadOptions.Default, Assert.Single(contract.ReadOptions));
    }

    /// <summary>The caller's own spelling of the repository and number comes back, so a
    /// caller matching its answers to its links finds them; a pull request asked twice is
    /// asked of the contract once.</summary>
    [Fact]
    public async Task It_keeps_the_callers_spelling_and_asks_a_pull_request_once()
    {
        var contract = new InMemoryPullRequests().Holds(Pull(1344));
        var reader = new RepositoryManagementGoalPullRequests(() => contract);

        var answer = await reader.ReadAsync([new(" jsdotnet/backlog ", "#1344"), new(Backlog, "1344")], TestContext.Current.CancellationToken);

        Assert.Equal([(" jsdotnet/backlog ", "#1344"), (Backlog, "1344")], answer.Select(pull => (pull.Repository, pull.Number)));
        Assert.All(answer, pull => Assert.Equal(LinkedPullRequestState.Open, pull.State));
        Assert.Equal(["read jsdotnet/backlog#1344"], contract.Calls);
    }

    /// <summary>A repository that refused reads its pull request as unknown, never as
    /// another repository's pull request that happens to carry the same number.</summary>
    [Fact]
    public async Task A_refused_repository_does_not_borrow_another_repositorys_pull_request_with_the_same_number()
    {
        var contract = new InMemoryPullRequests()
            .Holds(Pull(12, PullRequestState.Merged) with { MergedAt = Noon })
            .Refuses("JSdotNet/Backlogg", "Not Found");
        var reader = new RepositoryManagementGoalPullRequests(() => contract);

        var answer = await reader.ReadAsync([new(Backlog, "12"), new("JSdotNet/Backlogg", "12")], TestContext.Current.CancellationToken);

        Assert.Equal(LinkedPullRequestState.Merged, answer[0].State);
        Assert.Equal(GoalPullRequestDto.Unread(new("JSdotNet/Backlogg", "12")), answer[1]);
    }

    /// <summary>A pull request in an archived repository answers what was kept for it,
    /// flagged so a held-back pull request there is never settled; one in an active
    /// repository, or with no registry composed, is not flagged.</summary>
    [Fact]
    public async Task An_archived_repositorys_pull_request_is_flagged_whatever_was_kept()
    {
        var contract = new InMemoryPullRequests()
            .Holds(Pull(7, PullRequestState.Merged) with { MergedAt = Noon })
            .Holds(Pull(8) with { RepositoryFullName = "JSdotNet/Archify" });
        var reader = new RepositoryManagementGoalPullRequests(() => contract, () => new ArchivedRepositories("jsdotnet/archify"));

        var answer = await reader.ReadAsync([new(Backlog, "7"), new("JSdotNet/Archify", "8")], TestContext.Current.CancellationToken);

        Assert.Equal(
            [(LinkedPullRequestState.Merged, false), (LinkedPullRequestState.Open, true)],
            answer.Select(pull => (pull.State, pull.InArchivedRepository)));
        Assert.False(Assert.Single(await new RepositoryManagementGoalPullRequests(() => contract)
            .ReadAsync([new("JSdotNet/Archify", "8")], TestContext.Current.CancellationToken)).InArchivedRepository);
    }

    /// <summary>A repository that answers, but holds no pull request with the number —
    /// an issue there, or a number not yet used — reads it as unknown.</summary>
    [Fact]
    public async Task A_number_the_contract_answers_without_reads_unknown()
    {
        var contract = new InMemoryPullRequests().Holds(Pull(1477, PullRequestState.Merged) with { MergedAt = Noon });
        var reader = new RepositoryManagementGoalPullRequests(() => contract);

        var answer = await reader.ReadAsync([new(Backlog, "1477"), new("JSdotNet/Marketplace", "1477")], TestContext.Current.CancellationToken);

        Assert.Equal(LinkedPullRequestState.Merged, answer[0].State);
        Assert.Equal(GoalPullRequestDto.Unread(new("JSdotNet/Marketplace", "1477")), answer[1]);
    }

    [Fact]
    public async Task Without_the_contract_or_a_repository_registered_every_pull_request_reads_unknown_and_nothing_is_asked()
    {
        var unconfigured = new InMemoryPullRequests { IsConfigured = false }.Holds(Pull(4));

        var none = Assert.Single(await new RepositoryManagementGoalPullRequests(() => null).ReadAsync([new(Backlog, "4")], TestContext.Current.CancellationToken));
        var off = Assert.Single(await new RepositoryManagementGoalPullRequests(() => unconfigured).ReadAsync([new(Backlog, "4")], TestContext.Current.CancellationToken));

        Assert.Equal(GoalPullRequestDto.Unread(new(Backlog, "4")), none);
        Assert.Equal(GoalPullRequestDto.Unread(new(Backlog, "4")), off);
        Assert.Empty(unconfigured.Calls);
    }

    /// <summary>A reading that throws is Progress's unknown, not a failed reading: what
    /// was not read is asked again next time.</summary>
    [Fact]
    public async Task A_read_that_throws_reads_unknown()
    {
        var contract = new InMemoryPullRequests { Throws = new InvalidOperationException("GitHub went away.") }.Holds(Pull(4));
        var reader = new RepositoryManagementGoalPullRequests(() => contract);

        var answer = Assert.Single(await reader.ReadAsync([new(Backlog, "4")], TestContext.Current.CancellationToken));

        Assert.Equal(LinkedPullRequestState.Unknown, answer.State);
    }

    [Fact]
    public async Task A_link_that_names_no_pull_request_reads_unknown_and_is_not_asked()
    {
        var contract = new InMemoryPullRequests();
        var reader = new RepositoryManagementGoalPullRequests(() => contract);

        var answer = await reader.ReadAsync([new("Backlog", "4"), new(Backlog, "four"), new(Backlog, "0")], TestContext.Current.CancellationToken);

        Assert.All(answer, pull => Assert.Equal(LinkedPullRequestState.Unknown, pull.State));
        Assert.Empty(contract.Calls);
    }

    /// <summary>A merged pull request remembered across a restart, or one an older server
    /// answered over REST, carries its state and merge day and nothing its status read
    /// would have said: no checks and no approvals, rather than a <c>0/0</c> or a
    /// <c>0</c> approvals that would read as GitHub's word.</summary>
    [Fact]
    public async Task A_pull_request_whose_status_was_not_read_has_no_checks_and_no_approvals()
    {
        var contract = new InMemoryPullRequests().Holds(Pull(9, PullRequestState.Merged) with { MergedAt = Noon, StatusRead = false });
        var reader = new RepositoryManagementGoalPullRequests(() => contract);

        var answer = Assert.Single(await reader.ReadAsync([new(Backlog, "9")], TestContext.Current.CancellationToken));

        Assert.Equal(LinkedPullRequestState.Merged, answer.State);
        Assert.Equal(new DateOnly(2026, 10, 7), answer.MergedOn);
        Assert.Null(answer.Checks);
        Assert.Null(answer.Approvals);
    }

    // ---- The mapping -----------------------------------------------------------------------

    /// <summary>No checks on the head commit maps to no checks rather than <c>0/0</c>; no
    /// rule on the base branch maps to approvals with no required total.</summary>
    [Fact]
    public void A_draft_without_checks_or_a_rule_maps_to_no_checks_and_no_required_total()
    {
        var mapped = RepositoryManagementGoalPullRequests.Map(Pull(8, PullRequestState.Draft) with
        {
            Approvals = new PullRequestApprovals(PullRequestReviewDecision.None, 1, 0)
        });

        Assert.Equal(Backlog, mapped.Repository);
        Assert.Equal("8", mapped.Number);
        Assert.Equal(LinkedPullRequestState.Draft, mapped.State);
        Assert.Null(mapped.MergedOn);
        Assert.Null(mapped.Checks);
        Assert.Equal(new GoalPullRequestApprovalsDto(1, null), mapped.Approvals);
    }

    [Fact]
    public void An_answered_pull_request_carries_its_title()
    {
        var mapped = RepositoryManagementGoalPullRequests.Map(Pull(8) with { Title = "Pay invoices by bank transfer" });

        Assert.Equal("Pay invoices by bank transfer", mapped.Title);
    }

    /// <summary>The title reaches the caller through the reading too; one nothing read has
    /// none, so the card draws its status line alone.</summary>
    [Fact]
    public async Task The_reading_carries_the_title_and_a_pull_request_nothing_read_has_none()
    {
        var contract = new InMemoryPullRequests().Holds(Pull(11) with { Title = "Pay invoices by bank transfer" });
        var reader = new RepositoryManagementGoalPullRequests(() => contract);

        var answer = await reader.ReadAsync([new(Backlog, "#11"), new(Backlog, "12")], TestContext.Current.CancellationToken);

        Assert.Equal(["Pay invoices by bank transfer", null], answer.Select(pull => pull.Title));
    }

    [Fact]
    public void A_closed_pull_request_maps_closed()
    {
        Assert.Equal(LinkedPullRequestState.Closed, RepositoryManagementGoalPullRequests.Map(Pull(3, PullRequestState.Closed)).State);
    }

    /// <summary>The viewer's review is asked for only while the pull request can still be
    /// reviewed, and only on GitHub's word: a status that was not read says nothing of it.</summary>
    [Theory]
    [InlineData(PullRequestState.Open, true, true, true)]
    [InlineData(PullRequestState.Draft, true, true, true)]
    [InlineData(PullRequestState.Open, false, true, false)]
    [InlineData(PullRequestState.Open, true, false, false)]
    [InlineData(PullRequestState.Merged, true, true, false)]
    [InlineData(PullRequestState.Closed, true, true, false)]
    public void The_viewers_review_is_requested_only_on_a_read_open_pull_request_that_asks_for_it(
        PullRequestState state, bool requested, bool statusRead, bool expected)
    {
        var mapped = RepositoryManagementGoalPullRequests.Map(Pull(5, state) with
        {
            ViewerReviewRequested = requested,
            StatusRead = statusRead
        });

        Assert.Equal(expected, mapped.ReviewRequested);
    }

    [Fact]
    public void A_pull_request_nothing_read_has_no_review_request()
    {
        Assert.False(GoalPullRequestDto.Unread(new(Backlog, "5")).ReviewRequested);
    }

    [Fact]
    public async Task The_reading_carries_the_viewers_review_request_through_the_contract()
    {
        var contract = new InMemoryPullRequests().Holds(Pull(11) with { ViewerReviewRequested = true });
        var reader = new RepositoryManagementGoalPullRequests(() => contract);

        var answer = Assert.Single(await reader.ReadAsync([new(Backlog, "#11")], TestContext.Current.CancellationToken));

        Assert.True(answer.ReviewRequested);
    }

    /// <summary>An archived repository is never read again, so a kept request for the
    /// viewer's review would stand for good on a pull request nobody can review: the
    /// answer drops it and keeps the rest of what was kept.</summary>
    [Fact]
    public async Task An_archived_repositorys_open_pull_request_does_not_ask_for_the_viewers_review()
    {
        var contract = new InMemoryPullRequests().Holds(Pull(8) with { RepositoryFullName = "JSdotNet/Archify", ViewerReviewRequested = true });
        var reader = new RepositoryManagementGoalPullRequests(() => contract, () => new ArchivedRepositories("jsdotnet/archify"));

        var answer = Assert.Single(await reader.ReadAsync([new("JSdotNet/Archify", "8")], TestContext.Current.CancellationToken));

        Assert.Equal((LinkedPullRequestState.Open, true, false), (answer.State, answer.InArchivedRepository, answer.ReviewRequested));
    }

    private static PullRequestStatus Pull(int number, PullRequestState state = PullRequestState.Open) =>
        new(Backlog, number, $"https://github.com/{Backlog}/pull/{number}", $"Pull request {number}", state, $"feature-{number}", "main", "octo", false, Noon);

    private sealed class ArchivedRepositories(params string[] archived) : IRegisteredRepositories
    {
        public bool IsConfigured => true;

        public IReadOnlyList<string> Active => [Backlog];

        public bool IsArchived(string repositoryFullName) =>
            archived.Contains(repositoryFullName, StringComparer.OrdinalIgnoreCase);
    }
}
