using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
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
    public void A_closed_pull_request_maps_closed()
    {
        Assert.Equal(LinkedPullRequestState.Closed, RepositoryManagementGoalPullRequests.Map(Pull(3, PullRequestState.Closed)).State);
    }

    private static PullRequestStatus Pull(int number, PullRequestState state = PullRequestState.Open) =>
        new(Backlog, number, $"https://github.com/{Backlog}/pull/{number}", $"Pull request {number}", state, $"feature-{number}", "main", "octo", false, Noon);
}
