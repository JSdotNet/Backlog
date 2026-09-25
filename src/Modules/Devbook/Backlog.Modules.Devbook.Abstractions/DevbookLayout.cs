namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The two places a repository can keep its knowledge folders, and the one
/// question every reader has to ask of a path because of it.
/// <para>
/// The devbook layout nests every folder under one parent without its dot —
/// <c>.devbook/arc42</c>, <c>.devbook/domain</c> — and keeps the repository-wide
/// rollup, the generated database included, in <c>.devbook/_meta/</c>. The layout
/// before it put each folder at the root as <c>.arc42</c>, <c>.domain</c>, with
/// the rollup in <c>_meta/</c>. A repository uses one or the other, and the
/// generator writes its rows in the spelling of whichever it found, so a reader
/// comparing a path against a folder has to accept both.
/// </para>
/// <para>
/// Everything here is pure except <see cref="UsesDevbookLayout"/>, which asks the
/// disk and never throws.
/// </para>
/// </summary>
public static class DevbookLayout
{
    /// <summary>The generated rollup's directory, beside the folders it indexes.</summary>
    public const string MetaDirectory = "_meta";

    private const string DevbookPrefix = DevbookFolderSetting.DevbookRoot + "/";

    /// <summary>The folder names the devbook layout recognises under
    /// <c>.devbook/</c>. <c>backlog</c> is read for the root layout's retired
    /// folder only, so it is absent here.</summary>
    private static readonly string[] DevbookFolderNames = ["arc42", "domain", "tech", "design", "ai"];

    /// <summary>
    /// A path in the root layout's spelling: <c>.devbook/arc42/05.md</c> becomes
    /// <c>.arc42/05.md</c>; anything else comes back with forward slashes and no
    /// leading <c>./</c> or <c>/</c>, otherwise unchanged. The spelling stored keys
    /// and the per-area stores already use, so two paths naming the same chapter
    /// compare equal after this whichever layout either came from.
    /// </summary>
    public static string ConventionalPath(string? path)
    {
        var normalized = Normalize(path);
        if (!normalized.StartsWith(DevbookPrefix, StringComparison.OrdinalIgnoreCase)) return normalized;

        var rest = normalized[DevbookPrefix.Length..];
        var end = rest.IndexOf('/');
        var name = end < 0 ? rest : rest[..end];

        return DevbookFolderNames.Contains(name, StringComparer.OrdinalIgnoreCase) ? "." + rest : normalized;
    }

    /// <summary>
    /// A repository-relative path relative to the knowledge folder it is in:
    /// <c>inbox/domain.md</c> for both <c>.domain/inbox/domain.md</c> and
    /// <c>.devbook/domain/inbox/domain.md</c>. A path with no folder segment comes
    /// back as it is.
    /// </summary>
    public static string WithinFolder(string? path)
    {
        var conventional = ConventionalPath(path);
        var separator = conventional.IndexOf('/');
        return separator >= 0 ? conventional[(separator + 1)..] : conventional;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is <paramref name="folder"/> itself or lies
    /// under it, comparing both in the conventional spelling — so a
    /// <c>.devbook/tech/…</c> row is in the <c>.tech</c> scope and a
    /// <c>.tech/…</c> row in the <c>.devbook/tech</c> one.
    /// </summary>
    public static bool IsInFolder(string? path, string? folder)
    {
        var conventionalFolder = ConventionalPath(folder).TrimEnd('/');
        if (conventionalFolder.Length == 0) return false;

        var conventional = ConventionalPath(path);
        return string.Equals(conventional, conventionalFolder, StringComparison.OrdinalIgnoreCase)
            || DevbookChapterKey.StartsWithSegment(conventional, conventionalFolder);
    }

    /// <summary>
    /// Whether the repository at <paramref name="repositoryRoot"/> keeps any of its
    /// knowledge folders under <c>.devbook/</c>. A <c>.devbook/</c> holding only
    /// configuration does not count: that is a repository wired for the devbook
    /// tooling whose chapters have not moved yet.
    /// </summary>
    public static bool UsesDevbookLayout(string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot)) return false;

        try
        {
            return DevbookFolderNames.Any(name =>
                Directory.Exists(System.IO.Path.Combine(repositoryRoot, DevbookFolderSetting.DevbookRoot, name)));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The repository-wide rollup directory for a layout:
    /// <c>.devbook/_meta</c> or <c>_meta</c>.</summary>
    public static string MetaDirectoryFor(string repositoryRoot, bool devbookLayout) =>
        devbookLayout
            ? System.IO.Path.Combine(repositoryRoot, DevbookFolderSetting.DevbookRoot, MetaDirectory)
            : System.IO.Path.Combine(repositoryRoot, MetaDirectory);

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var forward = path.Trim().Replace('\\', '/');
        while (forward.StartsWith("./", StringComparison.Ordinal)) forward = forward[2..];
        return forward.TrimStart('/');
    }
}
