using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// How a popped-out window reaches the main window's shell (local ADR 0043 §4):
/// <c>Home</c> attaches itself here, the way it attaches to
/// <see cref="SessionsSurfaceActivator"/>, and a <see cref="SurfaceWindow"/>'s
/// <b>Show in main window</b> — or a jump to a surface the catalog cannot show in a
/// window yet — asks it to show a surface.
/// <para>
/// Process-wide, because the windows are not: each window has its own scope, so
/// its own <see cref="ISurfaceNavigator"/>, and only a singleton is shared between
/// them. The shell attached last answers — the desktop head has one main window, and
/// in the web harness the tab opened last is the one in front.
/// </para>
/// </summary>
public sealed class MainWindowSurfaces
{
    private readonly List<Func<SurfaceRequest, Task<bool>>> _shells = [];
    private readonly object _gate = new();

    /// <summary>
    /// Attach a main window's shell. The returned handle detaches it; a shell that
    /// does not dispose it is a window this class goes on calling into after it is gone.
    /// </summary>
    /// <param name="shell">Shows the surface asked for and answers whether it could.
    /// Invoked off the renderer's synchronisation context, so it marshals onto its
    /// own before it touches component state.</param>
    public IDisposable Attach(Func<SurfaceRequest, Task<bool>> shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        lock (_gate)
        {
            _shells.Add(shell);
        }

        return new Attachment(this, shell);
    }

    /// <summary>Whether a main window's shell is attached to answer.</summary>
    public bool Attached
    {
        get
        {
            lock (_gate)
            {
                return _shells.Count > 0;
            }
        }
    }

    /// <summary>Asks the main window to show <paramref name="request"/>'s surface.
    /// Answers false when no main window is attached, when it went away meanwhile, or
    /// when its shell cannot show that surface.</summary>
    public async Task<bool> ShowAsync(SurfaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Func<SurfaceRequest, Task<bool>>? shell;
        lock (_gate)
        {
            shell = _shells.Count == 0 ? null : _shells[^1];
        }

        if (shell is null) return false;

        try
        {
            return await shell(request).ConfigureAwait(false);
        }
        catch (Exception error) when (error is ObjectDisposedException or InvalidOperationException)
        {
            // The main window went away between the lookup and the call: nothing
            // showed the surface, which is the answer, not a failure of the caller.
            return false;
        }
    }

    private void Detach(Func<SurfaceRequest, Task<bool>> shell)
    {
        lock (_gate)
        {
            _shells.Remove(shell);
        }
    }

    private sealed class Attachment(MainWindowSurfaces owner, Func<SurfaceRequest, Task<bool>> shell) : IDisposable
    {
        private bool _detached;

        public void Dispose()
        {
            if (_detached) return;

            _detached = true;
            owner.Detach(shell);
        }
    }
}
