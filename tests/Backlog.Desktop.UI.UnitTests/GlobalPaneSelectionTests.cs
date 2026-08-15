
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

    [Fact]
    public void Supports_one_two_or_all_three_global_panes_when_capacity_allows_it()
    {
        var selection = new GlobalPaneSelection();

        selection.Toggle(GlobalPane.Inbox);
        Assert.True(selection.IsEnabled(GlobalPane.Backlog));
        Assert.True(selection.IsEnabled(GlobalPane.Inbox));
        Assert.False(selection.IsEnabled(GlobalPane.Knowledge));

        selection.Toggle(GlobalPane.Knowledge);
        Assert.True(selection.IsEnabled(GlobalPane.Backlog));
        Assert.True(selection.IsEnabled(GlobalPane.Inbox));
        Assert.True(selection.IsEnabled(GlobalPane.Knowledge));

        selection.Toggle(GlobalPane.Backlog);
        Assert.False(selection.IsEnabled(GlobalPane.Backlog));
        Assert.True(selection.IsEnabled(GlobalPane.Inbox));
        Assert.True(selection.IsEnabled(GlobalPane.Knowledge));

        selection.Toggle(GlobalPane.Inbox);
        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.True(selection.IsEnabled(GlobalPane.Knowledge));
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

    [Fact]
    public void Two_pane_capacity_refuses_to_open_a_third_pane_until_one_closes()
    {
        var selection = new GlobalPaneSelection();
        selection.TrySetCapacity(2);
        selection.Toggle(GlobalPane.Inbox);

        Assert.False(selection.CanEnable(GlobalPane.Knowledge));
        Assert.False(selection.TrySetEnabled(GlobalPane.Knowledge, enabled: true));
        Assert.False(selection.IsEnabled(GlobalPane.Knowledge));
        Assert.Equal(2, selection.EnabledCount);
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
}
