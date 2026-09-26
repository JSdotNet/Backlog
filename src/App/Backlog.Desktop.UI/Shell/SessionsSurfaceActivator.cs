using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The shell's answer to <c>open_dashboard</c>: whatever windows are showing the
/// application attach themselves here, and a caller from outside the user interface
/// can ask them whether they offer the Sessions pane.
/// <para>
/// Asking, not showing. The surfaces are the reader's: only a click in the window
/// changes what it shows. An earlier version brought the pane forward here, and since
/// every agent run calls <c>open_dashboard</c> when it starts, the window jumped to
/// Sessions whenever an agent did — the thing this class must never do again.
/// </para>
/// <para>
/// A registry of attached shells rather than a single one, because the harness runs a
/// circuit per browser tab and a person may have two open. Each is asked, and the best
/// answer wins — one window offering the pane is the pane being available, whatever a
/// second window was doing.
/// </para>
/// </summary>
public sealed class SessionsSurfaceActivator : ISessionsSurfaceActivator
{
    private readonly List<Func<CancellationToken, Task<DeliverySurfaceActivation>>> _shells = [];
    private readonly object _gate = new();

    /// <summary>
    /// Attach a shell. The returned handle detaches it, and a shell that does not
    /// dispose its handle is a window this class goes on calling into after it is
    /// gone.
    /// </summary>
    /// <param name="shell">What to run to answer. It is invoked off
    /// the renderer's synchronisation context, so an implementation that touches
    /// component state marshals onto it first.</param>
    public IDisposable Attach(Func<CancellationToken, Task<DeliverySurfaceActivation>> shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        lock (_gate)
        {
            _shells.Add(shell);
        }

        return new Attachment(this, shell);
    }

    /// <inheritdoc />
    public async Task<DeliverySurfaceActivation> ActivateAsync(CancellationToken cancellationToken = default)
    {
        Func<CancellationToken, Task<DeliverySurfaceActivation>>[] shells;

        lock (_gate)
        {
            shells = [.. _shells];
        }

        // Snapshotted, then awaited outside the lock. A shell's handler marshals onto
        // its renderer, which can wait behind a render, and holding the lock across
        // that would block every other window's attach and detach behind one slow paint.
        var best = DeliverySurfaceActivation.Unattached;

        foreach (var shell in shells)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var answer = await shell(cancellationToken).ConfigureAwait(false);

                if (answer > best) best = answer;
            }
            catch (Exception error) when (error is ObjectDisposedException or InvalidOperationException)
            {
                // A window that went away between the snapshot and the call. Its
                // disposal races this by construction, and a torn-down circuit is not
                // a failure of the caller's request — it is one fewer window that
                // could have answered.
            }
        }

        return best;
    }

    private void Detach(Func<CancellationToken, Task<DeliverySurfaceActivation>> shell)
    {
        lock (_gate)
        {
            _shells.Remove(shell);
        }
    }

    private sealed class Attachment(SessionsSurfaceActivator activator, Func<CancellationToken, Task<DeliverySurfaceActivation>> shell)
        : IDisposable
    {
        private bool _detached;

        public void Dispose()
        {
            if (_detached) return;

            _detached = true;
            activator.Detach(shell);
        }
    }
}
