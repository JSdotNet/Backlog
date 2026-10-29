namespace Backlog.SharedKernel.Markdown;

/// <summary>
/// A bare URL found in prose: where it starts and the exact characters it is, so
/// <c>text.Substring(Index, Length)</c> is always <see cref="Url"/>.
/// </summary>
public readonly record struct BareUrl(int Index, string Url)
{
    public int Length => Url.Length;
}

/// <summary>
/// Finds the bare <c>http://</c> and <c>https://</c> URLs in a line of prose — the
/// one reading a task title and a Markdown body share, so a URL is a link in both or
/// in neither.
/// <para>
/// Where a URL ends follows GFM's extended autolinks, because that is what people
/// who paste a URL next to words expect: it runs to whitespace, <c>&lt;</c> or <c>&gt;</c>,
/// then sheds trailing <c>.,:;!?*_~'"</c> and any closing bracket the URL did not
/// open, so <c>see https://example.com/a.</c> and <c>(https://example.com/a)</c>
/// both link <c>https://example.com/a</c> while a Wikipedia title's own
/// <c>(bar)</c> stays in. Which schemes count is <see cref="LinkSchemes"/>'s, not
/// this type's.
/// </para>
/// </summary>
public static class BareUrlScanner
{
    private const string Separator = "://";

    // Sentence punctuation GFM leaves outside an autolink when it ends one; inside
    // a URL every one of them is kept.
    private const string TrailingPunctuation = ".,:;!?*_~'\"";

    /// <summary>Every bare URL in <paramref name="text"/>, in the order they appear.</summary>
    public static IReadOnlyList<BareUrl> FindAll(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];

        var found = new List<BareUrl>();
        var at = 0;
        while (FindNext(text, at) is { } url)
        {
            found.Add(url);
            at = url.Index + url.Length;
        }

        return found;
    }

    /// <summary>
    /// The first bare URL that starts at or after <paramref name="startAt"/>, or
    /// <see langword="null"/> when there is none. The character before
    /// <paramref name="startAt"/> still counts: a scheme glued to the word before it
    /// is not the start of a URL.
    /// </summary>
    public static BareUrl? FindNext(string text, int startAt)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(startAt);

        var at = startAt;
        while (at < text.Length)
        {
            if (!char.IsAsciiLetter(text[at]))
            {
                at++;
                continue;
            }

            // A whole run of letters at a time, so "xhttps" is one word that is not
            // a scheme rather than an "x" followed by one.
            var schemeEnd = at;
            while (schemeEnd < text.Length && char.IsAsciiLetter(text[schemeEnd])) schemeEnd++;

            if (StartsAWord(text, at) && Read(text, at, schemeEnd) is { } url) return url;

            at = schemeEnd;
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="url"/>, taken whole, is a URL a bare one would link:
    /// an <c>http</c> or <c>https</c> scheme, <c>://</c>, and a host. Nothing is
    /// trimmed — for a caller that already knows where its URL ends, such as
    /// CommonMark's <c>&lt;https://…&gt;</c> — so the same address is a link in
    /// either form or in neither.
    /// </summary>
    public static bool IsWebUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;

        var schemeEnd = 0;
        while (schemeEnd < url.Length && char.IsAsciiLetter(url[schemeEnd])) schemeEnd++;

        return IsWeb(url, 0, schemeEnd, url.Length);
    }

    private static bool StartsAWord(string text, int at) => at == 0 || !char.IsLetterOrDigit(text[at - 1]);

    private static BareUrl? Read(string text, int start, int schemeEnd)
    {
        // Not even "://" after the letters: there is no address to measure.
        if (!text.AsSpan(schemeEnd).StartsWith(Separator, StringComparison.Ordinal)) return null;

        var hostStart = schemeEnd + Separator.Length;
        var end = hostStart;
        while (end < text.Length && !EndsUrl(text[end])) end++;

        end = TrimEnd(text, hostStart, end);
        return IsWeb(text, start, schemeEnd, end) ? new BareUrl(start, text[start..end]) : null;
    }

    // The one test both forms share: a bare scheme, "://", a host, and the
    // allow-list's own last word on the whole address.
    private static bool IsWeb(string text, int start, int schemeEnd, int end)
    {
        if (!LinkSchemes.IsBare(text.AsSpan(start, schemeEnd - start))) return false;
        if (!text.AsSpan(schemeEnd, end - schemeEnd).StartsWith(Separator, StringComparison.Ordinal)) return false;

        return HasHost(text, schemeEnd + Separator.Length, end)
            && LinkSchemes.IsNavigable(text[start..end]);
    }

    // `<` and `>` both end it: the pair is CommonMark's own autolink, `<https://…>`,
    // and a bare URL read out of one must not carry the closing bracket with it.
    private static bool EndsUrl(char c) => char.IsWhiteSpace(c) || char.IsControl(c) || c is '<' or '>';

    private static int TrimEnd(string text, int from, int end)
    {
        var opened = 0;
        var closed = 0;
        for (var at = from; at < end; at++)
        {
            if (text[at] == '(') opened++;
            else if (text[at] == ')') closed++;
        }

        while (end > from)
        {
            var last = text[end - 1];
            if (TrailingPunctuation.Contains(last, StringComparison.Ordinal))
            {
                end--;
            }
            else if (last == ')' && closed > opened)
            {
                end--;
                closed--;
            }
            else
            {
                break;
            }
        }

        return end;
    }

    // The host is the run of domain characters straight after "://"; it has to
    // hold at least one letter or digit, so "https://." and "https:///x" are not
    // links. What follows it — a port, a path, a query — is the URL's own business.
    private static bool HasHost(string text, int from, int end)
    {
        var hasName = false;
        for (var at = from; at < end; at++)
        {
            var c = text[at];
            if (char.IsLetterOrDigit(c)) hasName = true;
            else if (c is not ('-' or '_' or '.')) break;
        }

        return hasName;
    }
}
