using System.Globalization;

using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Modules.Tasks.Services;

/// <summary>
/// Tasks' answer to which task a session or a pull request belongs to, and which pull
/// requests the tasks link (local ADR 0042, V8), over the window's task rows as
/// <see cref="ITaskLookupCandidates"/> hands them.
/// <para>
/// The screens ask once per cell they draw, so the matching is built once per list of
/// candidates and kept while the Tasks state hands back the same list instance. The
/// list and what was built from it are kept as one reference, so a reader on another
/// thread never pairs one list with another's matching.
/// </para>
/// </summary>
internal sealed class TaskLookup(ITaskLookupCandidates candidates) : ITaskLookup
{
    private readonly ITaskLookupCandidates _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));

    private Built? _built;

    public TaskReference? TaskForSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;

        var task = Current().Candidates.FirstOrDefault(candidate =>
            candidate.SessionIds.Contains(sessionId, StringComparer.OrdinalIgnoreCase));

        return ReferenceTo(task);
    }

    public TaskReference? TaskForPullRequest(PullRequestTaskQuestion pullRequest, bool mineOnly = false) =>
        ReferenceTo(Match(pullRequest, mineOnly)?.Candidate);

    public IReadOnlyList<TaskPullRequestLink> LinkedPullRequests() => Current().Linked;

    /// <summary>The match with how it was found, for the tests that pin the order down.</summary>
    internal PullRequestTaskMatch? Match(PullRequestTaskQuestion pullRequest, bool mineOnly = false)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);

        return Current().Matching.Match(pullRequest, mineOnly);
    }

    private Built Current()
    {
        var list = _candidates.Candidates;
        var built = _built;
        if (built is not null && ReferenceEquals(built.Candidates, list)) return built;

        built = new Built(list, new PullRequestTaskMatching(list), LinkedOf(list));
        _built = built;
        return built;
    }

    private static IReadOnlyList<TaskPullRequestLink> LinkedOf(IReadOnlyList<TaskLookupCandidate> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var linked = new List<TaskPullRequestLink>();
        foreach (var link in candidates.SelectMany(candidate => candidate.PullRequests))
        {
            if (seen.Add(string.Create(CultureInfo.InvariantCulture, $"{link.RepositoryFullName.Trim()}#{link.Number}"))) linked.Add(link);
        }

        return linked;
    }

    private static TaskReference? ReferenceTo(TaskLookupCandidate? candidate) =>
        candidate is null ? null : new TaskReference(candidate.EntryId, candidate.Title);

    private sealed record Built(
        IReadOnlyList<TaskLookupCandidate> Candidates,
        PullRequestTaskMatching Matching,
        IReadOnlyList<TaskPullRequestLink> Linked);
}
