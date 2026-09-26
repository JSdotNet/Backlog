using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Devbook.Building;

/// <summary>
/// Which folders of a repository the builder indexes, how a path names its
/// folder, and where the repository-wide reading order is kept — the three
/// things that differ between the two layouts.
///
/// <para>The devbook layout is what the devbook plugin's own generator serves,
/// and the one <c>DevbookBuilderParityTests</c> compares against. The root layout
/// (<c>.arc42</c>, <c>.domain</c>, … at the repository root) is served with the
/// same rules and that layout's folder names, which is enough for a repository
/// the app reads but this project does not control to be searchable; nothing
/// holds it to the retired generator that used to index it.</para>
/// </summary>
internal sealed record DevbookBuildLayout(
    bool IsDevbookLayout,
    IReadOnlyList<string> Folders,
    string RepositoryReadingOrderPath)
{
    /// <summary>The repository-wide scope.</summary>
    public const string RepositoryScope = ".";

    private static readonly string[] DevbookFolderNames = ["arc42", "domain", "tech", "design", "ai"];

    private static readonly string[] RootFolderNames = [".arc42", ".domain", ".backlog", ".tech", ".design", ".ai"];

    /// <summary>
    /// The layout of <paramref name="repositoryRoot"/> and the folders it actually
    /// has, in the generator's fixed order. No folders at all is an ordinary
    /// answer: there is nothing to build.
    /// </summary>
    public static DevbookBuildLayout Discover(string repositoryRoot)
    {
        if (DevbookLayout.UsesDevbookLayout(repositoryRoot))
        {
            var folders = DevbookFolderNames
                .Select(name => $"{DevbookFolderSetting.DevbookRoot}/{name}")
                .Where(folder => Directory.Exists(Path.Combine(repositoryRoot, folder)))
                .ToArray();

            return new DevbookBuildLayout(true, folders, $"{DevbookFolderSetting.DevbookRoot}/_reading-order.json");
        }

        var rootFolders = RootFolderNames
            .Where(folder => Directory.Exists(Path.Combine(repositoryRoot, folder)))
            .ToArray();

        return new DevbookBuildLayout(false, rootFolders, "_reading-order.json");
    }

    /// <summary>The scopes: the repository, then each folder.</summary>
    public IReadOnlyList<string> Scopes => Folders.Count == 0 ? [] : [RepositoryScope, .. Folders];

    /// <summary>
    /// The folder a repository-relative path belongs to, as the generator's
    /// <c>folderKindForPath</c> names it — <c>domain</c> for
    /// <c>.devbook/domain/…</c> — or <see langword="null"/> outside every folder.
    /// </summary>
    public string? FolderKindForPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');

        if (IsDevbookLayout)
        {
            const string prefix = DevbookFolderSetting.DevbookRoot + "/";
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) return null;
            var subject = normalized[prefix.Length..];
            return DevbookFolderNames.FirstOrDefault(name => subject.StartsWith(name + "/", StringComparison.Ordinal));
        }

        return RootFolderNames.FirstOrDefault(name => normalized.StartsWith(name + "/", StringComparison.Ordinal)) is { } folder
            ? folder[1..]
            : null;
    }

    /// <summary>The status a folder's chapters rest at and so omit.</summary>
    public static string? RestingStatusFor(string? folder) =>
        folder is "domain" or "arc42" or "design" ? "active" : null;

    /// <summary>
    /// The chapter's <c>type</c>, falling back to <c>kind</c> in <c>tech</c>, where
    /// the field had that name before.
    /// </summary>
    public static object? ResolveType(string? folder, IReadOnlyDictionary<string, object?>? meta)
    {
        if (meta is null) return null;
        if (meta.TryGetValue("type", out var type) && type is not null && !(type is string { Length: 0 })) return type;
        if (folder != "tech") return null;
        return meta.TryGetValue("kind", out var legacy) && legacy is not null && !(legacy is string { Length: 0 }) ? legacy : null;
    }
}
