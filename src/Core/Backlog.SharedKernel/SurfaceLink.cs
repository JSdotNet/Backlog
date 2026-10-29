namespace Backlog.SharedKernel;

/// <summary>
/// The addresses a registered <see cref="ShellSurface"/> opens at (local ADR 0041 §6,
/// §7): the path <c>/window/{surfaceId}</c> with the surface's query, and the
/// <c>backlog://window/{surfaceId}?…</c> link the installed app is activated by, and
/// that link read back into a <see cref="SurfaceWindowLink"/>.
/// <para>
/// Here rather than beside the shell, because it is spelled by more than the shell:
/// the desktop head and the browser tabs open the path, and the <c>open_window</c>
/// tool, which cannot reference the shell, answers both. <see cref="Parse"/> sits
/// beside the builder, so the one format is written and read in one place, as pure
/// functions tested without Windows.
/// </para>
/// </summary>
public static class SurfaceLink
{
    /// <summary>The scheme the installed app registers (local ADR 0041 §7).</summary>
    public const string Scheme = "backlog";

    /// <summary>The path a surface's window renders: <c>/window/{surfaceId}</c> and the
    /// query, each key and value escaped, in the order <paramref name="query"/> lists
    /// them.</summary>
    public static string Path(string surfaceId, IEnumerable<KeyValuePair<string, string>>? query = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);

        var path = "/window/" + Uri.EscapeDataString(surfaceId);
        var pairs = query?.ToList() ?? [];
        if (pairs.Count == 0) return path;

        return path + "?" + string.Join('&', pairs.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
    }

    /// <summary>The link that opens a native window on the surface in the running app:
    /// <c>backlog://window/{surfaceId}?…</c>. It carries no token, so it stays valid in
    /// a file read days later (local ADR 0041 §7).</summary>
    public static string Protocol(string surfaceId, IEnumerable<KeyValuePair<string, string>>? query = null) =>
        Scheme + ":/" + Path(surfaceId, query);

    private const string Prefix = Scheme + "://";
    private const string WindowSegment = "window/";

    /// <summary>
    /// The window link <paramref name="text"/> is, or null when it is none: another
    /// scheme, another <c>backlog://</c> host (such as <c>devbook</c>), no surface id,
    /// or a path deeper than the one segment. Whitespace and the quotes a command line
    /// may leave around it are ignored, and so are the case of the scheme and of
    /// <c>window</c>, and one trailing slash after the surface id. Whether the surface
    /// is on offer is not this method's question: the surface id is kept as written,
    /// and the catalog answers it, the reserved <c>proposals</c> included.
    /// </summary>
    public static SurfaceWindowLink? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var link = text.Trim().Trim('"').Trim();
        if (!link.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;

        // A fragment belongs to no part of the link that opens anything.
        var fragment = link.IndexOf('#', StringComparison.Ordinal);
        if (fragment >= 0) link = link[..fragment];

        var queryStart = link.IndexOf('?', StringComparison.Ordinal);
        var path = queryStart < 0 ? link[Prefix.Length..] : link[Prefix.Length..queryStart];
        if (!path.StartsWith(WindowSegment, StringComparison.OrdinalIgnoreCase)) return null;

        var segment = path[WindowSegment.Length..];
        if (segment.EndsWith('/')) segment = segment[..^1];
        if (segment.Length == 0 || segment.Contains('/', StringComparison.Ordinal)) return null;

        var surfaceId = Uri.UnescapeDataString(segment);
        if (string.IsNullOrWhiteSpace(surfaceId)) return null;

        return new SurfaceWindowLink(surfaceId, QueryOf(link));
    }

    /// <summary>The first window link among <paramref name="arguments"/>, such as a
    /// process's command line, or null when none is.</summary>
    public static SurfaceWindowLink? Find(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (var argument in arguments)
        {
            if (Parse(argument) is { } link) return link;
        }

        return null;
    }

    /// <summary>The query of <paramref name="uri"/>, each key and value unescaped, a
    /// <c>+</c> read as a space. A key given twice keeps its first value.</summary>
    public static Dictionary<string, string> QueryOf(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        var start = uri.IndexOf('?', StringComparison.Ordinal);
        if (start < 0) return query;

        var end = uri.IndexOf('#', start);
        var text = end < 0 ? uri[(start + 1)..] : uri[(start + 1)..end];
        foreach (var part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            var key = Unescape(equals < 0 ? part : part[..equals]);
            var value = equals < 0 ? string.Empty : Unescape(part[(equals + 1)..]);
            if (key.Length > 0) query.TryAdd(key, value);
        }

        return query;
    }

    private static string Unescape(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));
}
