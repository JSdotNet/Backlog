
namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The side panes beside the shell's main view. The task list used to be a pane in
/// this selection and is a view now, so the selection may be empty, and the viewport
/// capacity counts the view as one of the panes it fits.
/// </summary>
public sealed class GlobalPaneSelectionTests
{
    /// <summary>
    /// A layout saved before the Knowledge context was renamed to Devbook still
    /// restores: the shell persists its open panes as enum member names, so the
    /// name a pane was written under is a stored value.
    /// </summary>
    [Theory]
    [InlineData("Knowledge", "Devbook")]
    [InlineData("Inbox", "Inbox")]
    [InlineData("Devbook", "Devbook")]
    public void A_persisted_pane_name_is_read_including_the_name_it_was_saved_under_before_the_rename(
        string persisted, string expected)
    {
        Assert.True(GlobalPaneSelection.TryParsePersistedPane(persisted, out var pane));
        Assert.Equal(expected, pane.ToString());
    }

    /// <summary>
    /// "Tasks" — and "Backlog", its name before that — is what every file written
    /// before the view switch lists among its panes. The task list is a view now,
    /// so the entry names no pane and is ignored; the panes beside it keep their
    /// state.
    /// </summary>
    [Theory]
    [InlineData("Tasks")]
    [InlineData("Backlog")]
    [InlineData("")]
    [InlineData("Roadmap")]
    [InlineData("knowledge")]
    [InlineData("1")]
    [InlineData(null)]
    public void A_pane_name_that_names_no_side_pane_is_refused(string? persisted)
    {
        Assert.False(GlobalPaneSelection.TryParsePersistedPane(persisted, out _));
    }

    [Fact]
    public void Starts_with_no_side_pane_open()
    {
        var selection = new GlobalPaneSelection();

        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.False(selection.IsEnabled(GlobalPane.Devbook));
        Assert.Equal(0, selection.EnabledCount);
        Assert.Equal(3, selection.Capacity);
        Assert.Equal(2, selection.SideSlots);
    }

    [Fact]
    public void A_toggle_opens_a_pane_beside_the_view_and_closes_it_again()
    {
        var selection = new GlobalPaneSelection();

        Assert.True(selection.Toggle(GlobalPane.Inbox));
        Assert.True(selection.IsEnabled(GlobalPane.Inbox));

        Assert.True(selection.Toggle(GlobalPane.Devbook));
        Assert.Equal([GlobalPane.Inbox, GlobalPane.Devbook], selection.Enabled);

        Assert.True(selection.Toggle(GlobalPane.Inbox));
        Assert.Equal([GlobalPane.Devbook], selection.Enabled);

        // The last side pane closes too: the main view is still on screen.
        Assert.True(selection.Toggle(GlobalPane.Devbook));
        Assert.Empty(selection.Enabled);
    }

    /// <summary>Two panes' width fits the view and one side pane, so opening the
    /// second side pane closes the first.</summary>
    [Fact]
    public void A_two_pane_window_holds_one_side_pane_and_the_new_one_wins()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox);
        selection.TrySetCapacity(2);

        Assert.Equal(1, selection.SideSlots);
        Assert.True(selection.TryOpen(GlobalPane.Devbook));

        Assert.Equal([GlobalPane.Devbook], selection.Enabled);
    }

    /// <summary>A window that fits one pane still opens a side pane: the layout
    /// stacks its columns there, and a toggle that could never open would be a dead
    /// button.</summary>
    [Fact]
    public void A_one_pane_window_still_opens_one_side_pane()
    {
        var selection = new GlobalPaneSelection();
        selection.TrySetCapacity(1);

        Assert.Equal(1, selection.SideSlots);
        Assert.True(selection.TryOpen(GlobalPane.Inbox));
        Assert.True(selection.TryOpen(GlobalPane.Devbook));

        Assert.Equal([GlobalPane.Devbook], selection.Enabled);
    }

    [Fact]
    public void Narrowing_the_window_trims_the_first_open_pane_in_the_stable_order()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Devbook);

        Assert.True(selection.TrySetCapacity(2));

        Assert.Equal([GlobalPane.Devbook], selection.Enabled);
    }

    [Fact]
    public void Setting_the_same_capacity_again_changes_nothing()
    {
        var selection = new GlobalPaneSelection();

        Assert.False(selection.TrySetCapacity(3));
        Assert.False(selection.TrySetCapacity(7));
    }

    [Fact]
    public void Unavailable_panes_cannot_be_opened_and_close_when_they_go()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Devbook);

        Assert.True(selection.TrySetAvailable(GlobalPane.Devbook, available: false));
        Assert.Equal([GlobalPane.Inbox], selection.Enabled);
        Assert.False(selection.TryOpen(GlobalPane.Devbook));
        Assert.False(selection.Toggle(GlobalPane.Devbook));

        Assert.True(selection.TrySetAvailable(GlobalPane.Devbook, available: true));
        Assert.True(selection.TryOpen(GlobalPane.Devbook));
    }

    [Fact]
    public void Opening_an_open_pane_or_closing_a_closed_one_changes_nothing()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox);

        Assert.False(selection.TryOpen(GlobalPane.Inbox));
        Assert.False(selection.TryClose(GlobalPane.Devbook));
        Assert.Equal([GlobalPane.Inbox], selection.Enabled);
    }

    [Fact]
    public void The_constructor_ignores_unknown_panes_and_duplicates()
    {
        var selection = new GlobalPaneSelection((GlobalPane)99, GlobalPane.Devbook, GlobalPane.Devbook);

        Assert.Equal([GlobalPane.Devbook], selection.Enabled);
        Assert.False(selection.TryOpen((GlobalPane)99));
        Assert.False(selection.IsEnabled((GlobalPane)99));
    }
}
