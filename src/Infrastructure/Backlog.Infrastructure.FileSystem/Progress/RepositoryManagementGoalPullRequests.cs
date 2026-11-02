using System.Globalization;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Progress;

/// <summary>
/// Progress's <see cref="IGoalPullRequestReader"/>, answered through Repository
/// Management's contract (<see cref="IPullRequests"/>, local ADR 0042 §5): each pull
/// request's state, merge day, check counts and failing check names, and approvals
/// against the approvals its base branch requires
/// (<c>.devbook/domain/progress/dependencies.md</c>,
/// <c>.devbook/domain/progress/features.md#pull-request-status</c>).
/// <para>
/// It keeps nothing of its own: it reads by number through the contract's one cache,
/// which every other reader of a pull request shares — the Pull requests pane, a task's
/// row and side panel, the session card — so a pull request one of them read within the
/// minute is not read again here (V3), and an archived repository is never read (local
/// ADR 0029). A pull request the contract could not answer for — none composed, no
/// repository registered, a repository that refused, a number that is not a pull
/// request — answers <see cref="LinkedPullRequestState.Unknown"/> with no checks, no
/// approvals and no merge day, the module's own answer, so a task is then done by its
/// status alone. A pull request in an archived repository answers what was kept for it,
/// flagged <see cref="GoalPullRequestDto.InArchivedRepository"/> so a held-back pull
/// request there is never settled. It never throws for GitHub's sake.
/// </para>
/// </summary>
/// <param name="pullRequests">The contract, resolved per call; null reads nothing.</param>
/// <param name="repositories">The registry, resolved per call; null flags none archived.</param>
public sealed class RepositoryManagementGoalPullRequests(
    Func<IPullRequests?> pullRequests,
    Func<IRegisteredRepositories?>? repositories = null)
    : IGoalPullRequestReader
{
    public async Task<IReadOnlyList<GoalPullRequestDto>> ReadAsync(
        IReadOnlyList<PullRequestRefDto> pullRequests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequests);

        var wanted = pullRequests
            .Select(Reference)
            .OfType<PullRequestReference>()
            .DistinctBy(reference => reference.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var answers = new Dictionary<string, GoalPullRequestDto>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count > 0 && Contract() is { IsConfigured: true } contract)
        {
            try
            {
                var listing = await contract.ReadAsync(wanted, PullRequestReadOptions.Default, cancellationToken).ConfigureAwait(false);

                // The one in its own repository: the contract answers under the name it
                // was asked by, so a pull request under another name is another
                // repository's, however alone its number is in the reading.
                foreach (var pull in listing.PullRequests) answers.TryAdd(pull.Reference.Key, Map(pull));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Not answered is the module's Unknown, not a failed reading: what was
                // not read is asked about again on the next reading.
            }
        }

        // The task's own spelling of the repository and number, so a caller matching its
        // answers back to its links finds them; one from an archived repository says so,
        // since its answer was kept, not read.
        var registry = repositories?.Invoke();
        return [.. pullRequests.Select(pull => (Reference(pull) is { } reference && answers.TryGetValue(reference.Key, out var answer)
            ? answer with { Repository = pull.Repository, Number = pull.Number }
            : GoalPullRequestDto.Unread(pull)) with { InArchivedRepository = registry?.IsArchived(pull.Repository.Trim()) ?? false })];
    }

    /// <summary>
    /// One pull request the contract answered, as Progress reads it: the state; the merge
    /// day in local time; the check counts with the failing checks' names, or none where
    /// the head commit has none or they were refused; and the approvals received and the
    /// approvals the base branch requires, null where no rule requires one. A pull request
    /// whose status was not read — remembered merged across a restart, or answered over
    /// REST — has neither checks nor approvals: nothing read says them.
    /// </summary>
    public static GoalPullRequestDto Map(PullRequestStatus pull)
    {
        ArgumentNullException.ThrowIfNull(pull);

        return new GoalPullRequestDto(
            pull.RepositoryFullName,
            pull.Number.ToString(CultureInfo.InvariantCulture),
            State(pull.State),
            pull.MergedAt is { } merged && pull.State is PullRequestState.Merged ? DateOnly.FromDateTime(merged.ToLocalTime().Date) : null,
            pull.StatusRead && pull.CheckCounts is { } checks
                ? new GoalPullRequestChecksDto(checks.Passed, checks.Failed, checks.Pending, pull.FailingCheckNames)
                : null,
            pull.StatusRead ? new GoalPullRequestApprovalsDto(pull.Approvals.Approvals, pull.Approvals.Required) : null);
    }

    private IPullRequests? Contract() => pullRequests();

    private static PullRequestReference? Reference(PullRequestRefDto pull) =>
        int.TryParse(pull.Number.Trim().TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
        && number > 0
        && pull.Repository.Contains('/', StringComparison.Ordinal)
            ? new PullRequestReference(pull.Repository.Trim(), number)
            : null;

    private static LinkedPullRequestState State(PullRequestState state) => state switch
    {
        PullRequestState.Open => LinkedPullRequestState.Open,
        PullRequestState.Draft => LinkedPullRequestState.Draft,
        PullRequestState.Merged => LinkedPullRequestState.Merged,
        PullRequestState.Closed => LinkedPullRequestState.Closed,
        _ => LinkedPullRequestState.Unknown
    };
}
