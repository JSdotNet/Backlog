namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// A window's reload of its task list for a write it did not make, coalesced: an
/// agent's burst — an import, a run of transitions — is one signal per write, so a
/// signal that lands while a reload is running marks one more as owed instead of
/// starting a second over the first. At most one reload is in flight, and the last
/// write is always read unless a reload fails: then the next write reads it.
/// <para>
/// One per window, because each window has its own list (local ADR 0043 §5): the
/// main window's <c>Home</c> holds one, and so does every <see cref="SurfaceWindow"/>.
/// Not thread-safe by design — the owner calls it only on its renderer's
/// dispatcher, which is where the reload has to run anyway.
/// </para>
/// </summary>
internal sealed class CoalescedReload
{
    private bool _running;
    private bool _owed;

    /// <summary>
    /// Runs <paramref name="reload"/> until no reload is owed, or marks one more as
    /// owed when a run is already going and returns at once. A failure lets go of
    /// what was owed and is thrown to the call that was running, for its owner to
    /// say; a later signal starts over.
    /// </summary>
    public async Task RunAsync(Func<Task> reload)
    {
        ArgumentNullException.ThrowIfNull(reload);

        _owed = true;
        if (_running) return;

        _running = true;

        try
        {
            while (_owed)
            {
                _owed = false;
                await reload();
            }
        }
        catch
        {
            _owed = false;
            throw;
        }
        finally
        {
            _running = false;
        }
    }
}
