using Backlog.Desktop.UI.Services;

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
    }

    [Fact]
    public void Supports_one_two_or_all_three_global_panes()
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
}
