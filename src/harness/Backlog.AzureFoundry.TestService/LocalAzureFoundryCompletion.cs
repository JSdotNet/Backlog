namespace Backlog.AzureFoundry.TestService;

public static class LocalAzureFoundryCompletion
{
    public static string CreateAnswer(IReadOnlyList<AzureFoundryChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var userPrompt = messages.LastOrDefault(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content;

        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return "Local Azure Foundry test response: no question was provided.";
        }

        var question = ExtractSection(userPrompt, "Question:") ?? userPrompt.Trim();
        var content = ExtractSection(userPrompt, "Content:");
        var contentPreview = CreatePreview(content);

        return contentPreview.Length == 0
            ? $"Local Azure Foundry test response: {question}"
            : $"Local Azure Foundry test response: {question} Based on the supplied content, I found: {contentPreview}";
    }

    private static string? ExtractSection(string prompt, string marker)
    {
        var markerIndex = prompt.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (markerIndex < 0)
        {
            return null;
        }

        var sectionStart = markerIndex + marker.Length;
        var nextMarkerIndex = prompt.IndexOf("\n\n", sectionStart, StringComparison.Ordinal);
        var section = nextMarkerIndex < 0
            ? prompt[sectionStart..]
            : prompt[sectionStart..nextMarkerIndex];

        return section.Trim();
    }

    private static string CreatePreview(string? content)
    {
        var normalized = string.Join(' ', (content ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 220 ? normalized : string.Concat(normalized.AsSpan(0, 217), "...");
    }
}
