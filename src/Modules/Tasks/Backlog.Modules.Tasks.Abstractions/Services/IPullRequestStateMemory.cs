namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>How a pull request ended, as far as GitHub last said.</summary>
public enum PullRequestOutcome
{
    /// <summary>Merged. Final: GitHub never un-merges a pull request.</summary>
    Merged,

    /// <summary>Closed without merging. Not final: a closed pull request can be
    /// reopened, so it is asked about again now and then.</summary>
    Closed
}

/// <summary>One pull request's remembered outcome, and when GitHub said so.</summary>
/// <param name="Repository">The repository as <c>owner/name</c>.</param>
/// <param name="Number">The pull request's number.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="ReadAt">When the read that said so came back.</param>
public sealed record RememberedPullRequest(string Repository, int Number, PullRequestOutcome Outcome, DateTimeOffset ReadAt)
{
    /// <summary>The branch a merged pull request merged into, as the read said, or
    /// null when it did not say. With <see cref="DefaultBranch"/>, what decides whether
    /// the merge finishes an entry that records it.</summary>
    public string? BaseRefName { get; init; }

    /// <summary>The repository's default branch at the read, or null.</summary>
    public string? DefaultBranch { get; init; }

    /// <summary>
    /// The entries whose finish on this merge is settled: moved to Done, or found to be
    /// entries the merge does not finish. Per entry, because one pull request can land
    /// the work of several, and a merge remembered for the pull request must not stand
    /// in for a move an entry never had — one still owed when the app quit, or one
    /// recorded on the pull request after it merged.
    /// </summary>
    public IReadOnlyList<Guid> SettledEntries { get; init; } = [];
}

/// <summary>
/// The outcomes of the pull requests the backlog's work links have read, kept on this
/// device so a merged or closed link draws as such from the first frame after a
/// restart — offline too — instead of waiting on GitHub again.
/// <para>
/// Derived and discardable, like every workspace cache (local ADRs 0004 and 0015): it
/// lives in app storage, never in the entry text, the replica, or a repository, and
/// losing it costs one read per pull request. It holds the end state alone — never the
/// checks, the review or the auto-merge request, because a stale one of those would
/// offer a merge that is no longer allowed — and never a credential (local ADR 0031).
/// </para>
/// <para>
/// Synchronous: it is a local read once per list and a small write when a pull
/// request is first seen ending. A reader that cannot read or write answers as if it
/// held nothing; it never throws.
/// </para>
/// </summary>
public interface IPullRequestStateMemory
{
    /// <summary>Everything remembered. Empty when nothing is, or nothing could be read.</summary>
    IReadOnlyList<RememberedPullRequest> ReadAll();

    /// <summary>Remembers one outcome, replacing whatever was remembered for the same
    /// pull request (the repository compared without regard to case).</summary>
    void Remember(RememberedPullRequest pullRequest);

    /// <summary>Forgets one pull request — a closed one GitHub now says is open again.</summary>
    void Forget(string repository, int number);
}
