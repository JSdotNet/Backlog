namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Undo and redo in the live editor, held by the document rather than by the
/// textarea.
/// <para>
/// A textarea's own undo is the box's, and the box is rebuilt for every block
/// the caret moves into: an edit made in one block was gone from undo the
/// moment the reader stepped into the next. So the history is the state's — the
/// whole source before and after each step, with the block it was made in and
/// where the caret was — and undoing reopens that block wherever the caret has
/// gone since.
/// </para>
/// <para>
/// One rule coalesces keystrokes: a step runs for as long as the reader keeps
/// doing the same thing in the same block. A new one starts when a word starts
/// (a space or a line break typed after something that is not one), when typing
/// turns into deleting or back, when a selection is typed over, when the caret
/// leaves or the block grows, and at every tick, undo and redo.
/// </para>
/// </summary>
public sealed class MarkdownLiveStateHistoryTests
{
    /// <summary>
    /// Segments, in order: 0 the heading, 1 a blank line, 2 the wrapped
    /// paragraph, 3 a blank line, 4 the task list, 5 a blank line, 6 the last
    /// paragraph. Lines: the list is 5–6, the last paragraph 8.
    /// </summary>
    private const string Doc = "# Title\n\nA paragraph\nthat wraps.\n\n- [ ] one\n- [x] two\n\nLast.";

    /// <summary>Types <paramref name="keys"/> one character at a time after
    /// what the box holds, the way a reader does.</summary>
    private static void TypeKeys(MarkdownLiveState state, string keys)
    {
        foreach (var key in keys) state.Type(state.ActiveText + key);
    }

    [Fact]
    public void A_new_document_has_nothing_to_undo_or_redo()
    {
        var state = new MarkdownLiveState(Doc);

        Assert.Null(state.Undo());
        Assert.Null(state.Redo());
        Assert.Equal(Doc, state.Source);
    }

    [Fact]
    public void A_word_typed_key_by_key_is_one_step()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(6);
        TypeKeys(state, "!!!");

        var step = state.Undo();

        Assert.NotNull(step);
        Assert.Equal(Doc, step.Value.Source);
        Assert.Equal(Doc, state.Source);
        Assert.Equal(new CaretTarget(6, "Last.".Length), step.Value.Caret);
        Assert.Equal("Last.", state.ActiveText);
        Assert.Null(state.Undo());
    }

    [Fact]
    public void A_new_word_is_a_new_step()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(6);
        TypeKeys(state, " ab cd");

        Assert.Equal(Doc.Replace("Last.", "Last. ab"), state.Undo()!.Value.Source);
        Assert.Equal("Last. ab", state.ActiveText);
        Assert.Equal(Doc, state.Undo()!.Value.Source);
        Assert.Null(state.Undo());
    }

    [Fact]
    public void Deleting_after_typing_is_a_new_step()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(6);
        TypeKeys(state, "xy");
        state.Type("Last.x");

        Assert.Equal(Doc.Replace("Last.", "Last.xy"), state.Undo()!.Value.Source);
        Assert.Equal(Doc, state.Undo()!.Value.Source);
    }

    /// <summary>The caret leaving a block ends the step, even when it comes
    /// straight back.</summary>
    [Fact]
    public void The_caret_leaving_the_block_ends_the_step()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(6);
        TypeKeys(state, "!");
        state.Commit();
        state.Activate(6);
        TypeKeys(state, "!");

        Assert.Equal(Doc.Replace("Last.", "Last.!"), state.Undo()!.Value.Source);
    }

    /// <summary>
    /// The reason the history is the document's. The edit was in the paragraph;
    /// the caret has since gone to another block. Undo puts the paragraph back
    /// and reopens it, with the caret at the end of what it restored.
    /// </summary>
    [Fact]
    public void Undo_reopens_the_block_the_edit_was_made_in_wherever_the_caret_is_now()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(2);
        state.Type("Changed");
        state.Commit();
        state.Activate(6);

        var step = state.Undo()!.Value;

        Assert.Equal(Doc, step.Source);
        Assert.Equal(new CaretTarget(2, "A paragraph\nthat wraps.".Length), step.Caret);
        Assert.Equal(2, state.ActiveIndex);
        Assert.Equal("A paragraph\nthat wraps.", state.ActiveText);
    }

    [Fact]
    public void Redo_puts_the_step_back_in_the_block_it_was_made_in()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(2);
        state.Type("Changed");
        state.Commit();
        state.Activate(6);
        state.Undo();

        var step = state.Redo()!.Value;

        Assert.Equal(Doc.Replace("A paragraph\nthat wraps.", "Changed"), step.Source);
        Assert.Equal(new CaretTarget(2, "Changed".Length), step.Caret);
        Assert.Equal("Changed", state.ActiveText);
        Assert.Null(state.Redo());
    }

    [Fact]
    public void Typing_after_an_undo_leaves_nothing_to_redo()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(6);
        TypeKeys(state, "!");
        state.Undo();

        TypeKeys(state, "?");

        Assert.Null(state.Redo());
        Assert.Equal(Doc, state.Undo()!.Value.Source);
    }

    /// <summary>A tick is a step of its own, and undoing it opens nothing: it
    /// was made on a rendered block.</summary>
    [Fact]
    public void A_ticked_task_is_one_step_that_reopens_nothing()
    {
        var state = new MarkdownLiveState(Doc);
        state.ToggleTask(0);

        var step = state.Undo()!.Value;

        Assert.Equal(Doc, step.Source);
        Assert.Null(step.Caret);
        Assert.Null(state.ActiveIndex);
        Assert.Equal(Doc.Replace("- [ ] one", "- [x] one"), state.Redo()!.Value.Source);
    }

    /// <summary>
    /// A join changes no byte, so it is no step by itself — but the line break
    /// it lets the next Backspace remove is, and undoing that puts the break
    /// back and reopens the region the join had grown.
    /// </summary>
    [Fact]
    public void The_break_a_join_lets_backspace_remove_comes_back_with_the_joined_region()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(6);
        state.Join(-1);
        Assert.Equal("- [ ] one\n- [x] two\n\nLast.", state.ActiveText);
        state.Type("- [ ] one\n- [x] two\nLast.");

        var step = state.Undo()!.Value;

        Assert.Equal(Doc, step.Source);
        Assert.Equal(new CaretTarget(4, "- [ ] one\n- [x] two\n\n".Length), step.Caret);
        Assert.Equal("- [ ] one\n- [x] two\n\nLast.", state.ActiveText);
        Assert.Equal(3, state.ActiveSegmentCount);
    }

    /// <summary>Undo writes back the source as it was, byte for byte — the
    /// typed text went in as <c>\n</c>, and the undone source still has every
    /// <c>\r\n</c> it had.</summary>
    [Fact]
    public void Undo_restores_a_crlf_document_byte_for_byte()
    {
        var crlf = Doc.Replace("\n", "\r\n");
        var state = new MarkdownLiveState(crlf);
        state.Activate(2);
        state.Type("One\n\nTwo");
        state.Commit();

        Assert.Equal(crlf, state.Undo()!.Value.Source);
        Assert.Equal(crlf.Replace("A paragraph\r\nthat wraps.", "One\r\n\r\nTwo"), state.Redo()!.Value.Source);
    }

    /// <summary>The host's echo of an undo is the editor's own text coming back,
    /// and a value the host sets of its own is a new document with a history of
    /// its own — none.</summary>
    [Fact]
    public void A_host_value_that_is_not_the_echo_clears_the_history()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(6);
        TypeKeys(state, "!");

        var undone = state.Undo()!.Value.Source;
        Assert.False(state.Reset(undone));
        Assert.NotNull(state.Redo());

        Assert.True(state.Reset("Something else."));
        Assert.Null(state.Undo());
        Assert.Null(state.Redo());
    }

    /// <summary>A paste is a step of its own, even one that does not start with
    /// a space: undoing it leaves the word typed before it.</summary>
    [Fact]
    public void A_paste_is_a_step_of_its_own()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(6);
        TypeKeys(state, "ab");
        state.Type(state.ActiveText + "pasted");

        Assert.Equal(Doc.Replace("Last.", "Last.ab"), state.Undo()!.Value.Source);
    }

    /// <summary>Typing somewhere else in the same box — the reader clicked to
    /// another place in it — starts a new step.</summary>
    [Fact]
    public void Typing_somewhere_else_in_the_box_is_a_new_step()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(6);
        TypeKeys(state, "!");
        state.Type("x" + state.ActiveText);

        Assert.Equal(Doc.Replace("Last.", "Last.!"), state.Undo()!.Value.Source);
    }

    /// <summary>Backspace after Backspace, and Delete after Delete, run on as one
    /// step: each takes up where the last left the caret.</summary>
    [Fact]
    public void A_run_of_deletes_is_one_step()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(6);
        state.Type("Last");
        state.Type("Las");
        state.Type("La");

        Assert.Equal(Doc, state.Undo()!.Value.Source);
        Assert.Null(state.Undo());
    }

    /// <summary>The history keeps whole sources, so it is bounded: past the
    /// limit the oldest steps go, and the newest are still there.</summary>
    [Fact]
    public void The_history_keeps_the_newest_steps_up_to_its_limit()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(6);
        for (var word = 0; word < MarkdownLiveState.HistoryLimit + 20; word++) TypeKeys(state, " a");

        var undone = 0;
        while (state.Undo() is not null) undone++;

        Assert.Equal(MarkdownLiveState.HistoryLimit, undone);
        Assert.Equal(Doc.Replace("Last.", "Last." + string.Concat(Enumerable.Repeat(" a", 20))), state.Source);
    }

    /// <summary>
    /// A word typed in the middle of a line is one step wherever it lands — even
    /// where a typed character equals its neighbour, so the text alone cannot say
    /// which side of the neighbour it went in on. Before a space, inside a word
    /// next to a letter it repeats, and at the end of the line.
    /// </summary>
    [Theory]
    [InlineData("Ship. Merge", 5)]  // " beta" typed before " Merge": the space repeats the space after it
    [InlineData("Merge", 1)]        // " beta" typed into "M|erge": the "e" repeats the "e" after it
    [InlineData("Merge", 5)]        // at the end of the line, where nothing is ambiguous
    public void A_word_typed_beside_a_repeated_character_is_one_step(string line, int caret)
    {
        var doc = $"# Title\n\n{line}";
        var state = new MarkdownLiveState(doc);
        state.Activate(2);

        var text = line;
        foreach (var key in " beta")
        {
            text = text.Insert(caret++, key.ToString());
            state.Type(text);
        }

        Assert.Equal(doc, state.Undo()!.Value.Source);
        Assert.Null(state.Undo());
    }

    /// <summary>
    /// Undoing or redoing a tick reopens no block, so the step says which task it
    /// ticked instead — the editor puts focus back on that checkbox, and the
    /// keyboard stays in the editor for the next undo.
    /// </summary>
    [Fact]
    public void Undoing_or_redoing_a_tick_names_the_task_it_ticked()
    {
        var state = new MarkdownLiveState(Doc);
        state.ToggleTask(1);

        var undone = state.Undo()!.Value;
        Assert.Null(undone.Caret);
        Assert.Equal(1, undone.Task);

        var redone = state.Redo()!.Value;
        Assert.Null(redone.Caret);
        Assert.Equal(1, redone.Task);
    }

    /// <summary>A typed step reopens its block and names no task.</summary>
    [Fact]
    public void A_typed_step_names_no_task()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(6);
        TypeKeys(state, "!");

        Assert.Null(state.Undo()!.Value.Task);
    }

    /// <summary>Retyping what is already there is not an edit.</summary>
    [Fact]
    public void A_value_the_box_already_holds_is_no_step()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(6);
        state.Type("Last.");

        Assert.Null(state.Undo());
    }
}
