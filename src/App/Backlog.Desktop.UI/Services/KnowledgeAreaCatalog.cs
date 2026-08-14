using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Services;

internal static class KnowledgeAreaCatalog
{
    private static readonly KnowledgeArea[] Areas =
    [
        new("backlog", "Backlog"),
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

    private static string AreaKey(KnowledgeFolderSetting folder) => folder.Key switch
    {
        ".backlog" => "backlog",
        "instructions" => "instructions",
        ".domain" => "domain",
        ".arc42" => "arc42",
        ".tech" => "tech",
        ".design" => "design",
        _ => folder.Key.TrimStart('.').ToLowerInvariant()
    };
}

internal sealed record KnowledgeArea(string Key, string Label);
