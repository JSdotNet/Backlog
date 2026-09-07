namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Keeps a local copy of a repository branch, so knowledge can be read out of a
/// repository nobody has cloned.
/// <para>
/// The port is declared here and implemented in
/// <c>Backlog.Infrastructure.FileSystem</c>, which is the split
/// <see cref="ILocalGitRepositoryService"/> already lives on either side of: a
/// contract phrased in terms of a <see cref="GitHubRepositoryRef"/> belongs
/// where that type is, while the half that decides where bytes land on disk
/// belongs with the workspace. Declaring it here is also what keeps the
/// dependency out of the knowledge panels' project, which already sees this
/// adapter and would otherwise need a second one.
/// </para>
/// <para>
/// What the implementation puts on disk is the repository tree exactly as the
/// branch has it, not just the knowledge folders. Knowledge folders are
/// configurable and can be pointed anywhere, the instructions area <em>is</em>
/// the repository root, and the diagram tooling reads <c>tools/</c> — so
/// "extract the folders we need" would be a guess, and would be wrong the first
/// time somebody re-pointed a folder.
/// </para>
/// </summary>
public interface IKnowledgeSnapshotCache
{
    /// <summary>
    /// Where a repository's branch snapshot would be, whether or not one has
    /// been fetched.
    /// <para>
    /// Synchronous and offline on purpose. It is called during folder
    /// resolution, which every panel does on every load, and resolution that
    /// reached the network would make opening a panel wait on GitHub.
    /// </para>
    /// </summary>
    string SnapshotPath(GitHubRepositoryRef repository, string? branch);

    /// <summary>What is on disk for this branch, or null when nothing has been
    /// fetched yet.</summary>
    KnowledgeSnapshot? TryRead(GitHubRepositoryRef repository, string? branch);

    /// <summary>
    /// Brings the snapshot up to the branch's current commit, downloading only
    /// when the commit on disk is not the one the branch points at.
    /// </summary>
    Task<KnowledgeSnapshotResult> FetchAsync(
        GitHubRepositoryRef repository,
        string? branch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the branch has moved past the snapshot on disk. Contacts the
    /// network — one cheap call for the branch head, no download — so it runs
    /// only when somebody asks.
    /// </summary>
    Task<KnowledgeSnapshotResult> CheckAsync(
        GitHubRepositoryRef repository,
        string? branch,
        CancellationToken cancellationToken = default);
}

/// <summary>One snapshot on disk: which commit of which branch, and when it was
/// taken.</summary>
public sealed record KnowledgeSnapshot(string Branch, string Sha, DateTimeOffset FetchedUtc);

/// <summary>
/// What came of asking about, or fetching, a snapshot.
/// <para>
/// A message rather than a thrown exception for everything a person can act on —
/// a branch that no longer exists, a repository this machine cannot reach — and
/// the message is the one the adapter produced, so the reason a snapshot could
/// not be taken is GitHub's reason rather than a paraphrase.
/// </para>
/// </summary>
/// <param name="Snapshot">What is on disk now. Null when nothing is.</param>
/// <param name="Updated">Whether this call changed what is on disk.</param>
/// <param name="Behind">Whether the branch has moved past what is on disk.</param>
/// <param name="Message">What to tell somebody, or null when there is nothing to say.</param>
public sealed record KnowledgeSnapshotResult(
    KnowledgeSnapshot? Snapshot,
    bool Updated,
    bool Behind,
    string? Message)
{
    public static KnowledgeSnapshotResult Failed(KnowledgeSnapshot? snapshot, string message) =>
        new(snapshot, false, false, message);
}
