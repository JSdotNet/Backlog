using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Answers the Devbook module's <see cref="IBrainShowHidden"/> from the device's
/// <see cref="ShellNavigationStore"/>, under its <c>brainHiddenShown</c> key: per
/// device and never synced, never in the Brain folder (local ADR 0026 §2), on the
/// footing of show archived (<see cref="ShowArchivedRepositories"/>).
/// <para>
/// Read from the store on every access rather than held, so a pane the Router
/// built afresh reads the choice as it was left. <see cref="Changed"/> fires only
/// when the choice moves, not on every write of the store, which also saves the
/// scope and the view on every press.
/// </para>
/// <para>
/// A host that composed no such store still resolves the port: the choice is then
/// held for the life of this instance and forgotten on restart.
/// </para>
/// </summary>
public sealed class BrainShowHidden : IBrainShowHidden, IDisposable
{
    private readonly ShellNavigationStore? _store;
    private bool _held;
    private bool _last;

    public BrainShowHidden(ShellNavigationStore? store)
    {
        _store = store;
        _last = Shown;
        if (_store is not null) _store.Changed += OnStoreChanged;
    }

    public event Action? Changed;

    public bool Shown => _store?.BrainHiddenShown ?? _held;

    public void SetShown(bool shown)
    {
        if (_store is not null)
        {
            // The store announces its own save; OnStoreChanged turns that into ours.
            _store.SetBrainHiddenShown(shown);
            return;
        }

        _held = shown;
        OnStoreChanged();
    }

    public void Dispose()
    {
        if (_store is not null) _store.Changed -= OnStoreChanged;
    }

    private void OnStoreChanged()
    {
        var shown = Shown;
        if (shown == _last) return;

        _last = shown;
        Changed?.Invoke();
    }
}
