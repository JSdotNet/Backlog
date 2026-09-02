using Backlog.Modules.Knowledge.Abstractions;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Knowledge;

public static class KnowledgeAreaCatalog
{
    private static readonly KnowledgeArea[] Areas =
    [
        new("instructions", "Instructions"),
        new("domain", "Domain"),
        new("arc42", "Architecture"),
        new("tech", "Technology"),
        new("design", "Design")
    ];

    public static IReadOnlyList<KnowledgeArea> VisibleAreas(IEnumerable<KnowledgeFolderSetting> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var enabledAreaKeys = KnowledgeFolderSetting.Normalize(folders)
            .Where(folder => folder.Enabled)
            .Select(AreaKey)
            .ToHashSet(StringComparer.Ordinal);

        return [.. Areas.Where(area => enabledAreaKeys.Contains(area.Key))];
    }

    /// <summary>
    /// The section a repository-relative knowledge path belongs to —
    /// <c>.domain/tasks/domain.md</c> is the Domain section — or
    /// <see langword="null"/> when the folder it names is not a section this
    /// product reads.
    /// <para>
    /// The leading segment is a knowledge folder key, so it goes through the same
    /// folder-key-to-section map the knowledge menu is built from rather than
    /// through a second one written here; the answer is then only handed back when
    /// it names a section on this list. A reference into <c>.github</c> resolves to
    /// nothing, which is the honest answer: there is no section to send a reader
    /// to, and a control that claimed otherwise would be a control that goes
    /// nowhere.
    /// </para>
    /// </summary>
    public static string? AreaKeyForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        // The same leniency KnowledgeFolders.FromPath allows, and for the same
        // reason: a hand-authored reference picks up a leading `./` or `/` often
        // enough, and those spell the same file.
        var trimmed = path.Trim().Replace('\\', '/');
        if (trimmed.StartsWith("./", StringComparison.Ordinal)) trimmed = trimmed[2..];
        trimmed = trimmed.TrimStart('/');

        var end = trimmed.IndexOf('/');
        if (end <= 0) return null;

        var areaKey = KnowledgeMenu.AreaKey(trimmed[..end]);
        return Areas.Any(area => string.Equals(area.Key, areaKey, StringComparison.Ordinal)) ? areaKey : null;
    }

    private static string AreaKey(KnowledgeFolderSetting folder) => folder.Key switch
    {
        "instructions" => "instructions",
        ".domain" => "domain",
        ".arc42" => "arc42",
        ".tech" => "tech",
        ".design" => "design",
        _ => folder.Key.TrimStart('.').ToLowerInvariant()
    };
}

public sealed record KnowledgeArea(string Key, string Label);
