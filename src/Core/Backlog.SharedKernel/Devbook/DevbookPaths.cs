namespace Backlog.SharedKernel.Devbook;

/// <summary>
/// The two spellings a devbook path comes in, and the one every comparison of
/// two paths reads them in. Devbook's published language (local ADR 0016), kept
/// in the kernel so that a context beside Devbook — the Brain, matching a
/// <c>repo:</c> link against a chapter (local ADR 0040 §2) — compares paths the
/// way Devbook does without referencing it.
/// <para>
/// The devbook layout nests every folder under <see cref="DevbookRoot"/> without
/// its dot — <c>.devbook/arc42</c> — and the layout before it put each folder at
/// the root as <c>.arc42</c>. Pure: nothing here asks the disk.
/// </para>
/// </summary>
public static class DevbookPaths
{
    /// <summary>The folder every section lives under in the devbook layout.</summary>
    public const string DevbookRoot = ".devbook";

    private const string DevbookPrefix = DevbookRoot + "/";

    /// <summary>The folder names the devbook layout recognises under
    /// <see cref="DevbookRoot"/>, in the generator's order. <c>backlog</c> is read
    /// for the root layout's retired folder only, so it is absent here.</summary>
    public static IReadOnlyList<string> FolderNames { get; } = ["arc42", "domain", "tech", "design", "ai"];

    /// <summary>
    /// A path with forward slashes and no leading <c>./</c> or <c>/</c>, but with the
    /// dot of a dot folder kept: <c>./.github\copilot-instructions.md</c> becomes
    /// <c>.github/copilot-instructions.md</c>.
    /// </summary>
    public static string NormalizeKeepingDotFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var forward = path.Trim().Replace('\\', '/');
        while (forward.StartsWith("./", StringComparison.Ordinal)) forward = forward[2..];
        return forward.TrimStart('/');
    }

    /// <summary>
    /// A path in the root layout's spelling: <c>.devbook/arc42/05.md</c> becomes
    /// <c>.arc42/05.md</c>; anything else comes back with forward slashes and no
    /// leading <c>./</c> or <c>/</c>, otherwise unchanged. The spelling stored keys
    /// and the per-area stores already use, so two paths naming the same chapter
    /// compare equal after this whichever layout either came from.
    /// </summary>
    public static string ConventionalPath(string? path)
    {
        var normalized = NormalizeKeepingDotFolder(path);
        if (!normalized.StartsWith(DevbookPrefix, StringComparison.OrdinalIgnoreCase)) return normalized;

        var rest = normalized[DevbookPrefix.Length..];
        var end = rest.IndexOf('/', StringComparison.Ordinal);
        var name = end < 0 ? rest : rest[..end];

        return FolderNames.Contains(name, StringComparer.OrdinalIgnoreCase) ? "." + rest : normalized;
    }
}
