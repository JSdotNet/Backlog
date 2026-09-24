namespace Backlog.Infrastructure.Devbook;

/// <summary>
/// Where a repository's devbook database is, given what a consumer actually
/// holds.
///
/// <para>Every knowledge consumer in this app is handed a *folder* — the absolute
/// path of <c>.domain</c>, say — because <c>IDevbookFolderSource</c> resolves an
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
public static class DevbookDatabaseLocation
{
    /// <summary>The folder the database sits in, at the repository root.</summary>
    private const string MetaDirectory = "_meta";

    /// <summary>The folder the devbook layout nests every knowledge folder
    /// under.</summary>
    private const string DevbookLayoutRoot = ".devbook";

    /// <summary>The database's file name.</summary>
    public const string FileName = "devbook.db";

    /// <summary>The file name the database had while the context was called
    /// Knowledge. Read, never written: <c>build-database.mjs</c> writes only
    /// <see cref="FileName"/>, so the old file is served until the next build
    /// replaces it and is ignored from then on.</summary>
    public const string LegacyFileName = "knowledge.db";

    /// <summary>
    /// The database inside a repository root, whether or not it exists.
    /// <para>
    /// <c>_meta/devbook.db</c> when it exists; else <c>_meta/knowledge.db</c> when
    /// that exists, so an index built before the rename keeps serving until the
    /// generator runs again; else the <c>devbook.db</c> path regardless — "absent"
    /// still resolves to the current name, so every caller's "does it exist" check
    /// behaves as it always did and nothing ever writes under the old one.
    /// </para>
    /// </summary>
    public static string? ForRepositoryRoot(string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot)) return null;

        var current = Path.Combine(repositoryRoot, MetaDirectory, FileName);
        if (File.Exists(current)) return current;

        var legacy = Path.Combine(repositoryRoot, MetaDirectory, LegacyFileName);
        return File.Exists(legacy) ? legacy : current;
    }

    /// <summary>
    /// The database for the repository a knowledge folder belongs to, found by
    /// going up one level from the folder — which is where the knowledge folders
    /// sit and where the root <c>_meta/</c> sits with them.
    /// <para>
    /// A folder in the devbook layout (<c>.devbook/arc42</c>) is one level
    /// deeper, so when that first step lands in <c>.devbook</c> and finds no
    /// database there, the repository root above it is tried as well.
    /// </para>
    /// </summary>
    public static string? ForDevbookFolder(string? devbookFolderPath)
    {
        if (string.IsNullOrWhiteSpace(devbookFolderPath)) return null;

        try
        {
            var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(Path.GetFullPath(devbookFolderPath)));
            if (parent is null) return null;

            var beside = ForRepositoryRoot(parent.FullName);
            if (File.Exists(beside)
                || !string.Equals(parent.Name, DevbookLayoutRoot, StringComparison.OrdinalIgnoreCase)
                || parent.Parent is null)
            {
                return beside;
            }

            var root = ForRepositoryRoot(parent.Parent.FullName);
            return File.Exists(root) ? root : beside;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return null;
        }
    }
}
