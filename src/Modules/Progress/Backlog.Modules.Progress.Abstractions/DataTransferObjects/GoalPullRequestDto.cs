namespace Backlog.Modules.Progress.Abstractions.DataTransferObjects;

/// <summary>
/// What a pull request a goal counts, or a task it holds links, says about itself: its
/// state, the day it merged, how its checks stand and how its approvals stand against
/// what its base branch requires (<c>.devbook/domain/progress/features.md#pull-request-status</c>).
/// <see cref="LinkedPullRequestState.Unknown"/>, no checks and no approvals until
/// something reads it.
/// </summary>
/// <param name="Repository">The repository as the caller asked for it, by <c>owner/name</c>.</param>
/// <param name="Number">The number as the caller asked for it.</param>
/// <param name="MergedOn">The day it merged, in local time; null unless merged.</param>
/// <param name="Checks">How the head commit's checks stand; null where it has none or
/// they were not read.</param>
/// <param name="Approvals">How many approvals it has and how many its base branch
/// requires; null only for a pull request nothing read
/// (<see cref="LinkedPullRequestState.Unknown"/>).</param>
public sealed record GoalPullRequestDto(
    string Repository,
    string Number,
    LinkedPullRequestState State,
    DateOnly? MergedOn,
    GoalPullRequestChecksDto? Checks,
    GoalPullRequestApprovalsDto? Approvals)
{
    /// <summary>A pull request nothing has read: unknown, with no checks and no approvals.</summary>
    public static GoalPullRequestDto Unread(PullRequestRefDto pullRequest)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        return new(pullRequest.Repository, pullRequest.Number, LinkedPullRequestState.Unknown, null, null, null);
    }
}

/// <summary>
/// How far a pull request's head commit checks have got — the row's <c>passed/total</c>
/// — and the names of the ones that failed.
/// </summary>
/// <param name="FailingNames">The failed checks by name, each once, in the order GitHub
/// lists them. It may name fewer than <paramref name="Failed"/> counts on a commit with
/// more checks than one read lists.</param>
public sealed record GoalPullRequestChecksDto(int Passed, int Failed, int Pending, IReadOnlyList<string> FailingNames)
{
    /// <summary>Every check counted: the row's denominator.</summary>
    public int Total => Passed + Failed + Pending;
}

/// <summary>
/// A pull request's approvals against what its base branch requires — the row's
/// <c>approvals/required</c>.
/// </summary>
/// <param name="Received">The approving reviews among the latest opinionated reviews
/// of people whose review counts towards the merge.</param>
/// <param name="Required">The approvals the base branch's protection rule or a ruleset
/// requires; null where no rule requires one, or the rules could not be read — no rule
/// means no required total.</param>
public sealed record GoalPullRequestApprovalsDto(int Received, int? Required);
