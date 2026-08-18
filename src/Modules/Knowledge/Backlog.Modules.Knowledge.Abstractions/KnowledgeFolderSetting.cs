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
        new(".arc42", "arc42 architecture", ".arc42"),
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
                        Path = folder.SupportsPathOverride && !string.IsNullOrWhiteSpace(existing.Path) ? existing.Path.Trim() : null
                    }
                    : folder)
        ];
    }
}
