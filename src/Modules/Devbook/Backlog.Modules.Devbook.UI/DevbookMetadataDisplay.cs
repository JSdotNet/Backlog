namespace Backlog.Desktop.UI.Devbook;

internal static class DevbookMetadataDisplay
{
    private static readonly HashSet<string> RedundantLinkLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "link",
        "related"
    };

    public static bool ShouldShowLabel(string label) => !RedundantLinkLabels.Contains(label.Trim());
}
