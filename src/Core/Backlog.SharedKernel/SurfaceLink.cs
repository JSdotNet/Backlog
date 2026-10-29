namespace Backlog.SharedKernel;

/// <summary>
/// The addresses a registered <see cref="ShellSurface"/> opens at (local ADR 0041 §6,
/// §7): the path <c>/window/{surfaceId}</c> with the surface's query, and the
/// <c>backlog://window/{surfaceId}?…</c> link the installed app is activated by.
/// <para>
/// Here rather than beside the shell, because it is spelled by more than the shell:
/// the desktop head and the browser tabs open the path, and the <c>open_window</c>
/// tool, which cannot reference the shell, answers both. The parser that reads a
/// <c>backlog://window/…</c> link back belongs beside this builder, so the one
/// format is written and read in one place.
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
}
