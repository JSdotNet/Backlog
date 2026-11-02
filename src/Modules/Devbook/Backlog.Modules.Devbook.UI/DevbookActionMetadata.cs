namespace Backlog.Desktop.UI.Devbook;

public sealed record DevbookActionItem(
    string Title,
    string Kind,
    string Path,
    IReadOnlyDictionary<string, string> Metadata,
    string? Summary);

public static class DevbookActionMetadata
{
    public static string Status(IReadOnlyDictionary<string, string> metadata) =>
        metadata.TryGetValue("status", out var status) && !string.IsNullOrWhiteSpace(status)
            ? status.Trim().ToLowerInvariant()
            : "none";

    public static string BuildPrompt(DevbookActionItem item) =>
        $"Work on this {item.Kind} devbook item with GitHub Copilot CLI.\n\n"
        + "Use the devbook metadata and summary as the task brief and preserve its intent.\n\n"
        + $"Devbook item: {item.Title}\n"
        + $"Type: {item.Kind}\n"
        + $"Path: {item.Path}\n"
        + $"State: {Status(item.Metadata)}\n"
        + MetadataPrompt(item.Metadata)
        + SummaryPrompt(item.Summary);

    private static string MetadataPrompt(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.Count == 0) return string.Empty;

        var lines = metadata.Select(pair => $"- {pair.Key}: {pair.Value}");
        return "\nMetadata:\n" + string.Join('\n', lines) + "\n";
    }

    private static string SummaryPrompt(string? summary) =>
        string.IsNullOrWhiteSpace(summary) ? string.Empty : $"\nSummary:\n{summary.Trim()}\n";
}
