namespace Backlog.Modules.Knowledge.Abstractions;

/// <summary>
/// One configured knowledge folder: which area it is, what it is called, where
/// it conventionally sits, and whether somebody has pointed it somewhere else.
/// <para>
/// This is Second Brain's published language rather than a storage detail, which
/// is why it moved out of <c>Backlog.Infrastructure.GitHub</c> and why that
/// adapter now references this project instead of the other way round: a
/// repository carries a list of these because a repository is one place these
/// folders can live, not because GitHub defines what they are.
/// </para>
/// </summary>
public sealed record KnowledgeFolderSetting(string Key, string DisplayName, string DefaultRelativePath, bool SupportsPathOverride = true)
{
    public bool Enabled { get; init; } = true;

    /// <summary>Optional repository-relative or absolute override. Null means the
    /// conventional folder at the repository root is used.</summary>
    public string? Path { get; init; }

    public string EffectivePath => string.IsNullOrWhiteSpace(Path) ? DefaultRelativePath : Path.Trim();

    public static List<KnowledgeFolderSetting> Defaults() =>
    [
        new("instructions", "Instructions", string.Empty, SupportsPathOverride: false),
        new(".backlog", "Backlog", ".backlog"),
        new(".domain", "Domain", ".domain"),
        new(".arc42", "Architecture", ".arc42"),
        new(".tech", "Technology", ".tech"),
        new(".design", "Design", ".design")
    ];

    public static List<KnowledgeFolderSetting> Normalize(IEnumerable<KnowledgeFolderSetting>? configured)
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
    /// the next save writes the leftover straight back.
    /// </para>
    /// </summary>
    private static string? ConfiguredOverride(KnowledgeFolderSetting folder, string? configured)
    {
        if (!folder.SupportsPathOverride || string.IsNullOrWhiteSpace(configured)) return null;

        var trimmed = configured.Trim();

        // Only the conventional relative folder folds. A rooted path ending in the
        // same segment names somewhere else that happens to be called the same
        // thing, and dropping it would silently move the folder back to the clone.
        // System.IO.Path is spelled out because this record has a Path of its own,
        // which the simple name would bind to first.
        if (string.IsNullOrEmpty(folder.DefaultRelativePath) || System.IO.Path.IsPathRooted(trimmed)) return trimmed;

        return string.Equals(Comparable(trimmed), Comparable(folder.DefaultRelativePath), StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;

        // Both separators are folded to one rather than to
        // Path.DirectorySeparatorChar, because this compares two written paths
        // instead of building one: a setting typed as ".arc42/" on Windows is the
        // same folder as ".arc42\", whichever machine reads the file back.
        static string Comparable(string path) => path.Replace('\\', '/').TrimEnd('/');
    }
}
