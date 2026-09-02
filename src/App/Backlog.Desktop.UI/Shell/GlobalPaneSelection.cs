using System.Collections.ObjectModel;

namespace Backlog.Desktop.UI.Shell;

internal enum GlobalPane
{
    Inbox,
    Tasks,
    Knowledge
}

/// <summary>
/// Tracks which of the shell's global panes are visible right now.
/// At least one available pane always stays on-screen so the shell never renders empty.
/// The viewport decides how many panes may be shown at the same time.
/// <para>
/// Switching to a pane is exclusive: it closes every pane the reader did not pin.
/// A pin is the reader saying "keep this one, whatever I look at next", which is why
/// it only ever describes a pane that is already open — every mutator that closes a
/// pane drops its pin with it.
/// </para>
/// </summary>
internal sealed class GlobalPaneSelection
{
    private static readonly GlobalPane[] KnownPaneOrder = [GlobalPane.Inbox, GlobalPane.Tasks, GlobalPane.Knowledge];
    private static readonly HashSet<GlobalPane> KnownPanes = [.. KnownPaneOrder];

    /// <summary>The name <see cref="GlobalPane.Tasks"/> was stored under before the
    /// Backlog bounded context was renamed to Tasks.</summary>
    private const string LegacyTasksPaneName = "Backlog";

    private readonly HashSet<GlobalPane> _enabled;
    private readonly HashSet<GlobalPane> _available = [.. KnownPanes];
    private readonly HashSet<GlobalPane> _pinned = [];
    private int _capacity = KnownPaneOrder.Length;

    public GlobalPaneSelection()
        : this(GlobalPane.Tasks)
    {
    }

    /// <summary>
    /// Reads a persisted pane name, accepting the name a pane was stored under
    /// before the rename.
    /// <para>
    /// The shell writes its open and pinned panes to <c>shell-navigation.json</c> as
    /// <c>ToString()</c> values, so a member name here is a stored value and not only
    /// an identifier. A plain <see cref="Enum.TryParse{TEnum}(string, out TEnum)"/>
    /// returns <see langword="false"/> for a layout saved as "Backlog", and the pane
    /// would be dropped on restore — the reader would lose the arrangement they left
    /// the app in, which reads as the app forgetting rather than as a rename.
    /// </para>
    /// </summary>
    public static bool TryParsePersistedPane(string? name, out GlobalPane pane)
    {
        if (string.Equals(name, LegacyTasksPaneName, StringComparison.Ordinal))
        {
            pane = GlobalPane.Tasks;
            return true;
        }

        return Enum.TryParse(name, out pane) && IsKnownPane(pane);
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

    /// <summary>Opening a pane always succeeds, because it makes its own room: a
    /// switch closes what the reader did not pin. Only an unavailable pane refuses.</summary>
    public bool CanEnable(GlobalPane pane) => IsKnownPane(pane) && _available.Contains(pane);

    public bool IsPinned(GlobalPane pane) => IsKnownPane(pane) && _pinned.Contains(pane);

    /// <summary>A pin keeps an open pane through a switch, so there has to be one on
    /// screen to keep — and room for a second pane for it to be kept beside.</summary>
    public bool CanPin(GlobalPane pane) => IsEnabled(pane) && _capacity > 1;

    public bool TrySetPinned(GlobalPane pane, bool pinned)
    {
        if (!IsKnownPane(pane))
        {
            return false;
        }

        // Unpinning is always allowed and never closes anything: it withdraws a
        // promise about the next switch rather than acting on this one.
        if (!pinned)
        {
            return _pinned.Remove(pane);
        }

        return CanPin(pane) && _pinned.Add(pane);
    }

    public bool TogglePin(GlobalPane pane) => TrySetPinned(pane, !IsPinned(pane));

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
            // Exclusivity belongs to the transition, not to the press: asking for the
            // pane already on screen is not asking to be left alone with it.
            if (_enabled.Contains(pane))
            {
                return false;
            }

            SwitchTo(pane);
            return true;
        }

        if (!_enabled.Contains(pane) || EnabledAvailableCount == 1)
        {
            return false;
        }

        _enabled.Remove(pane);
        _pinned.Remove(pane);
        return true;
    }

    /// <summary>Opens a pane beside the ones already on screen. Where the viewport has
    /// no room it falls back to a switch, because a request the shell made on the
    /// reader's behalf must never be silently dropped.</summary>
    public bool TryOpenAlongside(GlobalPane pane)
    {
        if (!IsKnownPane(pane) || !_available.Contains(pane) || _enabled.Contains(pane))
        {
            return false;
        }

        if (EnabledAvailableCount < _capacity)
        {
            return _enabled.Add(pane);
        }

        SwitchTo(pane);
        return true;
    }

    /// <summary>Opens <paramref name="pane"/> as the pane the reader asked for: every
    /// open pane they did not pin makes way for it.</summary>
    private void SwitchTo(GlobalPane pane)
    {
        _enabled.RemoveWhere(open => open != pane && !_pinned.Contains(open));

        // A pin is a preference and the pane just asked for is a request, so where
        // the viewport cannot hold both the request wins and the oldest pin goes.
        foreach (var survivor in KnownPaneOrder)
        {
            if (EnabledAvailableCount + 1 <= _capacity)
            {
                break;
            }

            if (survivor == pane || !_enabled.Contains(survivor))
            {
                continue;
            }

            _enabled.Remove(survivor);
            _pinned.Remove(survivor);
        }

        _enabled.Add(pane);
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
            _available.Add(GlobalPane.Tasks);
        }

        _enabled.IntersectWith(_available);

        if (_enabled.Count == 0)
        {
            _enabled.Add(DefaultPane());
        }

        // A pin only ever describes an open pane, so whatever just left the selection
        // — an unavailable pane included — leaves its pin behind.
        _pinned.IntersectWith(_enabled);
    }

    /// <summary>Drops panes until the viewport holds what is left, taking the ones the
    /// reader did not pin first and unpinning whatever it has to take after that.</summary>
    private void TrimToCapacity()
    {
        while (EnabledAvailableCount > _capacity)
        {
            if ((FirstEnabled(pinned: false) ?? FirstEnabled(pinned: true)) is not { } victim)
            {
                break;
            }

            if (!_enabled.Remove(victim))
            {
                break;
            }

            _pinned.Remove(victim);
        }

        GlobalPane? FirstEnabled(bool pinned) => KnownPaneOrder
            .Where(p => _enabled.Contains(p) && _pinned.Contains(p) == pinned)
            .Cast<GlobalPane?>()
            .FirstOrDefault();
    }

    private GlobalPane DefaultPane()
    {
        if (_available.Contains(GlobalPane.Tasks))
        {
            return GlobalPane.Tasks;
        }

        return KnownPaneOrder.First(_available.Contains);
    }

    private static bool IsKnownPane(GlobalPane pane) => KnownPanes.Contains(pane);
}
