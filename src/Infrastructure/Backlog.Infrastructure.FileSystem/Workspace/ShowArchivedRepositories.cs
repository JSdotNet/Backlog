using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Answers the shared kernel's <see cref="IShowArchivedRepositories"/> from the device's
/// <see cref="ShellNavigationStore"/>, under its <c>showArchivedRepositories</c> key: per
/// device and never synced (local ADR 0029, §3), on the footing of the roadmap's view
/// choices (local ADR 0019, §4).
/// <para>
/// Read from the store on every access rather than held, so a shell the Router built
/// afresh reads the choice as it was left. <see cref="Changed"/> fires only when the
/// choice moves, not on every write of the store, which also saves the scope and the
/// view on every press.
/// </para>
/// <para>
/// A host that composed no such store still resolves the port: the choice is then held
/// for the life of this instance and forgotten on restart.
/// </para>
/// </summary>
public sealed class ShowArchivedRepositories : IShowArchivedRepositories, IDisposable
{
    private readonly ShellNavigationStore? _store;
    private bool _held;
    private bool _last;

    public ShowArchivedRepositories(ShellNavigationStore? store)
    {
        _store = store;
        _last = Shown;
        if (_store is not null) _store.Changed += OnStoreChanged;
    }

    public event Action? Changed;

    public bool Shown => _store?.ShowArchivedRepositories ?? _held;

    public void SetShown(bool shown)
    {
        if (_store is not null)
        {
            // The store announces its own save; OnStoreChanged turns that into ours.
            _store.SetShowArchivedRepositories(shown);
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
