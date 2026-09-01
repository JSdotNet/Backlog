
namespace Backlog.Desktop.UI.UnitTests;

public sealed class GlobalPaneSelectionTests
{
    [Fact]
    public void Starts_with_backlog_visible_only()
    {
        var selection = new GlobalPaneSelection();

        Assert.True(selection.IsEnabled(GlobalPane.Backlog));
        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.False(selection.IsEnabled(GlobalPane.Knowledge));
        Assert.Equal(1, selection.EnabledCount);
        Assert.Equal(3, selection.Capacity);
    }

    /// <summary>
    /// Asking for a section is asking to look at it, not asking for one more thing on
    /// screen. So a pane the reader switches to takes the screen from the panes they
    /// did not ask to keep, however much room the window has.
    /// </summary>
    [Fact]
    public void Switching_to_a_pane_closes_the_unpinned_panes_it_replaces()
    {
        var selection = new GlobalPaneSelection();

        selection.Toggle(GlobalPane.Inbox);
        Assert.True(selection.IsEnabled(GlobalPane.Inbox));
        Assert.False(selection.IsEnabled(GlobalPane.Backlog));
        Assert.False(selection.IsEnabled(GlobalPane.Knowledge));
        Assert.Equal(1, selection.EnabledCount);

        selection.Toggle(GlobalPane.Knowledge);
        Assert.True(selection.IsEnabled(GlobalPane.Knowledge));
        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.False(selection.IsEnabled(GlobalPane.Backlog));
        Assert.Equal(1, selection.EnabledCount);

        selection.Toggle(GlobalPane.Backlog);
        Assert.True(selection.IsEnabled(GlobalPane.Backlog));
        Assert.False(selection.IsEnabled(GlobalPane.Knowledge));
        Assert.Equal(1, selection.EnabledCount);
    }

    [Fact]
    public void Single_pane_capacity_switches_to_the_requested_pane()
    {
        var selection = new GlobalPaneSelection();
        selection.TrySetCapacity(1);

        var changed = selection.Toggle(GlobalPane.Knowledge);

        Assert.True(changed);
        Assert.False(selection.IsEnabled(GlobalPane.Backlog));
        Assert.True(selection.IsEnabled(GlobalPane.Knowledge));
        Assert.Equal(1, selection.EnabledCount);
        Assert.True(selection.CanEnable(GlobalPane.Inbox));
    }

    /// <summary>
    /// A full viewport is no longer a refusal. The pane asked for makes its own room
    /// by closing what the reader did not pin, so opening one never fails for width.
    /// </summary>
    [Fact]
    public void Two_pane_capacity_makes_room_by_closing_the_unpinned_panes()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Backlog);
        selection.TrySetCapacity(2);

        Assert.True(selection.CanEnable(GlobalPane.Knowledge));
        Assert.True(selection.TrySetEnabled(GlobalPane.Knowledge, enabled: true));

        Assert.True(selection.IsEnabled(GlobalPane.Knowledge));
        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.False(selection.IsEnabled(GlobalPane.Backlog));
        Assert.Equal(1, selection.EnabledCount);
    }

    [Fact]
    public void Reducing_capacity_trims_enabled_panes_in_stable_priority_order()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Backlog, GlobalPane.Knowledge);

        var changed = selection.TrySetCapacity(2);

        Assert.True(changed);
        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.True(selection.IsEnabled(GlobalPane.Backlog));
        Assert.True(selection.IsEnabled(GlobalPane.Knowledge));
        Assert.Equal(2, selection.EnabledCount);
    }

    [Fact]
    public void Refuses_to_hide_the_last_remaining_pane()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox);

        var changed = selection.TrySetEnabled(GlobalPane.Inbox, false);

        Assert.False(changed);
        Assert.True(selection.IsEnabled(GlobalPane.Inbox));
        Assert.Equal(1, selection.EnabledCount);
    }

    [Fact]
    public void Constructor_ignores_unknown_panes_and_keeps_a_known_default()
    {
        var selection = new GlobalPaneSelection((GlobalPane)999);

        Assert.True(selection.IsEnabled(GlobalPane.Backlog));
        Assert.False(selection.IsEnabled((GlobalPane)999));
        Assert.Equal(1, selection.EnabledCount);
    }

    [Fact]
    public void Unknown_panes_cannot_break_selection_invariant()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox);

        var changed = selection.Toggle((GlobalPane)999);

        Assert.False(changed);
        Assert.True(selection.IsEnabled(GlobalPane.Inbox));
        Assert.Equal(1, selection.EnabledCount);
    }

    [Fact]
    public void Unavailable_panes_cannot_be_enabled_or_toggled()
    {
        var selection = new GlobalPaneSelection();
        selection.TrySetAvailable(GlobalPane.Inbox, available: false);

        Assert.False(selection.TrySetEnabled(GlobalPane.Inbox, enabled: true));
        Assert.False(selection.Toggle(GlobalPane.Inbox));
        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.Equal(1, selection.EnabledCount);
    }

    [Fact]
    public void Removing_the_last_selected_available_pane_falls_back_to_backlog()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox);

        var changed = selection.TrySetAvailable(GlobalPane.Inbox, available: false);

        Assert.True(changed);
        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.True(selection.IsEnabled(GlobalPane.Backlog));
        Assert.Equal(1, selection.EnabledCount);
    }

    /// <summary>
    /// The pin is the exception to the switch: it says "keep this one, whatever I
    /// look at next". Inbox is open and unpinned here so the same act is shown doing
    /// both jobs — the pinned pane stays, the other one goes.
    /// </summary>
    [Fact]
    public void A_pinned_pane_survives_a_switch_away_from_it()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Backlog);

        Assert.True(selection.TrySetPinned(GlobalPane.Backlog, pinned: true));
        Assert.True(selection.TrySetEnabled(GlobalPane.Knowledge, enabled: true));

        Assert.True(selection.IsEnabled(GlobalPane.Backlog));
        Assert.True(selection.IsPinned(GlobalPane.Backlog));
        Assert.True(selection.IsEnabled(GlobalPane.Knowledge));
        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.Equal(2, selection.EnabledCount);
    }

    [Fact]
    public void Two_pinned_panes_both_survive_a_switch_when_the_viewport_has_room()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Backlog);

        Assert.True(selection.TrySetPinned(GlobalPane.Inbox, pinned: true));
        Assert.True(selection.TrySetPinned(GlobalPane.Backlog, pinned: true));

        Assert.True(selection.TrySetEnabled(GlobalPane.Knowledge, enabled: true));

        Assert.True(selection.IsEnabled(GlobalPane.Inbox));
        Assert.True(selection.IsEnabled(GlobalPane.Backlog));
        Assert.True(selection.IsEnabled(GlobalPane.Knowledge));
        Assert.Equal(3, selection.EnabledCount);
    }

    /// <summary>
    /// A pin is a preference, and the pane the reader just asked for is a request.
    /// When the two cannot both be honoured the request wins: the oldest pin in the
    /// stable order makes way, and loses its pin with its place.
    /// </summary>
    [Fact]
    public void The_switched_to_pane_wins_when_the_pinned_panes_fill_the_viewport()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Backlog);
        selection.TrySetCapacity(2);

        Assert.True(selection.TrySetPinned(GlobalPane.Inbox, pinned: true));
        Assert.True(selection.TrySetPinned(GlobalPane.Backlog, pinned: true));

        Assert.True(selection.TrySetEnabled(GlobalPane.Knowledge, enabled: true));

        Assert.True(selection.IsEnabled(GlobalPane.Knowledge));
        Assert.True(selection.IsEnabled(GlobalPane.Backlog));
        Assert.True(selection.IsPinned(GlobalPane.Backlog));

        // Evicted, and unpinned with it: a pin only ever describes a pane on screen.
        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.False(selection.IsPinned(GlobalPane.Inbox));
        Assert.Equal(2, selection.EnabledCount);
    }

    /// <summary>
    /// Exclusivity is a property of the transition, not of the press. Asking for the
    /// pane you are already reading is not asking to be left alone with it.
    /// </summary>
    [Fact]
    public void Re_enabling_an_open_pane_changes_nothing()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Backlog);

        Assert.False(selection.TrySetEnabled(GlobalPane.Backlog, enabled: true));

        Assert.True(selection.IsEnabled(GlobalPane.Inbox));
        Assert.True(selection.IsEnabled(GlobalPane.Backlog));
        Assert.Equal(2, selection.EnabledCount);
    }

    [Fact]
    public void Pinning_requires_an_open_pane()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Backlog);

        Assert.False(selection.CanPin(GlobalPane.Knowledge));
        Assert.False(selection.TrySetPinned(GlobalPane.Knowledge, pinned: true));
        Assert.False(selection.IsPinned(GlobalPane.Knowledge));

        Assert.True(selection.CanPin(GlobalPane.Backlog));
    }

    [Fact]
    public void Closing_a_pane_drops_its_pin()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Backlog);
        Assert.True(selection.TrySetPinned(GlobalPane.Inbox, pinned: true));

        Assert.True(selection.TrySetEnabled(GlobalPane.Inbox, enabled: false));

        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.False(selection.IsPinned(GlobalPane.Inbox));
    }

    /// <summary>
    /// Unpinning says "you may close this one when I switch", not "close it now".
    /// </summary>
    [Fact]
    public void Unpinning_leaves_the_pane_on_screen()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Backlog);
        Assert.True(selection.TrySetPinned(GlobalPane.Backlog, pinned: true));

        Assert.True(selection.TrySetPinned(GlobalPane.Backlog, pinned: false));

        Assert.False(selection.IsPinned(GlobalPane.Backlog));
        Assert.True(selection.IsEnabled(GlobalPane.Backlog));
        Assert.Equal(2, selection.EnabledCount);
    }

    /// <summary>
    /// There is nothing for a pin to protect a pane from in a window that holds one
    /// pane: every switch is a takeover, so offering the pin would be offering a
    /// promise the viewport cannot keep.
    /// </summary>
    [Fact]
    public void A_pin_cannot_be_taken_in_a_single_pane_window()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Backlog);
        selection.TrySetCapacity(1);

        Assert.False(selection.CanPin(GlobalPane.Backlog));
        Assert.False(selection.TrySetPinned(GlobalPane.Backlog, pinned: true));
        Assert.False(selection.IsPinned(GlobalPane.Backlog));
    }

    /// <summary>
    /// A pin taken in a wide window is kept through a narrow one rather than thrown
    /// away: the window is a passing condition and the reader's choice is not.
    /// </summary>
    [Fact]
    public void A_pin_survives_a_narrow_window_and_comes_back_with_it()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Backlog);
        Assert.True(selection.TrySetPinned(GlobalPane.Backlog, pinned: true));

        selection.TrySetCapacity(1);

        Assert.True(selection.IsEnabled(GlobalPane.Backlog));
        Assert.True(selection.IsPinned(GlobalPane.Backlog));
        Assert.False(selection.CanPin(GlobalPane.Backlog));
        Assert.Equal(1, selection.EnabledCount);

        selection.TrySetCapacity(3);

        Assert.True(selection.IsPinned(GlobalPane.Backlog));
        Assert.True(selection.CanPin(GlobalPane.Backlog));
    }

    [Fact]
    public void Trimming_prefers_the_unpinned_panes()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Backlog, GlobalPane.Knowledge);
        Assert.True(selection.TrySetPinned(GlobalPane.Inbox, pinned: true));

        Assert.True(selection.TrySetCapacity(2));

        // Backlog leads the unpinned panes in the stable order, so it goes first —
        // where without the pin Inbox would have.
        Assert.True(selection.IsEnabled(GlobalPane.Inbox));
        Assert.False(selection.IsEnabled(GlobalPane.Backlog));
        Assert.True(selection.IsEnabled(GlobalPane.Knowledge));
        Assert.Equal(2, selection.EnabledCount);
    }

    /// <summary>
    /// The always-one-visible invariant outranks exclusivity: a switch closes panes,
    /// but the pane it opens is added in the same act, so there is no arrangement in
    /// which the shell comes out empty.
    /// </summary>
    [Fact]
    public void A_switch_never_leaves_the_shell_empty()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Backlog, GlobalPane.Knowledge);

        foreach (var capacity in new[] { 1, 2, 3 })
        {
            selection.TrySetCapacity(capacity);

            foreach (var pane in new[] { GlobalPane.Knowledge, GlobalPane.Inbox, GlobalPane.Backlog })
            {
                selection.TrySetEnabled(pane, enabled: true);

                Assert.True(selection.IsEnabled(pane));
                Assert.InRange(selection.EnabledCount, 1, capacity);
            }
        }
    }

    [Fact]
    public void A_pane_that_becomes_unavailable_loses_its_pin()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Backlog, GlobalPane.Knowledge);
        Assert.True(selection.TrySetPinned(GlobalPane.Knowledge, pinned: true));

        Assert.True(selection.TrySetAvailable(GlobalPane.Knowledge, available: false));

        Assert.False(selection.IsEnabled(GlobalPane.Knowledge));
        Assert.False(selection.IsPinned(GlobalPane.Knowledge));
    }

    /// <summary>
    /// The shell opens a pane on the reader's behalf when they pick an Inbox item to
    /// work on. That is not the reader switching sections, so it must not close the
    /// Inbox they picked it from.
    /// </summary>
    [Fact]
    public void Opening_a_pane_alongside_keeps_the_panes_already_open()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox);

        Assert.True(selection.TryOpenAlongside(GlobalPane.Backlog));

        Assert.True(selection.IsEnabled(GlobalPane.Inbox));
        Assert.True(selection.IsEnabled(GlobalPane.Backlog));
        Assert.Equal(2, selection.EnabledCount);

        // Already there: nothing to open and nothing to close.
        Assert.False(selection.TryOpenAlongside(GlobalPane.Backlog));
        Assert.Equal(2, selection.EnabledCount);
    }

    /// <summary>
    /// A request the shell made for the reader may not be silently dropped, so where
    /// there is no room beside the open panes it takes the room instead.
    /// </summary>
    [Fact]
    public void Opening_alongside_falls_back_to_a_switch_when_the_viewport_is_full()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Knowledge);
        selection.TrySetCapacity(2);
        Assert.True(selection.TrySetPinned(GlobalPane.Inbox, pinned: true));

        Assert.True(selection.TryOpenAlongside(GlobalPane.Backlog));

        Assert.True(selection.IsEnabled(GlobalPane.Backlog));
        Assert.True(selection.IsEnabled(GlobalPane.Inbox));
        Assert.False(selection.IsEnabled(GlobalPane.Knowledge));
        Assert.Equal(2, selection.EnabledCount);
    }
}
