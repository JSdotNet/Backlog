using Backlog.Desktop.UI.Shell;
using Microsoft.JSInterop;

namespace Backlog.Desktop.WebHarness;

/// <summary>
/// This harness's answer to <see cref="IShellWindows"/> (local ADR 0040 §7): a ⧉
/// press opens <c>/window/{surfaceId}</c>, with its query, in a new browser tab.
/// <para>
/// Nothing else is needed for the tab to be a window of its own: each tab starts a
/// circuit, and a circuit is a scope, so the tab gets its own view state the way a
/// desktop window gets its own <c>BlazorWebView</c> (§5).
/// </para>
/// <para>
/// A browser keeps the rest of the contract out of a page's reach, so the rest does
/// nothing. No tab is opened under a key — this host cascades none, so
/// <see cref="SurfaceWindow"/> never asks to close or retitle one, and a script may
/// only close a tab it opened itself; and a page cannot bring another tab to the
/// front.
/// </para>
/// Scoped, because <see cref="IJSRuntime"/> is the circuit's: the tab is opened from
/// the tab whose ⧉ was pressed.
/// </summary>
public sealed class BrowserTabShellWindows(IJSRuntime js, ILogger<BrowserTabShellWindows> logger) : IShellWindows
{
    /// <inheritdoc />
    public void Open(string surfaceId, IReadOnlyDictionary<string, string>? query = null)
    {
        // The path is root-relative, which is this host's base href.
        var path = SurfaceWindowRoute.PathFor(surfaceId, query);

        // The contract is synchronous and the press that asks is the renderer's, so
        // the call is left to finish on its own. It runs inside the press's user
        // activation, which is what lets the browser open the tab rather than block it
        // as a pop-up. A failure — the circuit gone before the call landed — is
        // logged, never thrown into the renderer.
        _ = OpenTabAsync(path);
    }

    /// <inheritdoc />
    public void Close(string windowKey)
    {
    }

    /// <inheritdoc />
    public void SetTitle(string windowKey, string title)
    {
    }

    /// <inheritdoc />
    public void ActivateMainWindow()
    {
    }

    private async Task OpenTabAsync(string path)
    {
        try
        {
            await js.InvokeVoidAsync("open", path, "_blank");
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Could not open {Path} in a new browser tab.", path);
        }
    }
}
