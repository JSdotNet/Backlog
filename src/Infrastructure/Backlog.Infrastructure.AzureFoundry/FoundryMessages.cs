namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>
/// How much of an Azure answer the chat, embeddings and cost clients quote when
/// they describe a failure. One copy for the three of them, so the message a
/// person sees cannot drift between features.
/// </summary>
internal static class FoundryMessages
{
    /// <summary>The payload trimmed, and cut to its first 300 characters with an
    /// ellipsis when it is longer than that.</summary>
    internal static string TrimForMessage(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300] + "...";
    }
}
