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
/// At least one available pane always stays on-screen so the shell never renders empty.
/// The viewport decides how many panes may be shown at the same time.
/// </summary>
internal sealed class GlobalPaneSelection
{
    private static readonly GlobalPane[] KnownPaneOrder = [GlobalPane.Inbox, GlobalPane.Backlog, GlobalPane.Knowledge];
    private static readonly HashSet<GlobalPane> KnownPanes = [.. KnownPaneOrder];

    private readonly HashSet<GlobalPane> _enabled;
    private readonly HashSet<GlobalPane> _available = [.. KnownPanes];
    private int _capacity = KnownPaneOrder.Length;

    public GlobalPaneSelection()
        : this(GlobalPane.Backlog)
    {
    }

    public GlobalPaneSelection(params GlobalPane[] enabled)
    {
        _enabled = [.. enabled.Where(IsKnownPane).Distinct()];
        EnsureAtLeastOneAvailablePaneEnabled();
        TrimToCapacity();
    }

    public int Capacity => _capacity;

    public int EnabledCount => _enabled.Count;

    public IReadOnlyCollection<GlobalPane> Enabled => new ReadOnlyCollection<GlobalPane>([.. _enabled]);

    public bool IsAvailable(GlobalPane pane) => IsKnownPane(pane) && _available.Contains(pane);

    public bool IsEnabled(GlobalPane pane) => IsKnownPane(pane) && _enabled.Contains(pane);

    public bool CanDisable(GlobalPane pane) => IsEnabled(pane) && EnabledAvailableCount > 1;

    public bool CanEnable(GlobalPane pane)
    {
        if (!IsKnownPane(pane) || !_available.Contains(pane))
        {
            return false;
        }

        return IsEnabled(pane) || EnabledAvailableCount < _capacity || _capacity == 1;
    }

    public bool TrySetCapacity(int capacity)
    {
        var clamped = Math.Clamp(capacity, 1, KnownPaneOrder.Length);
        if (clamped == _capacity)
        {
            return false;
        }

        _capacity = clamped;
        TrimToCapacity();
        EnsureAtLeastOneAvailablePaneEnabled();
        return true;
    }

    public bool TrySetEnabled(GlobalPane pane, bool enabled)
    {
        if (!IsKnownPane(pane) || !_available.Contains(pane))
        {
            return false;
        }

        if (enabled)
        {
            if (_enabled.Contains(pane))
            {
                return false;
            }

            if (_capacity == 1)
            {
                _enabled.Clear();
                _enabled.Add(pane);
                return true;
            }

            if (EnabledAvailableCount >= _capacity)
            {
                return false;
            }

            return _enabled.Add(pane);
        }

        if (!_enabled.Contains(pane) || EnabledAvailableCount == 1)
        {
            return false;
        }

        _enabled.Remove(pane);
        return true;
    }

    public bool TrySetAvailable(GlobalPane pane, bool available)
    {
        if (!IsKnownPane(pane))
        {
            return false;
        }

        var changed = available ? _available.Add(pane) : _available.Remove(pane);
        if (!changed)
        {
            return false;
        }

        if (!available)
        {
            _enabled.Remove(pane);
        }

        EnsureAtLeastOneAvailablePaneEnabled();
        TrimToCapacity();
        return true;
    }

    public bool Toggle(GlobalPane pane) => TrySetEnabled(pane, !IsEnabled(pane));

    private int EnabledAvailableCount => _enabled.Count(pane => _available.Contains(pane));

    private void EnsureAtLeastOneAvailablePaneEnabled()
    {
        if (_available.Count == 0)
        {
            _available.Add(GlobalPane.Backlog);
        }

        _enabled.IntersectWith(_available);

        if (_enabled.Count > 0)
        {
            return;
        }

        _enabled.Add(DefaultPane());
    }

    private void TrimToCapacity()
    {
        while (EnabledAvailableCount > _capacity)
        {
            var paneToRemove = KnownPaneOrder.FirstOrDefault(_enabled.Contains);
            if (!_enabled.Remove(paneToRemove))
            {
                break;
            }
        }
    }

    private GlobalPane DefaultPane()
    {
        if (_available.Contains(GlobalPane.Backlog))
        {
            return GlobalPane.Backlog;
        }

        return KnownPaneOrder.First(_available.Contains);
    }

    private static bool IsKnownPane(GlobalPane pane) => KnownPanes.Contains(pane);
}
