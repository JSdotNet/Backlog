using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Opens a registered surface in an OS window of its own, beside the main window
/// (local ADR 0043 §4). The desktop head answers it with <c>ShellWindows</c>, a
/// window per call; a host with no windows to open composes none, and a caller
/// resolves it as optional.
/// </summary>
public interface IShellWindows
{
    /// <summary>Opens another window on <paramref name="surfaceId"/>, with the query
    /// keys that surface registered. Every call opens one more: a surface may be open
    /// in any number of windows at once.</summary>
    void Open(string surfaceId, IReadOnlyDictionary<string, string>? query = null);

    /// <summary>Opens another window on <paramref name="surfaceId"/>, as
    /// <see cref="Open"/> does, and answers once it is known whether the window was
    /// built and handed to the system: false when there was nothing to open it in, or
    /// building it failed. What a caller that reports the outcome — the
    /// <c>open_window</c> tool — awaits, so it never says "opened" before a window
    /// exists.</summary>
    Task<bool> OpenAsync(string surfaceId, IReadOnlyDictionary<string, string>? query = null);

    /// <summary>Closes the popped-out window <paramref name="windowKey"/> names. The
    /// main window is never closed this way.</summary>
    void Close(string windowKey);

    /// <summary>Writes <paramref name="title"/> into the popped-out window's title
    /// bar, after a jump switched the surface it shows.</summary>
    void SetTitle(string windowKey, string title);

    /// <summary>Brings the main window to the front.</summary>
    void ActivateMainWindow();

    /// <summary>Whether a popped-out window can be remembered to reopen on the next
    /// start (local ADR 0043 §5): true in the desktop head only. A host that answers
    /// false — a browser tab, a test — draws no pin.</summary>
    bool CanRemember => false;

    /// <summary>Whether the popped-out window <paramref name="windowKey"/> names is
    /// remembered to reopen on the next start.</summary>
    bool IsRemembered(string windowKey) => false;

    /// <summary>Marks the popped-out window <paramref name="windowKey"/> names
    /// remembered, so it reopens on the next start on what it shows then, or forgets
    /// it. Changes that one window alone.</summary>
    void SetRemembered(string windowKey, bool remembered)
    {
    }

    /// <summary>What the popped-out window <paramref name="windowKey"/> names shows
    /// now, after a jump or a scope change: what a remembered window reopens on.</summary>
    void ReportShowing(string windowKey, string surfaceId, IReadOnlyDictionary<string, string> query)
    {
    }
}

/// <summary>
/// The route a popped-out window renders (local ADR 0043 §6), and the name of the
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
    /// key given twice keeps its first value. <see cref="SurfaceLink.QueryOf"/> reads
    /// it, as it reads a <c>backlog://window/…</c> link's.</summary>
    public static Dictionary<string, string> QueryOf(string uri) => SurfaceLink.QueryOf(uri);
}
