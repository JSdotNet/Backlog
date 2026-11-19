using System.Text.RegularExpressions;

using Backlog.SharedKernel.C4;

namespace Backlog.Infrastructure.Devbook.Building;

/// <summary>
/// The arc42 folder's Structurizr DSL workspace, read the way devbook's
/// <c>c4.mjs</c> reads it, for the C4 part of the reference graph: the shared
/// <see cref="C4DslReader"/> under <see cref="C4IdMode.Generator"/>, whose
/// identifiers are the generator's, with <see cref="DevbookC4Includes"/> resolving
/// its <c>!include</c>s. The reader's problems are not kept: the database keeps
/// none of them, and reporting them is the checker's job (local ADR 0015).
/// </summary>
internal static class DevbookC4Workspace
{
    /// <summary>Reads the workspace at <paramref name="workspacePath"/>, a
    /// repository-relative path. Every file name in the result is repository-relative
    /// with <c>/</c>, as the generator's <c>readWorkspace</c> labels them.</summary>
    public static C4Workspace Read(string repositoryRoot, string workspacePath)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var absolute = Path.GetFullPath(Path.Combine(root, workspacePath.Replace('/', Path.DirectorySeparatorChar)));

        return C4DslReader.Read(File.ReadAllText(absolute), new C4ReadOptions
        {
            Ids = C4IdMode.Generator,
            File = DevbookC4Includes.Label(root, absolute),
            Include = DevbookC4Includes.Resolver(root, absolute)
        });
    }
}

/// <summary>
/// Which files a workspace's <c>!include</c> may read, as devbook's <c>c4.mjs</c>
/// allows them: a relative path, resolved from the including file, to a file that
/// exists in the <c>c4/</c> folder beside the workspace — not a URL, not an
/// absolute path, and not a link whose target leaves that folder.
/// </summary>
public static partial class DevbookC4Includes
{
    /// <summary>The only folder beside the workspace an include may read from.</summary>
    public const string Folder = "c4";

    /// <summary>A file's label: its path relative to <paramref name="root"/>, with <c>/</c>.</summary>
    public static string Label(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// The resolver for the workspace at <paramref name="workspacePath"/>. Every
    /// label it hands out, and every one it is handed, is relative to
    /// <paramref name="root"/>.
    /// </summary>
    public static C4IncludeResolver Resolver(string root, string workspacePath)
    {
        root = Path.GetFullPath(root);
        var allowed = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workspacePath))!, Folder);

        return (target, fromFile) =>
        {
            if (fromFile is null) return null;
            if (SchemePrefix().IsMatch(target) || Path.IsPathRooted(target) || target.StartsWith('/') || target.StartsWith('\\')) return null;

            var from = Path.GetFullPath(Path.Combine(root, fromFile.Replace('/', Path.DirectorySeparatorChar)));
            var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(from)!, target.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(allowed, resolved) || !File.Exists(resolved)) return null;

            // A link out of c4/ is refused by the generator after it resolves the
            // real paths; the link's own target is what is checked here.
            var real = new FileInfo(resolved).LinkTarget is { } link
                ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(resolved)!, link))
                : resolved;
            if (!Inside(allowed, real)) return null;

            return new C4IncludedFile(Label(root, resolved), File.ReadAllText(resolved));
        };
    }

    private static bool Inside(string folder, string path)
    {
        var relative = Path.GetRelativePath(folder, path);
        return relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
    }

    [GeneratedRegex(@"^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase)]
    private static partial Regex SchemePrefix();
}
