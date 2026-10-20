using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Answers Devbook's <see cref="IDevbookViewPreferences"/> from the device's
/// <see cref="ShellNavigationStore"/>, the file that already remembers how the reader
/// last had the shell drawn. Per device and never synced, as the tools pane's view
/// choices are.
/// <para>
/// A host that composed no such store still resolves the port: the choice is then held
/// for the life of this instance and forgotten on restart.
/// </para>
/// </summary>
public sealed class DevbookViewPreferences : IDevbookViewPreferences
{
    private readonly ShellNavigationStore? _store;
    private bool _hintDismissed;

    public DevbookViewPreferences(ShellNavigationStore? store)
    {
        _store = store;
    }

    public bool C4ZoomHintDismissed => _store?.C4ZoomHintDismissed ?? _hintDismissed;

    public void DismissC4ZoomHint()
    {
        if (_store is null)
        {
            _hintDismissed = true;
            return;
        }

        _store.DismissC4ZoomHint();
    }
}
