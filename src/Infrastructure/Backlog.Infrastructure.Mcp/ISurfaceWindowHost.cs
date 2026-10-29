namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// What the host that serves <c>open_window</c> can do with a surface (local ADR 0042
/// §7): open it in a native window, and hand out a web link to it.
/// <para>
/// A port of this project, answered by each host, because what opens a window and
/// what mints a link are the host's: the desktop head's windows and the link tokens
/// of its loopback listener live under <c>src/App</c>, which this project may not
/// reference. The tool has already checked the surface and its query against the
/// catalog when it calls either member.
/// </para>
/// </summary>
public interface ISurfaceWindowHost
{
    /// <summary>
    /// Opens one more native window on <paramref name="surfaceId"/> with
    /// <paramref name="query"/>, beside the main window, and answers once it is known
    /// whether one was: false when this host has no native windows to open — the web
    /// harness — or the window could not be built, and nothing was opened.
    /// </summary>
    Task<bool> TryOpenWindowAsync(string surfaceId, IReadOnlyDictionary<string, string> query);

    /// <summary>
    /// The full web address of the surface's page with <paramref name="query"/>, a link
    /// token of its own appended where this host guards its pages with one. Null when
    /// nothing serves the pages now — the listener is not listening — and no token was
    /// minted.
    /// </summary>
    Uri? WebLink(string surfaceId, IReadOnlyDictionary<string, string> query);
}
