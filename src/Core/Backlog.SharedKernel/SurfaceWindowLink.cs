namespace Backlog.SharedKernel;

/// <summary>
/// A <c>backlog://window/{surfaceId}?…</c> link, read by <see cref="SurfaceLink.Parse"/>
/// (local ADR 0041 §7): the surface it names and the query it carries, such as
/// <c>plan</c>, <c>entry</c>, <c>chapter</c> and <c>scope</c> (§6). The link carries no
/// token, so one written into a brief or a digest still opens days later.
/// </summary>
/// <param name="SurfaceId">The surface the link names, unescaped.</param>
/// <param name="Query">Its query, each key and value unescaped; a key given twice keeps
/// its first value.</param>
public sealed record SurfaceWindowLink(string SurfaceId, IReadOnlyDictionary<string, string> Query);
