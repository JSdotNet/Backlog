namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// The entries a merged pull request's finish is settled for, kept on this device.
/// </summary>
/// <param name="Repository">The repository as <c>owner/name</c>.</param>
/// <param name="Number">The pull request's number.</param>
/// <param name="SettledEntries">
/// The entries whose finish on this merge is settled: moved to Done, or found to be
/// entries the merge does not finish. Per entry, because one pull request can land the
/// work of several, and a merge settled for one entry must not stand in for a move
/// another never had — one still owed when the app quit, or one recorded on the pull
/// request after it merged.
/// </param>
public sealed record PullRequestFinish(string Repository, int Number, IReadOnlyList<Guid> SettledEntries);

/// <summary>
/// Which of a merged pull request's entries its finish is settled for — Tasks' own fact
/// about its own entries (local ADR 0042 §3), so a restart neither moves an entry to
/// Done twice nor forgets a move still owed.
/// <para>
/// Not the merge itself. That a pull request merged, and where, is Repository
/// Management's settled fact, read through <see cref="ILinkedPullRequestStates.Kept"/>;
/// this keeps only what the task list did about it.
/// </para>
/// <para>
/// Derived and discardable, like every workspace cache (local ADRs 0004 and 0015): it
/// lives in app storage, never in the entry text, the replica, or a repository.
/// Synchronous: a local read once per run and a small write when a move lands. A
/// memory that cannot read or write answers as if it held nothing; it never throws.
/// </para>
/// </summary>
public interface IPullRequestFinishMemory
{
    /// <summary>Every pull request with a settled entry. Empty when nothing is, or
    /// nothing could be read.</summary>
    IReadOnlyList<PullRequestFinish> ReadAll();

    /// <summary>Records that the merge of <paramref name="repository"/>#<paramref name="number"/>
    /// is settled for <paramref name="entry"/>, beside the entries already settled for it
    /// (the repository compared without regard to case).</summary>
    void Settle(string repository, int number, Guid entry);
}
