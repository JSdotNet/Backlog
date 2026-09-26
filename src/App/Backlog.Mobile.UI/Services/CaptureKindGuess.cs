using System.Text.RegularExpressions;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// Which <c>CaptureKindMarker</c> glyph a row leads with, read off the capture's
/// text.
/// <para>
/// The same heuristic the desktop's <c>ContentKindDetector</c> applies, kept
/// here as the phone's own copy because neither side may reach the other: the
/// sync service interprets no more of a capture than it must (ADR 0005), so it
/// does not send a kind, and the phone does not reference the Inbox module. The
/// glyph is a hint beside the title, never a decision — the desktop classifies
/// the item for real when it arrives.
/// </para>
/// </summary>
public static partial class CaptureKindGuess
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp"];

    /// <summary>A slug from <c>CaptureKinds.All</c>.</summary>
    public static string Of(string title, string? bodyMd)
    {
        var text = string.IsNullOrWhiteSpace(bodyMd) ? title ?? string.Empty : $"{title}\n{bodyMd}";
        var match = UrlRegex().Match(text);

        if (match.Success)
        {
            var address = match.Value.TrimEnd('.', ',', ';', ':');

            if (IsHost(address, "youtube.com") || IsHost(address, "youtu.be")) return "youtube";
            if (IsHost(address, "claude.ai") || IsHost(address, "claude.com")) return "claude-artifact";
            if (HasExtension(address, ImageExtensions)) return "image";
            if (HasExtension(address, [".pdf"])) return "document";

            return string.Equals((title ?? string.Empty).Trim(), address, StringComparison.OrdinalIgnoreCase)
                ? "link"
                : "article";
        }

        return text.Contains("```", StringComparison.Ordinal) ? "code" : "text";
    }

    private static bool HasExtension(string address, string[] extensions)
    {
        var path = address.Split('?', '#')[0];
        return extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHost(string address, string host) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"https?://[^\s<>()""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
