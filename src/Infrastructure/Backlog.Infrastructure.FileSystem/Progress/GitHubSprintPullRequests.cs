using System.Globalization;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Progress;

/// <summary>
/// Progress's <see cref="ISprintPullRequestReader"/>, answered from GitHub through the
/// read by number the In progress view's linked pull requests already use
/// (<see cref="GitHubIntegration.ListLinkedPullRequestsAsync"/>): each pull request's
/// state, merge day, author and the day it was opened
/// (<c>.devbook/domain/progress/dependencies.md</c>).
/// <para>
/// It keeps nothing of its own: it reads through
/// <see cref="GitHubIntegration.ReadLinkedPullRequestsAsync"/>, the cache the In
/// progress view's reader reads through too, so the sprint view and the Now view asking
/// about the same links in one refresh ask GitHub once. On that cache's terms, a merged
/// pull request is read once and kept for the life of the process, a number GitHub
/// answered without is not asked about again, and any other is read again once its
/// answer is older than <see cref="FreshFor"/>. A pull request GitHub could not be asked
/// about — no GitHub configured, a repository that refused, a number that is not a pull
/// request — answers <see cref="LinkedPullRequestState.Unknown"/> with no author, the
/// module's own answer, so a member is then done by its task's status alone. It never
/// throws for GitHub's sake.
/// </para>
/// </summary>
/// <param name="gitHub">The GitHub adapter, resolved per call; null reads nothing.</param>
public sealed class GitHubSprintPullRequests(Func<GitHubIntegration?> gitHub) : ISprintPullRequestReader
{
    /// <summary>How long an answer that is not merged is kept before it is asked again —
    /// the In progress view's own freshness, so the two never disagree for long.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(1);

    public async Task<IReadOnlyList<LinkedPullRequestDto>> ReadAsync(
        IReadOnlyList<PullRequestRefDto> pullRequests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequests);

        var wanted = pullRequests
            .Select(Reference)
            .OfType<GitHubPullRequestReference>()
            .DistinctBy(Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var answers = new Dictionary<string, LinkedPullRequestDto>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count > 0 && gitHub() is { IsConfigured: true } configured)
        {
            try
            {
                var listing = await configured.ReadLinkedPullRequestsAsync(wanted, FreshFor, cancellationToken).ConfigureAwait(false);
                var refused = listing.Failures.Select(failure => failure.RepositoryFullName.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var whole = refused.Contains("GitHub");

                foreach (var reference in wanted)
                {
                    // The one in its own repository, or — renamed since it was linked —
                    // the only one with its number.
                    var pull = listing.PullRequests.FirstOrDefault(candidate =>
                            candidate.Number == reference.Number
                            && string.Equals(candidate.RepositoryFullName, reference.RepositoryFullName, StringComparison.OrdinalIgnoreCase))
                        ?? (listing.PullRequests.Count(candidate => candidate.Number == reference.Number) == 1
                            ? listing.PullRequests.Single(candidate => candidate.Number == reference.Number)
                            : null);

                    if (pull is not null)
                    {
                        answers[Key(reference)] = new LinkedPullRequestDto(
                            reference.RepositoryFullName,
                            reference.Number.ToString(CultureInfo.InvariantCulture),
                            State(pull.State),
                            pull.MergedAt is { } merged ? DateOnly.FromDateTime(merged.ToLocalTime().Date) : null,
                            string.IsNullOrWhiteSpace(pull.AuthorLogin) ? null : pull.AuthorLogin,
                            pull.CreatedAt is { } opened ? DateOnly.FromDateTime(opened.ToLocalTime().Date) : null);
                    }
                    else if (!whole && !refused.Contains(reference.RepositoryFullName.Trim()))
                    {
                        // Answered without it — not a pull request, or deleted.
                        answers[Key(reference)] = new LinkedPullRequestDto(
                            reference.RepositoryFullName,
                            reference.Number.ToString(CultureInfo.InvariantCulture),
                            LinkedPullRequestState.Unknown,
                            null,
                            null);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // GitHub not answering is the module's Unknown, not a failed sprint: what
                // was not read is asked about again on the next reading.
            }
        }

        // The task's own spelling of the repository and number, so a caller matching its
        // answers back to its links finds them.
        return [.. pullRequests.Select(pull => answers.TryGetValue(Key(pull), out var answer)
            ? answer with { Repository = pull.Repository, Number = pull.Number }
            : LinkedPullRequestDto.Unread(pull))];
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
