namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Keeps one login's AI-credit usage for calendar months that are over on disk,
/// so a dashboard read asks GitHub only about the month that is still running.
/// <para>
/// A month is stored whole and only once it is settled — see
/// <see cref="GitHubBillingClient"/> for what settled means — because the report
/// for a month that is still open moves every time somebody uses an assistant,
/// and the one for a month just ended can still move while GitHub's metering
/// catches up. What cannot move is a month a few days behind us, and that is the
/// only thing this holds.
/// </para>
/// <para>
/// Declared here and implemented in <c>Backlog.Infrastructure.FileSystem</c>, on
/// the same arrangement as <see cref="IPullRequestDetailCache"/>: the contract is
/// phrased in this adapter's types, and where the bytes land is the workspace's
/// decision.
/// </para>
/// <para>
/// Per login rather than per person. The billing client reads a separate report
/// for each identity this machine holds and adds them up; a month cached against
/// the sum would go stale the moment an account was added or removed, while one
/// per login is right whichever set is configured today.
/// </para>
/// <para>
/// Synchronous and non-throwing, like its siblings: an entry that cannot be read
/// is fetched, and one that cannot be written is fetched again next time.
/// </para>
/// </summary>
public interface IAiCreditUsageCache
{
    /// <summary>What is stored for this login and month, or null when nothing is —
    /// including when what is stored cannot be read or was written by an older
    /// version of this app.</summary>
    GitHubAiCreditUsage? TryRead(string login, int year, int month);

    /// <summary>Stores one settled month. Failure is not reported, because there is
    /// nothing a caller could usefully do about it.</summary>
    void Write(string login, int year, int month, GitHubAiCreditUsage usage);

    /// <summary>Drops every stored month for every login, so the next read asks
    /// GitHub about all of them again.</summary>
    void Forget();
}
