using System.Collections.ObjectModel;

namespace Backlog.Desktop.UI.Shell;

internal enum GlobalPane
{
    Inbox,
    Tasks,
    Devbook
}

/// <summary>
/// Tracks which of the shell's global panes are visible right now.
/// At least one available pane always stays on-screen so the shell never renders empty.
/// The viewport decides how many panes may be shown at the same time.
/// <para>
/// Switching to a pane is exclusive: it closes every other pane. Opening a pane
/// beside the ones already on screen is a separate act — <see cref="TryOpenBeside"/>,
/// the modifier-click convention the repository scope in the header already follows —
/// and where the viewport has no room for one more, the pane asked for wins and the
/// first open pane in the stable order makes way. It used to be a pin on each pane
/// that held it through a switch; the pin was a second control on every option for
/// what one modifier on the option itself now says.
/// </para>
/// </summary>
internal sealed class GlobalPaneSelection
{
    private static readonly GlobalPane[] KnownPaneOrder = [GlobalPane.Inbox, GlobalPane.Tasks, GlobalPane.Devbook];
    private static readonly HashSet<GlobalPane> KnownPanes = [.. KnownPaneOrder];

    /// <summary>The name <see cref="GlobalPane.Tasks"/> was stored under before the
    /// Backlog bounded context was renamed to Tasks.</summary>
    private const string LegacyTasksPaneName = "Backlog";

    /// <summary>The name <see cref="GlobalPane.Devbook"/> was stored under before the
    /// Knowledge bounded context was renamed to Devbook.</summary>
    private const string LegacyDevbookPaneName = "Knowledge";

    private readonly HashSet<GlobalPane> _enabled;
    private readonly HashSet<GlobalPane> _available = [.. KnownPanes];
    private int _capacity = KnownPaneOrder.Length;

    public GlobalPaneSelection()
        : this(GlobalPane.Tasks)
    {
    }

    /// <summary>
    /// Reads a persisted pane name, accepting the name a pane was stored under
    /// before its context was renamed.
    /// <para>
    /// The shell writes its open and pinned panes to <c>shell-navigation.json</c> as
    /// <c>ToString()</c> values, so a member name here is a stored value and not only
    /// an identifier. A plain <see cref="Enum.TryParse{TEnum}(string, out TEnum)"/>
    /// returns <see langword="false"/> for a layout saved as "Backlog" or "Knowledge",
    /// and the pane would be dropped on restore — the reader would lose the arrangement they left
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

        if (string.Equals(name, LegacyDevbookPaneName, StringComparison.Ordinal))
        {
            pane = GlobalPane.Devbook;
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
    /// switch closes the rest. Only an unavailable pane refuses.</summary>
    public bool CanEnable(GlobalPane pane) => IsKnownPane(pane) && _available.Contains(pane);

    /// <summary>Whether "beside" can mean anything: a window that fits one pane at a
    /// time has nowhere to put a second, so a request to open beside is a switch
    /// there and the header names no modifier.</summary>
    public bool TakesSeveral => _capacity > 1;

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

    /// <summary>Opens a pane beside the ones already on screen, as the reader asked
    /// with the modifier held. Unlike <see cref="TryOpenAlongside"/> a full viewport
    /// does not turn this into a switch: the reader asked to keep what is open, so
    /// only as many panes go as the one more needs room for — the first open ones in
    /// the stable order, which is the order <see cref="TrimToCapacity"/> takes them in.</summary>
    public bool TryOpenBeside(GlobalPane pane)
    {
        if (!IsKnownPane(pane) || !_available.Contains(pane) || _enabled.Contains(pane))
        {
            return false;
        }

        _enabled.Add(pane);

        foreach (var open in KnownPaneOrder)
        {
            if (EnabledAvailableCount <= _capacity)
            {
                break;
            }

            if (open != pane)
            {
                _enabled.Remove(open);
            }
        }

        return true;
    }

    /// <summary>Opens <paramref name="pane"/> as the pane the reader asked for: every
    /// other open pane makes way for it.</summary>
    private void SwitchTo(GlobalPane pane)
    {
        _enabled.RemoveWhere(open => open != pane);
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
    }

    /// <summary>Drops panes until the viewport holds what is left, first in the stable
    /// order first.</summary>
    private void TrimToCapacity()
    {
        while (EnabledAvailableCount > _capacity)
        {
            if (KnownPaneOrder.Where(_enabled.Contains).Cast<GlobalPane?>().FirstOrDefault() is not { } victim)
            {
                break;
            }

            if (!_enabled.Remove(victim))
            {
                break;
            }
        }
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
