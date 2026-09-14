namespace Backlog.Infrastructure.Knowledge;

/// <summary>
/// Where a repository's knowledge database is, given what a consumer actually
/// holds.
///
/// <para>Every knowledge consumer in this app is handed a *folder* — the absolute
/// path of <c>.domain</c>, say — because <c>IKnowledgeFolderSource</c> resolves an
/// area per registered repository and a folder can be turned off, moved, or
/// pointed somewhere off the clone. The database is not per folder: it is one file
/// at the repository root, because a scope is <c>WHERE folder = ?</c> and that is
/// the whole point of ADR 0004. So a folder path has to become a repository root,
/// and there is exactly one way this repository already does that — the atlas goes
/// up one level from a knowledge folder to find the repository-wide graph beside
/// it. This is the same walk, in one place, so the two cannot drift.</para>
///
/// <para>A folder configured outside a clone resolves to whatever sits above it,
/// which is very likely nothing at all. That is the correct answer rather than a
/// gap: no database there means the reader takes the Markdown path, which is what
/// such a folder has always done.</para>
/// </summary>
public static class KnowledgeDatabaseLocation
{
    /// <summary>The folder the database sits in, at the repository root.</summary>
    private const string MetaDirectory = "_meta";

    /// <summary>The database's file name.</summary>
    public const string FileName = "knowledge.db";

    /// <summary>The database inside a repository root, whether or not it exists.</summary>
    public static string? ForRepositoryRoot(string? repositoryRoot) =>
        string.IsNullOrWhiteSpace(repositoryRoot)
            ? null
            : Path.Combine(repositoryRoot, MetaDirectory, FileName);

    /// <summary>
    /// The database for the repository a knowledge folder belongs to, found by
    /// going up one level from the folder — which is where the knowledge folders
    /// sit and where the root <c>_meta/</c> sits with them.
    /// </summary>
    public static string? ForKnowledgeFolder(string? knowledgeFolderPath)
    {
        if (string.IsNullOrWhiteSpace(knowledgeFolderPath)) return null;

        try
        {
            var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(Path.GetFullPath(knowledgeFolderPath)));
            return parent is null ? null : ForRepositoryRoot(parent.FullName);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return null;
        }
    }
}
