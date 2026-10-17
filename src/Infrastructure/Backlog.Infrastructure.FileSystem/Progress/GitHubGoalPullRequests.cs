using System.Globalization;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Progress;

/// <summary>
/// Progress's <see cref="IGoalPullRequestReader"/>, answered from GitHub through the
/// read by number with each pull request's status
/// (<see cref="GitHubIntegration.ListLinkedPullRequestStatusesAsync"/>): its state, merge
/// day, check counts and failing check names, approvals against the approvals its base
/// branch requires, author and the day it was opened
/// (<c>.devbook/domain/progress/dependencies.md</c>,
/// <c>.devbook/domain/progress/features.md#pull-request-status</c>).
/// <para>
/// It keeps nothing of its own: it reads through
/// <see cref="GitHubIntegration.ReadLinkedPullRequestStatusesAsync"/>, the adapter's
/// status cache, which keeps answers on the terms the In progress view's linked read is
/// kept on: a merged pull request is read once and kept for the life of the process, a
/// number GitHub answered without is not asked about again, and any other is read again
/// once its answer is older than <see cref="FreshFor"/>. A pull request GitHub could not
/// be asked about — no GitHub configured, a repository that refused, a number that is not
/// a pull request — answers <see cref="LinkedPullRequestState.Unknown"/> with no checks,
/// no approvals and no author, the module's own answer, so a task is then done by its
/// status alone. A pull request in an archived repository (local ADR 0029) is not asked
/// about: it answers whatever that cache last kept for it, stale or not, and unread
/// otherwise. It never throws for GitHub's sake.
/// </para>
/// </summary>
/// <param name="gitHub">The GitHub adapter, resolved per call; null reads nothing.</param>
public sealed class GitHubGoalPullRequests(Func<GitHubIntegration?> gitHub) : IGoalPullRequestReader
{
    /// <summary>How long an answer that is not merged is kept before it is asked again —
    /// the In progress view's own freshness, so the two never disagree for long.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(1);

    public async Task<IReadOnlyList<GoalPullRequestDto>> ReadAsync(
        IReadOnlyList<PullRequestRefDto> pullRequests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequests);

        var configured = gitHub() is { IsConfigured: true } integration ? integration : null;
        var wanted = pullRequests
            .Select(Reference)
            .OfType<GitHubPullRequestReference>()
            .DistinctBy(Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var answers = new Dictionary<string, GoalPullRequestDto>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count > 0 && configured is not null)
        {
            // An archived repository is not read from GitHub (local ADR 0029). Left out of
            // the question rather than asked and answered empty, which the shared cache
            // would keep as answered without and so overwrite an answer already known with
            // Unknown; Progress keeps the repository and whatever was last read for it.
            var archived = wanted
                .Where(reference => configured.ResolveRepository(reference.RepositoryFullName) is { Archived: true })
                .ToList();
            var active = wanted.Except(archived).ToList();

            if (archived.Count > 0)
            {
                Answer(answers, archived, configured.KeptLinkedPullRequestStatuses(archived), answeredWithout: false);
            }

            if (active.Count > 0)
            {
                try
                {
                    var listing = await configured.ReadLinkedPullRequestStatusesAsync(active, FreshFor, cancellationToken).ConfigureAwait(false);
                    Answer(answers, active, listing, answeredWithout: true);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // GitHub not answering is the module's Unknown, not a failed reading: what
                    // was not read is asked about again on the next reading.
                }
            }
        }

        // The task's own spelling of the repository and number, so a caller matching its
        // answers back to its links finds them.
        return [.. pullRequests.Select(pull => answers.TryGetValue(Key(pull), out var answer)
            ? answer with { Repository = pull.Repository, Number = pull.Number }
            : GoalPullRequestDto.Unread(pull))];
    }

    /// <summary>Puts <paramref name="listing"/>'s answer to each of <paramref name="asked"/>
    /// into <paramref name="answers"/>. With <paramref name="answeredWithout"/>, a link its
    /// repository answered without — not a pull request, or deleted — answers Unknown; a
    /// listing of what was kept, which asked nothing, leaves such a link unread.</summary>
    private static void Answer(
        Dictionary<string, GoalPullRequestDto> answers,
        IReadOnlyList<GitHubPullRequestReference> asked,
        GitHubLinkedPullRequestListing listing,
        bool answeredWithout)
    {
        var refused = listing.Failures.Select(failure => failure.RepositoryFullName.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var whole = refused.Contains("GitHub");

        foreach (var reference in asked)
        {
            // The one in its own repository, or — renamed since it was linked — the only
            // one with its number.
            var pull = listing.PullRequests.FirstOrDefault(candidate =>
                    candidate.Number == reference.Number
                    && string.Equals(candidate.RepositoryFullName, reference.RepositoryFullName, StringComparison.OrdinalIgnoreCase))
                ?? (listing.PullRequests.Count(candidate => candidate.Number == reference.Number) == 1
                    ? listing.PullRequests.Single(candidate => candidate.Number == reference.Number)
                    : null);

            if (pull is not null)
            {
                answers[Key(reference)] = Map(pull) with
                {
                    Repository = reference.RepositoryFullName,
                    Number = reference.Number.ToString(CultureInfo.InvariantCulture)
                };
            }
            else if (answeredWithout && !whole && !refused.Contains(reference.RepositoryFullName.Trim()))
            {
                answers[Key(reference)] = GoalPullRequestDto.Unread(
                    new PullRequestRefDto(reference.RepositoryFullName, reference.Number.ToString(CultureInfo.InvariantCulture)));
            }
        }
    }

    /// <summary>
    /// One pull request GitHub answered, as Progress reads it: the state; the merge and
    /// opened days in local time; the check counts with the failing checks' names, or
    /// none where the head commit has no checks or they were refused; the approvals
    /// received and the approvals the base branch requires, null where no rule requires
    /// one; and the author.
    /// </summary>
    public static GoalPullRequestDto Map(GitHubLinkedPullRequest pull)
    {
        ArgumentNullException.ThrowIfNull(pull);

        return new GoalPullRequestDto(
            pull.RepositoryFullName,
            pull.Number.ToString(CultureInfo.InvariantCulture),
            State(pull.State),
            pull.MergedAt is { } merged ? DateOnly.FromDateTime(merged.ToLocalTime().Date) : null,
            pull.CheckCounts is { } checks
                ? new GoalPullRequestChecksDto(checks.Passed, checks.Failed, checks.Pending, [.. pull.FailingChecks])
                : null,
            new GoalPullRequestApprovalsDto(pull.Reviews.Approvals, pull.Reviews.Required),
            string.IsNullOrWhiteSpace(pull.AuthorLogin) ? null : pull.AuthorLogin,
            pull.CreatedAt is { } opened ? DateOnly.FromDateTime(opened.ToLocalTime().Date) : null);
    }

    private static GitHubPullRequestReference? Reference(PullRequestRefDto pull) =>
        int.TryParse(pull.Number.Trim().TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
        && number > 0
        && pull.Repository.Contains('/', StringComparison.Ordinal)
            ? new GitHubPullRequestReference(pull.Repository.Trim(), number)
            : null;

    private static LinkedPullRequestState State(GitHubItemState state) => state switch
    {
        GitHubItemState.Open => LinkedPullRequestState.Open,
        GitHubItemState.Draft => LinkedPullRequestState.Draft,
        GitHubItemState.Merged => LinkedPullRequestState.Merged,
        GitHubItemState.Closed => LinkedPullRequestState.Closed,
        _ => LinkedPullRequestState.Unknown
    };

    private static string Key(PullRequestRefDto pull) =>
        Reference(pull) is { } reference ? Key(reference) : $"{pull.Repository.Trim()}#{pull.Number.Trim()}";

    private static string Key(GitHubPullRequestReference reference) =>
        $"{reference.RepositoryFullName.Trim()}#{reference.Number.ToString(CultureInfo.InvariantCulture)}";
}
