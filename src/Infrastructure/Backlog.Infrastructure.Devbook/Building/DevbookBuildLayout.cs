using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Devbook.Building;

/// <summary>
/// Which folders of a repository the builder indexes and how a path names its
/// folder — the two things that differ between the layouts. Neither layout
/// keeps an authored reading order any more (local ADR 0016); the outline is
/// derived by <see cref="DevbookOutlineBuilder"/>.
///
/// <para>The devbook layout is what the devbook plugin's own generator serves,
/// and the one <c>DevbookBuilderParityTests</c> compares against. The root layout
/// (<c>.arc42</c>, <c>.domain</c>, … at the repository root) is served with the
/// same rules and that layout's folder names, which is enough for a repository
/// the app reads but this project does not control to be searchable; nothing
/// holds it to the retired generator that used to index it.</para>
///
/// <para>A corpus profile (<see cref="DevbookCorpusProfile"/>) may name its own
/// folders instead, in its own order, and a path's folder is then the first of
/// them it lies under. Where the profile has no folder conventions, none applies
/// to a profile's own folder — no root document by name, no resting status, no
/// <c>kind</c> fallback — even one that happens to be called <c>design</c>, which
/// is what <see cref="ConventionFolderForPath"/> answers; the generator gets the
/// same by knowing none of such a corpus's folders.</para>
/// </summary>
internal sealed record DevbookBuildLayout(
    bool IsDevbookLayout,
    IReadOnlyList<string> Folders)
{
    /// <summary>The repository-wide scope.</summary>
    public const string RepositoryScope = ".";

    private static readonly string[] DevbookFolderNames = ["arc42", "domain", "tech", "design", "ai"];

    private static readonly string[] RootFolderNames = [".arc42", ".domain", ".backlog", ".tech", ".design", ".ai"];

    /// <summary>The profile the folder is read under; a repository's unless a
    /// profile's own discovery made this layout.</summary>
    public DevbookCorpusProfile Profile { get; private init; } = DevbookCorpusProfile.Repository;

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

            return new DevbookBuildLayout(true, folders);
        }

        var rootFolders = RootFolderNames
            .Where(folder => Directory.Exists(Path.Combine(repositoryRoot, folder)))
            .ToArray();

        return new DevbookBuildLayout(false, rootFolders);
    }

    /// <summary>
    /// The layout of a folder read under <paramref name="profile"/>: the folders
    /// the profile names, in its order — which, through <see cref="Folders"/>, are
    /// also what a tagged inputs fingerprint records, so a new folder makes the
    /// database stale — or, where it names none, the repository layout's own
    /// discovery read under the profile.
    /// </summary>
    public static DevbookBuildLayout Discover(string root, DevbookCorpusProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile.Folders(root) is { } folders
            ? new DevbookBuildLayout(false, folders) { Profile = profile }
            : Discover(root) with { Profile = profile };
    }

    /// <summary>The scopes: the repository, then each folder.</summary>
    public IReadOnlyList<string> Scopes => Folders.Count == 0 ? [] : [RepositoryScope, .. Folders];

    /// <summary>
    /// The folder a repository-relative path belongs to, as the generator's
    /// <c>folderKindForPath</c> names it — <c>domain</c> for
    /// <c>.devbook/domain/…</c>, a profile's own folder for its <c>projects/…</c>
    /// — or <see langword="null"/> outside every folder.
    /// </summary>
    public string? FolderKindForPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');

        if (!Profile.HasFolderConventions)
        {
            return Folders.FirstOrDefault(folder => normalized.StartsWith(folder + "/", StringComparison.Ordinal));
        }

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

    /// <summary>
    /// The devbook folder whose conventions apply to a path — its reading-order
    /// slots, its resting status, its <c>kind</c> fallback. The path's folder in a
    /// repository; nothing under a profile without folder conventions, whose
    /// folders carry none of them.
    /// </summary>
    public string? ConventionFolderForPath(string relativePath) =>
        Profile.HasFolderConventions ? FolderKindForPath(relativePath) : null;

    /// <summary>The status a folder's chapters rest at and so omit — or, for a debt
    /// record's top-level block, <c>identified</c>, where its own ladder rests
    /// (contract 33).</summary>
    public static string? RestingStatusFor(string? folder, bool debtRecord = false) =>
        debtRecord ? "identified"
        : folder is "domain" or "arc42" or "design" ? "active"
        : null;

    /// <summary>
    /// Whether a file's top-level block is a debt record's: a file under
    /// <c>.devbook/arc42/tdr/</c> other than the folder's index (metadata.mjs
    /// <c>isDebtRecord</c>). The generator knows the devbook layout's path alone, so
    /// a root layout's <c>.arc42/tdr/</c> keeps the folder's ladder.
    /// </summary>
    public static bool IsDebtRecord(DevbookBuildLayout layout, string relativePath, IReadOnlyDictionary<string, object?>? fileMeta)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!layout.IsDevbookLayout || !layout.Profile.HasFolderConventions) return false;

        var normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith(DevbookFolderSetting.DevbookRoot + "/arc42/tdr/", StringComparison.Ordinal)
            && normalized.EndsWith(".md", StringComparison.Ordinal)
            && !(fileMeta?.GetValueOrDefault("index") is string index && index == "root");
    }

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
