using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Opens a registered surface in an OS window of its own, beside the main window
/// (local ADR 0041 §4). The desktop head answers it with <c>ShellWindows</c>, a
/// window per call; a host with no windows to open composes none, and a caller
/// resolves it as optional.
/// </summary>
public interface IShellWindows
{
    /// <summary>Opens another window on <paramref name="surfaceId"/>, with the query
    /// keys that surface registered. Every call opens one more: a surface may be open
    /// in any number of windows at once.</summary>
    void Open(string surfaceId, IReadOnlyDictionary<string, string>? query = null);

    /// <summary>Closes the popped-out window <paramref name="windowKey"/> names. The
    /// main window is never closed this way.</summary>
    void Close(string windowKey);

    /// <summary>Writes <paramref name="title"/> into the popped-out window's title
    /// bar, after a jump switched the surface it shows.</summary>
    void SetTitle(string windowKey, string title);

    /// <summary>Brings the main window to the front.</summary>
    void ActivateMainWindow();
}

/// <summary>
/// The route a popped-out window renders (local ADR 0041 §6), and the name of the
/// value that tells one window from another.
/// </summary>
public static class SurfaceWindowRoute
{
    /// <summary>The route's template; <see cref="SurfaceWindow"/> is its page.</summary>
    public const string Template = "/window/{SurfaceId}";

    /// <summary>The name the window's key is cascaded under, from the root
    /// component the window's head hands it to (<see cref="Routes.WindowKey"/>).</summary>
    public const string WindowKeyCascade = "ShellWindowKey";

    /// <summary>The path a window opens on: <c>/window/{surfaceId}</c> and the query,
    /// each key and value escaped. <see cref="SurfaceLink.Path"/> spells it, so the
    /// <c>open_window</c> tool's links name the same path.</summary>
    public static string PathFor(string surfaceId, IReadOnlyDictionary<string, string>? query = null) =>
        SurfaceLink.Path(surfaceId, query);

    /// <summary>The query of <paramref name="uri"/>, each key and value unescaped. A
    /// key given twice keeps its first value.</summary>
    public static Dictionary<string, string> QueryOf(string uri)
    {
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
