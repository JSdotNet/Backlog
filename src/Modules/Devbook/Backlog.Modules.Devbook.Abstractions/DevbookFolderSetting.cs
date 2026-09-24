namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// One configured knowledge folder: which area it is, what it is called, where
/// it conventionally sits, and whether somebody has pointed it somewhere else.
/// <para>
/// This is Devbook's published language rather than a storage detail, which
/// is why it moved out of <c>Backlog.Infrastructure.GitHub</c> and why that
/// adapter now references this project instead of the other way round: a
/// repository carries a list of these because a repository is one place these
/// folders can live, not because GitHub defines what they are.
/// </para>
/// </summary>
public sealed record DevbookFolderSetting(string Key, string DisplayName, string DefaultRelativePath, bool SupportsPathOverride = true)
{
    /// <summary>The folder every section lives under in the devbook layout.</summary>
    public const string DevbookRoot = ".devbook";

    public bool Enabled { get; init; } = true;

    /// <summary>Optional repository-relative or absolute override. Null means the
    /// conventional folder is used: <see cref="DefaultRelativePath"/>, or
    /// <see cref="LegacyRelativePath"/> in a repository that has not moved to
    /// <c>.devbook/</c> yet.</summary>
    public string? Path { get; init; }

    /// <summary>
    /// Where the section sat at the repository root before the devbook layout —
    /// <c>.arc42</c> for <c>.devbook/arc42</c> — or empty for a section with no
    /// folder of its own.
    /// <para>
    /// Two jobs. It is the second place an unconfigured folder is looked for, so a
    /// repository still on the root layout keeps working; and it stays the prefix
    /// of every canonical chapter key, because remarks already filed against
    /// <c>.arc42/…</c> are stored and synced values that a moved default must not
    /// orphan.
    /// </para>
    /// </summary>
    public string LegacyRelativePath { get; init; } = string.Empty;

    /// <summary>The folder a resolution actually found, when that is not the one
    /// <see cref="Path"/> or the default names — the legacy root folder. Set only
    /// on the copy a <c>DevbookFolderLocation</c> carries; never stored.</summary>
    public string? ResolvedPath { get; init; }

    public string EffectivePath =>
        ResolvedPath ?? (string.IsNullOrWhiteSpace(Path) ? DefaultRelativePath : Path.Trim());

    /// <summary>The folders a resolution tries, in order: the override alone when
    /// there is one, otherwise the devbook default and then the legacy root
    /// folder.</summary>
    public IReadOnlyList<string> CandidatePaths =>
        !string.IsNullOrWhiteSpace(Path) || string.IsNullOrEmpty(LegacyRelativePath)
            ? [EffectivePath]
            : [DefaultRelativePath, LegacyRelativePath];

    public static List<DevbookFolderSetting> Defaults() =>
    [
        new("instructions", "Instructions", string.Empty, SupportsPathOverride: false),
        Area("domain", "Domain"),
        Area("arc42", "Architecture"),
        Area("tech", "Technology"),
        Area("design", "Design"),
        // The AI adoption record. Last, after the registry it links into: a `.ai`
        // chapter points at the `.tech` chapter for the tool under it, never the
        // other way round, so the folder reads after the one it depends on.
        Area("ai", "AI")
    ];

    /// <summary>A section keyed by its legacy root folder (the key is a stored
    /// value, so it did not move) and defaulting to its folder under
    /// <c>.devbook/</c>.</summary>
    private static DevbookFolderSetting Area(string name, string displayName) =>
        new($".{name}", displayName, $"{DevbookRoot}/{name}") { LegacyRelativePath = $".{name}" };

    public static List<DevbookFolderSetting> Normalize(IEnumerable<DevbookFolderSetting>? configured)
    {
        var byKey = (configured ?? []).ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

        return
        [
            .. Defaults().Select(folder =>
                byKey.TryGetValue(folder.Key, out var existing)
                    ? folder with
                    {
                        Enabled = existing.Enabled,
                        Path = ConfiguredOverride(folder, existing.Path)
                    }
                    : folder)
        ];
    }

    /// <summary>
    /// The override a configured value really carries, or null when it carries
    /// none.
    /// <para>
    /// A value naming the section's own conventional folder is the default
    /// written down rather than a choice somebody made — installed settings
    /// files carry one for every section — and it has to fold, or the row reads
    /// "Uses <c>.arc42</c>." where it should read "at the repository root" and
    /// the next save writes the leftover straight back. The legacy root folder
    /// folds too: resolution falls back to it on its own, and keeping it as an
    /// override would pin a repository to the root layout after it moved to
    /// <c>.devbook/</c>.
    /// </para>
    /// </summary>
    private static string? ConfiguredOverride(DevbookFolderSetting folder, string? configured)
    {
        if (!folder.SupportsPathOverride || string.IsNullOrWhiteSpace(configured)) return null;

        var trimmed = configured.Trim();

        // Only the conventional relative folder folds. A rooted path ending in the
        // same segment names somewhere else that happens to be called the same
        // thing, and dropping it would silently move the folder back to the clone.
        // System.IO.Path is spelled out because this record has a Path of its own,
        // which the simple name would bind to first.
        if (string.IsNullOrEmpty(folder.DefaultRelativePath) || System.IO.Path.IsPathRooted(trimmed)) return trimmed;

        var comparable = Comparable(trimmed);
        var conventional = string.Equals(comparable, Comparable(folder.DefaultRelativePath), StringComparison.OrdinalIgnoreCase)
            || (folder.LegacyRelativePath.Length > 0
                && string.Equals(comparable, Comparable(folder.LegacyRelativePath), StringComparison.OrdinalIgnoreCase));

        return conventional ? null : trimmed;

        // Both separators are folded to one rather than to
        // Path.DirectorySeparatorChar, because this compares two written paths
        // instead of building one: a setting typed as ".arc42/" on Windows is the
        // same folder as ".arc42\", whichever machine reads the file back.
        static string Comparable(string path) => path.Replace('\\', '/').TrimEnd('/');
    }
}
