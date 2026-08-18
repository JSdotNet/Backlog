namespace Backlog.Desktop.UI.Knowledge;

internal static class KnowledgeMetadataDisplay
{
    private static readonly HashSet<string> RedundantLinkLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "link",
        "related"
    };

    public static bool ShouldShowLabel(string label) => !RedundantLinkLabels.Contains(label.Trim());
}
