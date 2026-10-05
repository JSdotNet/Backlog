using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.Services;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How the two GitHub settings documents are written on the task feed (local ADR
/// 0021): the kind token each carries, the one id each is stored under, and the title
/// the activity log shows. The wire half of the arrangement only — what a document
/// says, and which copy wins, is the GitHub settings store's, behind
/// <see cref="IGitHubSettingsReplication"/>.
/// <para>
/// The kind tokens are literals duplicated where they are read, for the reason
/// <see cref="RoadmapReplicaDocuments"/> gives: the phone's <c>TaskFold</c> skips the
/// same two, and neither side may see the other.
/// </para>
/// </summary>
public static class GitHubReplicaDocuments
{
    /// <summary>The kind token the repository registry document carries.</summary>
    public const string RegistryType = "repository-registry";

    /// <summary>The kind token the GitHub accounts document carries.</summary>
    public const string AccountsType = "github-accounts";

    /// <summary>
    /// The registry document's id — the same literal on every device, chosen once and
    /// <b>never to be changed</b>. The replica keys a document by owner and id, so a
    /// build writing the registry under another id would fork it: two registries on
    /// the replica, each device taking whichever it pulled last.
    /// </summary>
    public static readonly Guid RegistryId = Guid.Parse("42925550-7da3-413f-ba66-d3584ef64d27");

    /// <summary>The accounts document's id, on the same terms as
    /// <see cref="RegistryId"/>: never to be changed.</summary>
    public static readonly Guid AccountsId = Guid.Parse("1be600c0-ac5f-47cb-9e92-4f34fa9c2ef2");

    /// <summary>Both documents, in the order a push sends them.</summary>
    public static IReadOnlyList<GitHubReplicaDocument> All { get; } =
        [GitHubReplicaDocument.Registry, GitHubReplicaDocument.Accounts];

    /// <summary>The document a kind token names, or null for any other kind. Ordinal,
    /// as every other token on the feed is compared.</summary>
    public static GitHubReplicaDocument? KindOf(string? type) => type switch
    {
        RegistryType => GitHubReplicaDocument.Registry,
        AccountsType => GitHubReplicaDocument.Accounts,
        _ => null,
    };

    public static string TypeOf(GitHubReplicaDocument document) => document switch
    {
        GitHubReplicaDocument.Registry => RegistryType,
        GitHubReplicaDocument.Accounts => AccountsType,
        _ => throw new ArgumentOutOfRangeException(nameof(document)),
    };

    public static Guid IdOf(GitHubReplicaDocument document) => document switch
    {
        GitHubReplicaDocument.Registry => RegistryId,
        GitHubReplicaDocument.Accounts => AccountsId,
        _ => throw new ArgumentOutOfRangeException(nameof(document)),
    };

    /// <summary>What the activity log, and any listing of the feed, calls it.</summary>
    public static string TitleOf(GitHubReplicaDocument document) => document switch
    {
        GitHubReplicaDocument.Registry => "Repository registry",
        GitHubReplicaDocument.Accounts => "GitHub accounts",
        _ => throw new ArgumentOutOfRangeException(nameof(document)),
    };

    /// <summary>The document as a task-shaped change, the shape
    /// <see cref="WholeDocumentChange"/> describes.</summary>
    public static TaskChange ToChange(GitHubReplicaDocument document, string content, DateTimeOffset updatedAt) =>
        WholeDocumentChange.Of(IdOf(document), TitleOf(document), TypeOf(document), content, updatedAt);
}
