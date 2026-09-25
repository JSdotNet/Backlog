using Backlog.Modules.Devbook.Abstractions;

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
    /// Where it is depends on the layout, because the generator writes it beside
    /// the folders it indexed: <c>.devbook/_meta/devbook.db</c> in a repository
    /// that keeps its folders under <c>.devbook/</c>, <c>_meta/devbook.db</c> in
    /// one that keeps them at the root. The layout's own location is tried first
    /// and the other one after it, so a database built before a repository moved
    /// keeps serving until the next build — its rows then name no folder the
    /// reader asks about, which is the same answer as no database. In each
    /// location <c>devbook.db</c> is preferred and <c>knowledge.db</c>, the name
    /// before the rename, is still read.
    /// </para>
    /// <para>
    /// Absent everywhere, the answer is still the layout's own <c>devbook.db</c>
    /// path, so every caller's "does it exist" check behaves as it always did and
    /// nothing ever writes under an old name or the other layout's folder.
    /// </para>
    /// </summary>
    public static string? ForRepositoryRoot(string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot)) return null;

        var devbookLayout = DevbookLayout.UsesDevbookLayout(repositoryRoot);
        var primary = DevbookLayout.MetaDirectoryFor(repositoryRoot, devbookLayout);
        var secondary = DevbookLayout.MetaDirectoryFor(repositoryRoot, !devbookLayout);

        foreach (var directory in (string[])[primary, secondary])
        {
            foreach (var name in (string[])[FileName, LegacyFileName])
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return Path.Combine(primary, FileName);
    }

    /// <summary>
    /// The database for the repository a knowledge folder belongs to. The
    /// repository root is one level up from a root-layout folder (<c>.arc42</c>)
    /// and two from a devbook-layout one (<c>.devbook/arc42</c>).
    /// </summary>
    public static string? ForDevbookFolder(string? devbookFolderPath)
    {
        if (string.IsNullOrWhiteSpace(devbookFolderPath)) return null;

        try
        {
            var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(Path.GetFullPath(devbookFolderPath)));
            if (parent is null) return null;

            var root = string.Equals(parent.Name, DevbookFolderSetting.DevbookRoot, StringComparison.OrdinalIgnoreCase) && parent.Parent is not null
                ? parent.Parent
                : parent;

            return ForRepositoryRoot(root.FullName);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return null;
        }
    }
}
