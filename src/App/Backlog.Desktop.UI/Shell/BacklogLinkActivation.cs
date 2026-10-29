using Backlog.SharedKernel;

using Microsoft.Extensions.Logging;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// What activating a <c>backlog://window/{surfaceId}?…</c> link does in the desktop app
/// (local ADR 0041 §7): one more window on the surface it names, through
/// <see cref="IShellWindows"/>, with the query it carries.
/// <list type="bullet">
/// <item>A link that arrives before the app has started — the one the app was launched
/// with — waits, and opens once <see cref="Started"/> says the main window is up.</item>
/// <item>A link that names a surface <see cref="ISurfaceCatalog"/> does not offer — an
/// unknown id, one whose feature is off, or the reserved <c>proposals</c> until its pane
/// registers — opens nothing and logs a warning, as does text that is no window link.
/// Nothing is shown to the person: the link came from outside the app, and the app
/// cannot tell whether anyone is watching it.</item>
/// </list>
/// The desktop head feeds it: the protocol activation at launch, and every activation a
/// later launch redirected to this instance. A singleton, for the windows outlive every
/// window's scope.
/// </summary>
public sealed class BacklogLinkActivation(IShellWindows windows, ISurfaceCatalog catalog, ILogger<BacklogLinkActivation> logger)
{
    private readonly object _gate = new();
    private readonly List<string?> _waiting = [];
    private bool _started;

    /// <summary>Opens the window <paramref name="link"/> asks for, or keeps it until
    /// <see cref="Started"/> when the app is not up yet.</summary>
    public void Activate(string? link)
    {
        lock (_gate)
        {
            if (!_started)
            {
                _waiting.Add(link);
                return;
            }
        }

        Open(link);
    }

    /// <summary>The main window is up: every link that waited opens, in the order it
    /// arrived. A second call opens nothing again.</summary>
    public void Started()
    {
        string?[] waiting;
        lock (_gate)
        {
            if (_started) return;

            _started = true;
            waiting = [.. _waiting];
            _waiting.Clear();
        }

        foreach (var link in waiting) Open(link);
    }

    private void Open(string? text)
    {
        if (SurfaceLink.Parse(text) is not { } link)
        {
            logger.LogWarning("The activation {Link} is no backlog://window/ link; no window was opened.", text);
            return;
        }

        if (!catalog.TryGet(link.SurfaceId, out var surface))
        {
            logger.LogWarning(
                "The link {Link} names the surface '{SurfaceId}', which this app does not offer; no window was opened.",
                text,
                link.SurfaceId);
            return;
        }

        windows.Open(surface.Id, link.Query);
    }
}
