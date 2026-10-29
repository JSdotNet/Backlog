namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// A <c>backlog://window/{surfaceId}?…</c> link, read (local ADR 0041 §7): the surface
/// it names and the query it carries, such as <c>plan</c>, <c>entry</c>,
/// <c>chapter</c> and <c>scope</c> (§6). The link carries no token, so one written
/// into a brief or a digest still opens days later.
/// <para>
/// Read here and nowhere else, as a pure function, so that what the protocol
/// activation of the desktop head accepts is tested without Windows. Whether the
/// surface is on offer is not this type's question: <see cref="SurfaceId"/> is kept
/// as written, and the catalog answers it, the reserved <c>proposals</c> included.
/// </para>
/// </summary>
/// <param name="SurfaceId">The surface the link names, unescaped.</param>
/// <param name="Query">Its query, each key and value unescaped; a key given twice keeps
/// its first value.</param>
public sealed record BacklogWindowLink(string SurfaceId, IReadOnlyDictionary<string, string> Query)
{
    /// <summary>The scheme the desktop head registers.</summary>
    public const string Scheme = "backlog";

    private const string Prefix = Scheme + "://";
    private const string WindowSegment = "window/";

    /// <summary>
    /// The window link <paramref name="text"/> is, or null when it is none: another
    /// scheme, another <c>backlog://</c> host (such as <c>devbook</c>), no surface id,
    /// or a path deeper than the one segment. Whitespace and the quotes a command line
    /// may leave around it are ignored, and so are the case of the scheme and of
    /// <c>window</c>, and one trailing slash after the surface id.
    /// </summary>
    public static BacklogWindowLink? Parse(string? text)
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

        return new BacklogWindowLink(surfaceId, SurfaceWindowRoute.QueryOf(link));
    }

    /// <summary>The first window link among <paramref name="arguments"/>, such as a
    /// process's command line, or null when none is.</summary>
    public static BacklogWindowLink? Find(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (var argument in arguments)
        {
            if (Parse(argument) is { } link) return link;
        }

        return null;
    }
}
