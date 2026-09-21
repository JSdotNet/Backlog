
namespace Backlog.Desktop.UI.UnitTests;

public sealed class GlobalPaneSelectionTests
{
    /// <summary>
    /// A layout saved before a bounded context was renamed still restores: Backlog
    /// became Tasks, and Knowledge became Devbook.
    /// <para>
    /// The shell persists its open panes to shell-navigation.json as enum
    /// member names, so <c>GlobalPane.Tasks</c> was written as "Backlog" and
    /// <c>GlobalPane.Devbook</c> as "Knowledge" by every build before the respective
    /// rename. A plain <c>Enum.TryParse</c> rejects those, and the pane is then
    /// filtered out on restore — the reader opens the app and finds the arrangement
    /// they left it in quietly gone, which reads as the app forgetting rather than
    /// as a rename.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Backlog", "Tasks")]
    [InlineData("Knowledge", "Devbook")]
    [InlineData("Tasks", "Tasks")]
    [InlineData("Inbox", "Inbox")]
    [InlineData("Devbook", "Devbook")]
    public void A_persisted_pane_name_is_read_including_the_name_it_was_saved_under_before_the_rename(
        string persisted, string expected)
    {
        Assert.True(GlobalPaneSelection.TryParsePersistedPane(persisted, out var pane));
        Assert.Equal(expected, pane.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Roadmap")]
    [InlineData("backlog")]
    [InlineData("knowledge")]
    [InlineData(null)]
    public void A_pane_name_that_names_no_pane_is_refused(string? persisted)
    {
        Assert.False(GlobalPaneSelection.TryParsePersistedPane(persisted, out _));
    }

    [Fact]
    public void Starts_with_tasks_visible_only()
    {
        var selection = new GlobalPaneSelection();

        Assert.True(selection.IsEnabled(GlobalPane.Tasks));
        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.False(selection.IsEnabled(GlobalPane.Devbook));
        Assert.Equal(1, selection.EnabledCount);
        Assert.Equal(3, selection.Capacity);
    }

    /// <summary>
    /// Asking for a section is asking to look at it, not asking for one more thing on
    /// screen. So a pane the reader switches to takes the screen from the others,
    /// however much room the window has — one more is <see cref="GlobalPaneSelection.TryOpenBeside"/>,
    /// which the reader asks for with the modifier.
    /// </summary>
    [Fact]
    public void Switching_to_a_pane_closes_the_panes_it_replaces()
    {
        var selection = new GlobalPaneSelection();

        selection.Toggle(GlobalPane.Inbox);
        Assert.True(selection.IsEnabled(GlobalPane.Inbox));
        Assert.False(selection.IsEnabled(GlobalPane.Tasks));
        Assert.False(selection.IsEnabled(GlobalPane.Devbook));
        Assert.Equal(1, selection.EnabledCount);

        selection.Toggle(GlobalPane.Devbook);
        Assert.True(selection.IsEnabled(GlobalPane.Devbook));
        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.False(selection.IsEnabled(GlobalPane.Tasks));
        Assert.Equal(1, selection.EnabledCount);

        selection.Toggle(GlobalPane.Tasks);
        Assert.True(selection.IsEnabled(GlobalPane.Tasks));
        Assert.False(selection.IsEnabled(GlobalPane.Devbook));
        Assert.Equal(1, selection.EnabledCount);
    }

    [Fact]
    public void Single_pane_capacity_switches_to_the_requested_pane()
    {
        var selection = new GlobalPaneSelection();
        selection.TrySetCapacity(1);

        var changed = selection.Toggle(GlobalPane.Devbook);

        Assert.True(changed);
        Assert.False(selection.IsEnabled(GlobalPane.Tasks));
        Assert.True(selection.IsEnabled(GlobalPane.Devbook));
        Assert.Equal(1, selection.EnabledCount);
        Assert.True(selection.CanEnable(GlobalPane.Inbox));
    }

    /// <summary>
    /// A full viewport is no longer a refusal. The pane asked for makes its own room
    /// by closing the rest, so opening one never fails for width.
    /// </summary>
    [Fact]
    public void Two_pane_capacity_makes_room_by_closing_the_other_panes()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Tasks);
        selection.TrySetCapacity(2);

        Assert.True(selection.CanEnable(GlobalPane.Devbook));
        Assert.True(selection.TrySetEnabled(GlobalPane.Devbook, enabled: true));

        Assert.True(selection.IsEnabled(GlobalPane.Devbook));
        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.False(selection.IsEnabled(GlobalPane.Tasks));
        Assert.Equal(1, selection.EnabledCount);
    }

    [Fact]
    public void Reducing_capacity_trims_enabled_panes_in_stable_priority_order()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Tasks, GlobalPane.Devbook);

        var changed = selection.TrySetCapacity(2);

        Assert.True(changed);
        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.True(selection.IsEnabled(GlobalPane.Tasks));
        Assert.True(selection.IsEnabled(GlobalPane.Devbook));
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

        Assert.True(selection.IsEnabled(GlobalPane.Tasks));
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
        Assert.True(selection.IsEnabled(GlobalPane.Tasks));
        Assert.Equal(1, selection.EnabledCount);
    }

    /// <summary>
    /// "This one too": the modifier press opens a pane beside the open ones instead
    /// of in their place. Inbox and Tasks are both open here so the act is shown
    /// keeping more than one.
    /// </summary>
    [Fact]
    public void Opening_beside_keeps_the_open_panes()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Tasks);

        Assert.True(selection.TryOpenBeside(GlobalPane.Devbook));

        Assert.True(selection.IsEnabled(GlobalPane.Inbox));
        Assert.True(selection.IsEnabled(GlobalPane.Tasks));
        Assert.True(selection.IsEnabled(GlobalPane.Devbook));
        Assert.Equal(3, selection.EnabledCount);
    }

    /// <summary>
    /// The reader asked to keep what is open and for one more, and the viewport
    /// cannot hold all of it. The request wins, and only as much goes as it needs
    /// room for: the first open pane in the stable order, which is the one a trim
    /// would take too.
    /// </summary>
    [Fact]
    public void Opening_beside_a_full_viewport_evicts_only_the_first_open_pane()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Tasks);
        selection.TrySetCapacity(2);

        Assert.True(selection.TryOpenBeside(GlobalPane.Devbook));

        Assert.True(selection.IsEnabled(GlobalPane.Devbook));
        Assert.True(selection.IsEnabled(GlobalPane.Tasks));
        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.Equal(2, selection.EnabledCount);
    }

    /// <summary>
    /// Beside means nothing in a window that fits one pane, so the shell names no
    /// modifier there; asked anyway, the act degrades to the switch the plain press
    /// would have made rather than refusing.
    /// </summary>
    [Fact]
    public void A_single_pane_window_takes_no_second_pane()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Tasks);
        selection.TrySetCapacity(1);

        Assert.False(selection.TakesSeveral);
        Assert.True(selection.TryOpenBeside(GlobalPane.Devbook));

        Assert.True(selection.IsEnabled(GlobalPane.Devbook));
        Assert.False(selection.IsEnabled(GlobalPane.Tasks));
        Assert.Equal(1, selection.EnabledCount);

        selection.TrySetCapacity(3);
        Assert.True(selection.TakesSeveral);
    }

    [Fact]
    public void Opening_beside_refuses_an_unavailable_or_already_open_pane()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Tasks);
        selection.TrySetAvailable(GlobalPane.Inbox, available: false);

        Assert.False(selection.TryOpenBeside(GlobalPane.Inbox));
        Assert.False(selection.TryOpenBeside(GlobalPane.Tasks));
        Assert.Equal(1, selection.EnabledCount);
    }

    /// <summary>
    /// Exclusivity is a property of the transition, not of the press. Asking for the
    /// pane you are already reading is not asking to be left alone with it.
    /// </summary>
    [Fact]
    public void Re_enabling_an_open_pane_changes_nothing()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Tasks);

        Assert.False(selection.TrySetEnabled(GlobalPane.Tasks, enabled: true));

        Assert.True(selection.IsEnabled(GlobalPane.Inbox));
        Assert.True(selection.IsEnabled(GlobalPane.Tasks));
        Assert.Equal(2, selection.EnabledCount);
    }

    [Fact]
    public void Trimming_takes_the_first_open_pane_in_the_stable_order()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Tasks, GlobalPane.Devbook);

        Assert.True(selection.TrySetCapacity(2));

        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.True(selection.IsEnabled(GlobalPane.Tasks));
        Assert.True(selection.IsEnabled(GlobalPane.Devbook));
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
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Tasks, GlobalPane.Devbook);

        foreach (var capacity in new[] { 1, 2, 3 })
        {
            selection.TrySetCapacity(capacity);

            foreach (var pane in new[] { GlobalPane.Devbook, GlobalPane.Inbox, GlobalPane.Tasks })
            {
                selection.TrySetEnabled(pane, enabled: true);

                Assert.True(selection.IsEnabled(pane));
                Assert.InRange(selection.EnabledCount, 1, capacity);
            }
        }
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

        Assert.True(selection.TryOpenAlongside(GlobalPane.Tasks));

        Assert.True(selection.IsEnabled(GlobalPane.Inbox));
        Assert.True(selection.IsEnabled(GlobalPane.Tasks));
        Assert.Equal(2, selection.EnabledCount);

        // Already there: nothing to open and nothing to close.
        Assert.False(selection.TryOpenAlongside(GlobalPane.Tasks));
        Assert.Equal(2, selection.EnabledCount);
    }

    /// <summary>
    /// A request the shell made for the reader may not be silently dropped, so where
    /// there is no room beside the open panes it takes the room instead.
    /// </summary>
    [Fact]
    public void Opening_alongside_falls_back_to_a_switch_when_the_viewport_is_full()
    {
        var selection = new GlobalPaneSelection(GlobalPane.Inbox, GlobalPane.Devbook);
        selection.TrySetCapacity(2);

        Assert.True(selection.TryOpenAlongside(GlobalPane.Tasks));

        Assert.True(selection.IsEnabled(GlobalPane.Tasks));
        Assert.False(selection.IsEnabled(GlobalPane.Inbox));
        Assert.False(selection.IsEnabled(GlobalPane.Devbook));
        Assert.Equal(1, selection.EnabledCount);
    }
}
