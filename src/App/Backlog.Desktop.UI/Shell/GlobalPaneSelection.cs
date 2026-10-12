using System.Collections.ObjectModel;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The side panes the shell can open beside its main view. The task list used to be
/// a third member; it is a <see cref="ShellView"/> now, the one the view switch
/// shows when nothing else is chosen, so it is no longer something that can be
/// closed or opened beside itself.
/// </summary>
internal enum GlobalPane
{
    Inbox,
    Devbook
}

/// <summary>
/// Tracks which side panes are open beside the shell's main view.
/// <para>
/// The main view — Tasks or Roadmap, see <see cref="ShellView"/> — is always on
/// screen, so this selection may be empty: no side pane at all is the ordinary
/// state, where the pane strip it replaced always kept one pane open so the shell
/// would never render empty. Each side pane is a plain toggle that opens beside the
/// view; there is no "switch to" any more, because switching away from the main
/// view is the view switch's question and not a pane's.
/// </para>
/// <para>
/// The viewport decides how many panes fit, the main view included, so the side
/// panes get the slots it leaves — but never fewer than one: below the two-pane
/// width the layout stacks its columns instead of squeezing them, and a window that
/// could open no side pane at all would turn the Inbox and Devbook toggles into
/// dead buttons. Where a pane opens into a full set, the first open one in the
/// stable order makes way.
/// </para>
/// </summary>
internal sealed class GlobalPaneSelection
{
    private static readonly GlobalPane[] KnownPaneOrder = [GlobalPane.Inbox, GlobalPane.Devbook];
    private static readonly HashSet<GlobalPane> KnownPanes = [.. KnownPaneOrder];

    /// <summary>The panes the viewport fits at most, the main view included.</summary>
    private const int MaxCapacity = 3;

    /// <summary>The name <see cref="GlobalPane.Devbook"/> was stored under before the
    /// Knowledge bounded context was renamed to Devbook.</summary>
    private const string LegacyDevbookPaneName = "Knowledge";

    private readonly HashSet<GlobalPane> _enabled;
    private readonly HashSet<GlobalPane> _available = [.. KnownPanes];
    private int _capacity = MaxCapacity;

    public GlobalPaneSelection()
        : this([])
    {
    }

    public GlobalPaneSelection(params GlobalPane[] enabled)
    {
        _enabled = [.. enabled.Where(IsKnownPane).Distinct()];
        TrimToSlots(keep: null);
    }

    /// <summary>
    /// Reads a persisted pane name, accepting the name a pane was stored under
    /// before its context was renamed.
    /// <para>
    /// The shell writes its open panes to <c>shell-navigation.json</c> as
    /// <c>ToString()</c> values, so a member name here is a stored value and not only
    /// an identifier: "Knowledge" still reads as Devbook. "Tasks", and "Backlog"
    /// before it, read as nothing — the task list is a view now, and a file from
    /// before the view switch listing it among the panes simply had the Tasks view
    /// showing, which is what it reopens on. The Inbox and Devbook beside it keep
    /// their state.
    /// </para>
    /// </summary>
    public static bool TryParsePersistedPane(string? name, out GlobalPane pane)
    {
        if (string.Equals(name, LegacyDevbookPaneName, StringComparison.Ordinal))
        {
            pane = GlobalPane.Devbook;
            return true;
        }

        // Enum.TryParse also takes a number, which names no pane a file ever held.
        if (name is null || name.Length == 0 || !char.IsLetter(name[0]))
        {
            pane = default;
            return false;
        }

        return Enum.TryParse(name, out pane) && IsKnownPane(pane);
    }

    /// <summary>The panes the viewport fits, the main view included.</summary>
    public int Capacity => _capacity;

    /// <summary>How many side panes fit beside the main view: what the viewport
    /// leaves after it, and at least one.</summary>
    public int SideSlots => Math.Max(1, _capacity - 1);

    public int EnabledCount => _enabled.Count;

    public IReadOnlyCollection<GlobalPane> Enabled =>
        new ReadOnlyCollection<GlobalPane>([.. KnownPaneOrder.Where(IsEnabled)]);

    public bool IsAvailable(GlobalPane pane) => IsKnownPane(pane) && _available.Contains(pane);

    public bool IsEnabled(GlobalPane pane) => IsKnownPane(pane) && _available.Contains(pane) && _enabled.Contains(pane);

    public bool TrySetCapacity(int capacity)
    {
        var clamped = Math.Clamp(capacity, 1, MaxCapacity);
        if (clamped == _capacity)
        {
            return false;
        }

        _capacity = clamped;
        TrimToSlots(keep: null);
        return true;
    }

    /// <summary>Opens a pane beside the main view. Where the side slots are full the
    /// first open pane in the stable order makes way: the pane asked for is the one
    /// the reader wants on screen. Refused for a pane that is unavailable or already
    /// open.</summary>
    public bool TryOpen(GlobalPane pane)
    {
        if (!IsAvailable(pane) || _enabled.Contains(pane))
        {
            return false;
        }

        _enabled.Add(pane);
        TrimToSlots(keep: pane);
        return true;
    }

    /// <summary>Closes a pane. Always allowed: the main view stays on screen.</summary>
    public bool TryClose(GlobalPane pane) => IsKnownPane(pane) && _enabled.Remove(pane);

    /// <summary>A press on the pane's toggle: open it beside the view, or close it.</summary>
    public bool Toggle(GlobalPane pane) => IsEnabled(pane) ? TryClose(pane) : TryOpen(pane);

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

        TrimToSlots(keep: null);
        return true;
    }

    /// <summary>Drops open panes, first in the stable order first, until the side
    /// slots hold what is left — never <paramref name="keep"/>, the pane just asked
    /// for.</summary>
    private void TrimToSlots(GlobalPane? keep)
    {
        foreach (var open in KnownPaneOrder)
        {
            if (_enabled.Count(_available.Contains) <= SideSlots)
            {
                break;
            }

            if (open != keep && _available.Contains(open))
            {
                _enabled.Remove(open);
            }
        }
    }

    private static bool IsKnownPane(GlobalPane pane) => KnownPanes.Contains(pane);
}
