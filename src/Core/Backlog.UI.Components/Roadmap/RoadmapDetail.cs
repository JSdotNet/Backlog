namespace Backlog.UI.Components.Roadmap;

/// <summary>
/// Reads the <c>Detail</c> a caller hands a bar or a milestone: one fact to a line.
/// The tooltip lists the lines as they are; the accessible name runs them together
/// with commas, because a screen reader does not pause on a line break.
/// </summary>
internal static class RoadmapDetail
{
    public static IEnumerable<string> Lines(string? detail) =>
        string.IsNullOrWhiteSpace(detail)
            ? []
            : detail.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public static string? Spoken(string? detail) =>
        Lines(detail).ToList() is { Count: > 0 } lines ? string.Join(", ", lines) : null;

    /// <summary>The tooltip: every non-blank part on a line of its own.</summary>
    public static string Report(IEnumerable<string?> parts) =>
        string.Join('\n', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
}
