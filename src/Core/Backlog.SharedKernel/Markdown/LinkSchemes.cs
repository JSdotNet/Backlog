namespace Backlog.SharedKernel.Markdown;

/// <summary>
/// The one allow-list of URL schemes the product will put in an <c>href</c>, and
/// the subset of it a bare URL in prose becomes a link for.
/// <para>
/// One list because two would drift: the Markdown read view's links and a task
/// title's links must agree on what is safe to follow, and a scheme added to one
/// and not the other is a door left open in only one of them.
/// </para>
/// </summary>
public static class LinkSchemes
{
    // Bare: whether the scheme is followed when it is written as a bare URL in
    // prose. Only the web schemes are — GFM's extended autolinks recognise
    // `scheme://` and nothing else, and a bare `mailto:` reads as a word with a
    // colon in it far more often than as an address someone meant to follow.
    private static readonly (string Scheme, bool Bare)[] Allowed =
    [
        ("http", true),
        ("https", true),
        ("mailto", false)
    ];

    /// <summary>
    /// Whether a link URL is one we are willing to put in an <c>href</c>.
    /// Escaping keeps typed markup out of the document, but an <c>href</c> is a
    /// second door: <c>javascript:</c> and <c>data:</c> URLs run in the app's own
    /// origin under WebView2 (ADR 0001), and an entry body can arrive from a
    /// repository file nobody here wrote. So the scheme is an allow-list, not a
    /// block-list — anything unrecognised is text, not a link.
    /// </summary>
    public static bool IsNavigable(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        var trimmed = url.Trim();

        // Browsers strip control characters before resolving a URL, which is how
        // "java\tscript:" gets through a naive scheme check. Nothing legitimate
        // needs them.
        if (trimmed.Any(char.IsControl)) return false;

        if (!NamesScheme(trimmed)) return true;

        var scheme = trimmed[..trimmed.IndexOf(':', StringComparison.Ordinal)];
        return Allowed.Any(allowed => allowed.Scheme.Equals(scheme, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether a bare URL in prose that opens with <paramref name="scheme"/> and
    /// <c>://</c> becomes a link — <c>http</c> and <c>https</c>, and no other.
    /// </summary>
    public static bool IsBare(ReadOnlySpan<char> scheme)
    {
        foreach (var allowed in Allowed)
        {
            if (allowed.Bare && scheme.Equals(allowed.Scheme, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a link target opens with something a browser would read as a URL
    /// scheme, rather than being relative to the document it was written in.
    ///
    /// <para>Public because a caller deciding what to do with a target it could not
    /// place has to know which of two answers it is looking at. A relative target
    /// that resolves nowhere is the author's mistake and belongs on screen as the
    /// words they wrote; one that names a scheme was never the caller's to place,
    /// and handing it back is what keeps an <c>https</c> link the anchor it
    /// has always been. Asking the question in one place is what stops the two
    /// readings drifting: <see cref="IsNavigable"/>'s allow-list decides which
    /// schemes are welcome, and this decides only whether there is one.</para>
    /// </summary>
    public static bool NamesScheme(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        var trimmed = url.Trim();

        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0) return false;

        // A colon after the path has begun is part of the path, not a scheme:
        // "notes/10:30.md" is relative, "//host/x" is protocol-relative.
        var pathStart = trimmed.IndexOfAny(['/', '?', '#']);
        return pathStart < 0 || pathStart > colon;
    }
}
