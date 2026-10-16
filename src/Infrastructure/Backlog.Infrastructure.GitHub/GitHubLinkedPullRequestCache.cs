using System.Globalization;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// The one cache of linked pull requests read by number, which every reader of them
/// shares through <see cref="GitHubIntegration.ReadLinkedPullRequestsAsync"/> — the shell's
/// In progress reader and Progress's sprint reader ask about the same links, so in one
/// refresh a pull request is asked about once.
/// <para>
/// On these terms, whoever asks:
/// </para>
/// <list type="bullet">
/// <item>a merged pull request cannot change again, so it is read once and kept for the
/// life of the process;</item>
/// <item>a number its repository answered without — not a pull request, or deleted — is
/// not asked about again either;</item>
/// <item>any other answer is reused while it is younger than the caller's
/// <c>freshFor</c>, and so is a read of it still out that started inside that time:
/// the caller waits for that read rather than sending a second one;</item>
/// <item>a repository that refused, or a read that threw, is kept nowhere, so the next
/// read asks again.</item>
/// </list>
/// <para>
/// Each answer is kept under the reference it was asked by, not the name GitHub answers
/// with: a renamed repository answers under its new name, and a key the next read never
/// asks by would send the same question again every time. What the caller gets back is
/// GitHub's answer as it came.
/// </para>
/// </summary>
/// <param name="read">The read by number this cache stands in front of.</param>
internal sealed class GitHubLinkedPullRequestCache(
    Func<IReadOnlyList<GitHubPullRequestReference>, CancellationToken, Task<GitHubLinkedPullRequestListing>> read,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Answer> _answers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The pull requests <paramref name="links"/> name, from what is kept where it may
    /// be, from GitHub for the rest. A link left out of the answer with its repository
    /// not among the failures is one GitHub answered without.
    /// </summary>
    public async Task<GitHubLinkedPullRequestListing> ReadAsync(
        IEnumerable<GitHubPullRequestReference> links,
        TimeSpan freshFor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(links);

        var wanted = links
            .Where(link => link.Number > 0 && !string.IsNullOrWhiteSpace(link.RepositoryFullName))
            .Select(link => link with { RepositoryFullName = link.RepositoryFullName.Trim() })
            .DistinctBy(Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (wanted.Count == 0) return GitHubLinkedPullRequestListing.Empty;

        var waits = new List<Task<IReadOnlyDictionary<string, Answer>>>();
        var kept = new List<Answer>();
        var ask = new List<GitHubPullRequestReference>();
        Pending? started = null;

        lock (_gate)
        {
            var now = _clock.GetUtcNow();

            foreach (var link in wanted)
            {
                var key = Key(link);
                if (_answers.TryGetValue(key, out var answer) && answer.Holds(now, freshFor)) kept.Add(answer);
                else if (_pending.TryGetValue(key, out var pending) && now - pending.StartedAt < freshFor) waits.Add(pending.Read.Task);
                else ask.Add(link);
            }

            if (ask.Count > 0)
            {
                started = new Pending(now, new TaskCompletionSource<IReadOnlyDictionary<string, Answer>>(TaskCreationOptions.RunContinuationsAsynchronously));
                foreach (var link in ask) _pending[Key(link)] = started;
                waits.Add(started.Read.Task);
            }
        }

        // Read on no caller's token: another caller may come to wait on it, and one
        // leaving must not cancel the answer the other is waiting for.
        if (started is not null) _ = ReadAndKeepAsync(ask, started);

        var landed = await Task.WhenAll(waits).WaitAsync(cancellationToken).ConfigureAwait(false);

        var keys = wanted.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var answers = kept
            .Concat(landed.SelectMany(batch => batch.Where(entry => keys.Contains(entry.Key)).Select(entry => entry.Value)))
            .ToList();

        return new GitHubLinkedPullRequestListing(
            [.. answers.Where(answer => answer.PullRequest is not null).Select(answer => answer.PullRequest!).Distinct()],
            [.. answers.Where(answer => answer.Failure is not null).Select(answer => answer.Failure!).Distinct()]);
    }

    /// <summary>
    /// What is kept for <paramref name="links"/>, however old, and nothing read: the
    /// answer for a link whose repository is no longer read (an archived one, local
    /// ADR 0029), so a reader keeps what it last knew rather than forgetting it.
    /// </summary>
    public GitHubLinkedPullRequestListing Kept(IEnumerable<GitHubPullRequestReference> links)
    {
        ArgumentNullException.ThrowIfNull(links);

        lock (_gate)
        {
            return new GitHubLinkedPullRequestListing(
                [
                    .. links
                        .Where(link => link.Number > 0 && !string.IsNullOrWhiteSpace(link.RepositoryFullName))
                        .Select(link => _answers.GetValueOrDefault(Key(link))?.PullRequest)
                        .OfType<GitHubLinkedPullRequest>()
                        .Distinct()
                ],
                []);
        }
    }

    private async Task ReadAndKeepAsync(IReadOnlyList<GitHubPullRequestReference> ask, Pending started)
    {
        var answers = new Dictionary<string, Answer>(StringComparer.OrdinalIgnoreCase);
        try
        {
            GitHubLinkedPullRequestListing listing;
            try
            {
                listing = await read(ask, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                listing = new GitHubLinkedPullRequestListing([], [new GitHubRepositoryFailure("GitHub", exception.Message)]);
            }

            var whole = listing.Failures.FirstOrDefault(failure => failure.RepositoryFullName == "GitHub");
            var refused = listing.Failures
                .GroupBy(failure => failure.RepositoryFullName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var readAt = _clock.GetUtcNow();

            foreach (var link in ask)
            {
                var pull = Find(listing.PullRequests, link);
                var failure = pull is not null ? null : refused.GetValueOrDefault(link.RepositoryFullName) ?? whole;
                answers[Key(link)] = new Answer(pull, failure, readAt);
            }
        }
        finally
        {
            lock (_gate)
            {
                foreach (var (key, answer) in answers.Where(entry => entry.Value.Failure is null))
                {
                    _answers[key] = answer;
                }

                foreach (var link in ask)
                {
                    if (_pending.TryGetValue(Key(link), out var pending) && ReferenceEquals(pending, started)) _pending.Remove(Key(link));
                }
            }

            started.Read.SetResult(answers);
        }
    }

    /// <summary>The answer to <paramref name="link"/>: the one in its own repository, or —
    /// a repository renamed since it was linked — the only one with its number.</summary>
    private static GitHubLinkedPullRequest? Find(IReadOnlyList<GitHubLinkedPullRequest> pulls, GitHubPullRequestReference link) =>
        pulls.FirstOrDefault(pull => pull.Number == link.Number
                                     && string.Equals(pull.RepositoryFullName, link.RepositoryFullName, StringComparison.OrdinalIgnoreCase))
        ?? (pulls.Count(pull => pull.Number == link.Number) == 1 ? pulls.Single(pull => pull.Number == link.Number) : null);

    private static string Key(GitHubPullRequestReference link) =>
        $"{link.RepositoryFullName.Trim()}#{link.Number.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>One link's answer: the pull request, the refusal that kept it unread, or
    /// neither — answered without it.</summary>
    private sealed record Answer(GitHubLinkedPullRequest? PullRequest, GitHubRepositoryFailure? Failure, DateTimeOffset ReadAt)
    {
        public bool Holds(DateTimeOffset now, TimeSpan freshFor) =>
            PullRequest is null || PullRequest.State is GitHubItemState.Merged || now - ReadAt < freshFor;
    }

    /// <summary>A read out: when it began, and the answers it lands with. Compared by
    /// reference, so a read that began later under the same key is never taken for it.</summary>
    private sealed class Pending(DateTimeOffset startedAt, TaskCompletionSource<IReadOnlyDictionary<string, Answer>> read)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;

        public TaskCompletionSource<IReadOnlyDictionary<string, Answer>> Read { get; } = read;
    }
}
