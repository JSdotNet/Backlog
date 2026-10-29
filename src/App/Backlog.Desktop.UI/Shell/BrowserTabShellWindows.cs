using Microsoft.JSInterop;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// <see cref="IShellWindows"/> for a surface served as a web page (local ADR 0041 §7):
/// a browser has tabs rather than OS windows, so a pop-out opens
/// <c>/window/{surfaceId}</c> in a new tab, and a window's own title and close are
/// its tab's. Both the desktop app's served pages and the web harness register it;
/// the harness opens no tab under a window key, so it is never asked to close or
/// retitle one there.
/// <para>
/// Scoped, one per circuit, because it speaks through that circuit's
/// <see cref="IJSRuntime"/>. Every call is fire and forget, as the interface's are:
/// a page still prerendering has no browser to speak to yet, and a tab already gone
/// has none left, and neither is a failure anyone could act on.
/// </para>
/// <para>
/// The window keys the interface passes are ignored: a circuit is one tab, and the
/// only window it can retitle or close is its own.
/// </para>
/// </summary>
public sealed class BrowserTabShellWindows(IJSRuntime js) : IShellWindows
{
    private const string AppTitle = "Backlog";

    /// <inheritdoc />
    public void Open(string surfaceId, IReadOnlyDictionary<string, string>? query = null) =>
        Run("open", SurfaceWindowRoute.PathFor(surfaceId, query), "_blank");

    /// <inheritdoc />
    /// <remarks>A browser closes only a tab a script opened; any other stays open,
    /// and that is the browser's rule rather than this page's.</remarks>
    public void Close(string windowKey) => Run("backlogShellWindow.close");

    /// <inheritdoc />
    public void SetTitle(string windowKey, string title) => Run("backlogShellWindow.setTitle", $"{title} - {AppTitle}");

    /// <inheritdoc />
    /// <remarks>Nothing: a page in a browser cannot bring the desktop app's window
    /// to the front. Show in main window has already switched it.</remarks>
    public void ActivateMainWindow()
    {
    }

    private void Run(string identifier, params object?[] arguments)
    {
        try
        {
            _ = Settle(js.InvokeVoidAsync(identifier, arguments));
        }
        catch (InvalidOperationException)
        {
            // Prerendering: there is no browser on the other end yet. The
            // interactive render that follows asks again.
        }
    }

    private static async Task Settle(ValueTask call)
    {
        try
        {
            await call.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or OperationCanceledException or InvalidOperationException)
        {
            // The tab went, or the browser refused; nothing on the server to undo.
        }
    }
}
