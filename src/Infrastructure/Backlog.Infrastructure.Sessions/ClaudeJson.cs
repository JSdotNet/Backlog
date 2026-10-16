using System.Text.Json;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// How the Claude readers take a text field out of a line or file Claude wrote: one
/// copy, for the session reader, the facts fold and the work fold alike, so a field
/// that is absent, not a string or blank means the same thing to all of them.
/// </summary>
internal static class ClaudeJson
{
    /// <summary>The string <paramref name="name"/> holds, or null when it is absent or
    /// not a string. An empty or blank string is returned as it is.</summary>
    internal static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>As <see cref="Text"/>, with a blank string read as absent: for a field
    /// whose blank value states nothing, such as a model, an id or a URL.</summary>
    internal static string? NonBlankText(JsonElement element, string name) =>
        Text(element, name) is { } text && !string.IsNullOrWhiteSpace(text) ? text : null;
}
