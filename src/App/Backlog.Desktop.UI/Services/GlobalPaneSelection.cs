using System.Collections.ObjectModel;

namespace Backlog.Desktop.UI.Services;

internal enum GlobalPane
{
    Inbox,
    Backlog,
    Knowledge
}

/// <summary>
/// Tracks which of the shell's global panes are visible right now.
/// At least one pane always stays on-screen so the shell never renders empty.
/// </summary>
internal sealed class GlobalPaneSelection
{
    private readonly HashSet<GlobalPane> _enabled;

    public GlobalPaneSelection()
        : this(GlobalPane.Backlog)
    {
    }

    public GlobalPaneSelection(params GlobalPane[] enabled)
    {
        _enabled = enabled.Length == 0
            ? [GlobalPane.Backlog]
            : [.. enabled.Distinct()];

        if (_enabled.Count == 0)
        {
            _enabled.Add(GlobalPane.Backlog);
        }
    }

    public int EnabledCount => _enabled.Count;

    public IReadOnlyCollection<GlobalPane> Enabled => new ReadOnlyCollection<GlobalPane>([.. _enabled]);

    public bool IsEnabled(GlobalPane pane) => _enabled.Contains(pane);

    public bool CanDisable(GlobalPane pane) => _enabled.Contains(pane) && _enabled.Count > 1;

    public bool TrySetEnabled(GlobalPane pane, bool enabled)
    {
        if (enabled)
        {
            return _enabled.Add(pane);
        }

        if (!_enabled.Contains(pane)) return false;
        if (_enabled.Count == 1) return false;

        _enabled.Remove(pane);
        return true;
    }

    public bool Toggle(GlobalPane pane) => TrySetEnabled(pane, !IsEnabled(pane));
}
