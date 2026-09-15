using System.Text.RegularExpressions;

using Backlog.Modules.Inbox.Abstractions;

namespace Backlog.Modules.Inbox.Services;

/// <summary>
/// The Classification domain service, at its smallest: what a capture is, read
/// off its text.
/// <para>
/// A heuristic, and named as one. A YouTube host is a video, a Claude host is
/// an artifact, an image extension is an image, a PDF is a document, a fenced
/// block is code, any other URL is an article when there are words beside it
/// and a bare link when there are not, and everything else is text. A channel
/// that <em>knows</em> what it captured — a mail client, a voice memo — should
/// say so and skip this; until one exists, a guess a reader can see beats a
/// column of "Text". This is what <c>TasksDrafts.KindOf</c> did while the Inbox
/// showed backlog drafts, moved to the context that owns the question.
/// </para>
/// </summary>
internal static partial class ContentKindDetector
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp"];

    /// <summary>What the capture is, from its title and whatever body it
    /// carries. Today's captures carry only a title, so the URL is usually in it.</summary>
    public static ContentKind Detect(string title, string? bodyMd = null)
    {
        var text = string.IsNullOrWhiteSpace(bodyMd) ? title ?? string.Empty : $"{title}\n{bodyMd}";

        if (FirstUrl(text) is { } address)
        {
            if (IsHost(address, "youtube.com") || IsHost(address, "youtu.be")) return ContentKind.YouTube;
            if (IsHost(address, "claude.ai") || IsHost(address, "claude.com")) return ContentKind.ClaudeArtifact;
            if (HasExtension(address, ImageExtensions)) return ContentKind.Image;
            if (HasExtension(address, [".pdf"])) return ContentKind.Document;

            // A title that is the URL itself is a link nobody has read yet; a title
            // in words over a URL is an article somebody meant to read.
            return string.Equals((title ?? string.Empty).Trim(), address, StringComparison.OrdinalIgnoreCase)
                ? ContentKind.Link
                : ContentKind.Article;
        }

        if (text.Contains("```", StringComparison.Ordinal)) return ContentKind.Code;

        return ContentKind.Text;
    }

    /// <summary>The first absolute http(s) URL in the text, or null. Stops at
    /// whitespace and at the characters prose puts after an address — a closing
    /// bracket or quote — so a link inside a sentence does not swallow the words
    /// after it. A trailing comma or full stop is prose too.</summary>
    public static string? FirstUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = UrlRegex().Match(text);
        if (!match.Success) return null;

        return match.Value.TrimEnd('.', ',', ';', ':');
    }

    private static bool HasExtension(string path, string[] extensions)
    {
        // Query strings and fragments are not part of the name.
        var clean = path.Split('?', '#')[0];
        return extensions.Any(extension => clean.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHost(string address, string host)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return false;

        return string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"https?://[^\s<>()""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
