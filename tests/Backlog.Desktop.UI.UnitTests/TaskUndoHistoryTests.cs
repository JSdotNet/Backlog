namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Tasks pane's undo stacks on their own: what joins one step, what starts a
/// new one, and what a redo survives.
/// </summary>
public sealed class TaskUndoHistoryTests
{
    private static readonly Guid One = Guid.NewGuid();
    private static readonly Guid Two = Guid.NewGuid();

    [Fact]
    public void Saves_of_one_typing_session_join_into_one_step()
    {
        var history = new TaskUndoHistory();

        history.RecordText("typing", One, "a", "ab", open: true);
        history.RecordText("typing", One, "ab", "abc", open: true);
        history.RecordText("typing", One, "abc", "abcd", open: false);

        var step = Assert.IsType<TaskTextUndoStep>(history.TakeUndo());
        Assert.Equal(("a", "abcd"), (step.Before, step.After));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void A_closed_step_is_not_joined_by_the_next_edit()
    {
        var history = new TaskUndoHistory();

        history.RecordText("status", One, "a", "b", open: false);
        history.RecordText("typing", One, "b", "bc", open: true);

        Assert.Equal("bc", Assert.IsType<TaskTextUndoStep>(history.TakeUndo()).After);
        Assert.Equal("b", Assert.IsType<TaskTextUndoStep>(history.TakeUndo()).After);
    }

    [Fact]
    public void Typing_in_another_entry_starts_a_step_of_its_own()
    {
        var history = new TaskUndoHistory();

        history.RecordText("typing", One, "a", "ab", open: true);
        history.RecordText("typing", Two, "x", "xy", open: true);

        Assert.Equal(Two, Assert.IsType<TaskTextUndoStep>(history.TakeUndo()).Id);
        Assert.Equal(One, Assert.IsType<TaskTextUndoStep>(history.TakeUndo()).Id);
    }

    [Fact]
    public void Typing_into_a_draft_is_part_of_creating_it()
    {
        var history = new TaskUndoHistory();

        history.Record(new TaskCreatedUndoStep("adding", One, "# Dra", Open: true));
        history.RecordText("typing", One, "# Dra", "# Draft", open: false);

        var step = Assert.IsType<TaskCreatedUndoStep>(history.TakeUndo());
        Assert.Equal("# Draft", step.Text);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void A_group_is_one_step_and_a_group_of_one_is_that_step()
    {
        var history = new TaskUndoHistory();

        using (history.Group("bulk"))
        {
            history.RecordText("x", One, "a", "b", open: false);
            history.RecordText("x", Two, "c", "d", open: false);
        }

        using (history.Group("single"))
        {
            history.RecordText("x", One, "b", "e", open: false);
        }

        Assert.IsType<TaskTextUndoStep>(history.TakeUndo());
        var group = Assert.IsType<TaskGroupUndoStep>(history.TakeUndo());
        Assert.Equal(2, group.Steps.Count);
    }

    [Fact]
    public void Nested_groups_join_the_outermost()
    {
        var history = new TaskUndoHistory();

        using (history.Group("outer"))
        {
            history.RecordText("x", One, "a", "b", open: false);
            using (history.Group("inner"))
            {
                history.RecordText("x", Two, "c", "d", open: false);
            }
        }

        var group = Assert.IsType<TaskGroupUndoStep>(history.TakeUndo());
        Assert.Equal("outer", group.Label);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void A_new_change_empties_redo_and_a_redo_does_not()
    {
        var history = new TaskUndoHistory();
        history.RecordText("x", One, "a", "b", open: false);
        history.RecordText("x", One, "b", "c", open: false);

        history.Undone(history.TakeUndo()!);
        history.Undone(history.TakeUndo()!);
        history.Redone(history.TakeRedo()!);

        Assert.True(history.CanRedo);

        history.RecordText("x", One, "b", "z", open: false);

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void The_history_keeps_only_the_newest_steps()
    {
        var history = new TaskUndoHistory();

        for (var i = 0; i < TaskUndoHistory.Capacity + 10; i++)
        {
            history.RecordText("x", One, $"{i}", $"{i + 1}", open: false);
        }

        var count = 0;
        while (history.TakeUndo() is not null) count++;

        Assert.Equal(TaskUndoHistory.Capacity, count);
    }

    [Fact]
    public void A_recreated_entry_is_reached_through_every_id_it_has_had()
    {
        var history = new TaskUndoHistory();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        history.Alias(One, second);
        history.Alias(One, third);

        Assert.Equal(third, history.Resolve(One));
        Assert.Equal(third, history.Resolve(second));
        Assert.Equal(Two, history.Resolve(Two));
    }
}
