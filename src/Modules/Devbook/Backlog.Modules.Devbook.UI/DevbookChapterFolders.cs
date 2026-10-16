using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Where a repository's devbook sections live as repository-relative folders, and
/// which section a changed path falls in.
/// <para>
/// One answer for the branch review — which changed files are chapters — and the
/// branch overview — which sections a branch changes — so a folder pointed
/// somewhere else counts the same in both.
/// </para>
/// </summary>
internal static class DevbookChapterFolders
{
    /// <summary>
    /// The enabled sections' folders: each override, or the folder under
    /// <c>.devbook/</c> and the legacy one at the root, and
    /// <c>.github/instructions/</c> for the instructions section. A rooted
    /// override lives outside the repository, so no changed path can be under it.
    /// With nothing enabled, the whole <c>.devbook/</c> folder, its section read
    /// from the path.
    /// </summary>
    public static IReadOnlyList<DevbookFolderPrefix> For(GitHubRepositoryRef repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var prefixes = new List<DevbookFolderPrefix>();

        foreach (var folder in DevbookFolderSetting.Normalize(repository.DevbookFolders).Where(folder => folder.Enabled))
        {
            var area = DevbookAreaCatalog.AreaKeyFor(folder);

            // The instructions section has no folder of its own to override; the
            // chapters under it that a branch changes are the instruction files.
            if (string.Equals(area, DevbookAreaCatalog.InstructionsKey, StringComparison.Ordinal))
            {
                prefixes.Add(new DevbookFolderPrefix(".github/instructions/", area));
                continue;
            }

            prefixes.AddRange(folder.CandidatePaths
                .Where(path => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path))
                .Select(path => new DevbookFolderPrefix(path.Replace('\\', '/').Trim('/') + "/", area)));
        }

        return prefixes.Count > 0
            ? prefixes
            : [new DevbookFolderPrefix($"{DevbookFolderSetting.DevbookRoot}/", null), new DevbookFolderPrefix(".github/instructions/", DevbookAreaCatalog.InstructionsKey)];
    }

    /// <summary>
    /// The section a repository-relative path falls in, or null when it is under
    /// none of <paramref name="folders"/>. The longest folder wins, so an
    /// override nested inside another section's folder keeps its own section.
    /// </summary>
    public static string? AreaOf(string? path, IReadOnlyList<DevbookFolderPrefix> folders)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var match = folders
            .Where(folder => path.StartsWith(folder.Prefix, StringComparison.OrdinalIgnoreCase))
            .MaxBy(folder => folder.Prefix.Length);

        return match is null ? null : match.AreaKey ?? DevbookAreaCatalog.AreaKeyForPath(path);
    }
}

/// <summary>One section's folder as a path prefix ending in <c>/</c>.</summary>
/// <param name="AreaKey">The section, or null for the whole devbook folder, whose
/// section is read from the path.</param>
internal sealed record DevbookFolderPrefix(string Prefix, string? AreaKey);
