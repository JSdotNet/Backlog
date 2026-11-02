namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The editing state behind the live editor: which block holds the caret, what
/// its textarea holds, and the whole source that typing into it makes.
/// <para>
/// Pure C#, so the rules are pinned here without a renderer: the source is
/// always the untouched blocks around what was typed; the blocks stay where they
/// were while the caret is in one, and are cut again when it leaves; and the
/// neighbour a key moves to is found by where the lines are, because the
/// edit may have just changed how many blocks there are.
/// </para>
/// </summary>
public sealed class MarkdownLiveStateTests
{
    /// <summary>
    /// Segments, in order: 0 the heading, 1 a blank line, 2 the wrapped
    /// paragraph, 3 a blank line, 4 the task list, 5 a blank line, 6 the last
    /// paragraph.
    /// </summary>
    private const string Doc = "# Title\n\nA paragraph\nthat wraps.\n\n- [ ] one\n- [x] two\n\nLast.";

    [Fact]
    public void A_new_state_has_no_block_active()
    {
        var state = new MarkdownLiveState(Doc);

        Assert.Equal(Doc, state.Source);
        Assert.Null(state.ActiveIndex);
        Assert.Equal(7, state.Document.Segments.Count);
    }

    [Fact]
    public void Activating_a_block_offers_its_own_lines_for_editing()
    {
        var state = new MarkdownLiveState(Doc);

        Assert.True(state.Activate(2));

        Assert.Equal(2, state.ActiveIndex);
        Assert.Equal("A paragraph\nthat wraps.", state.ActiveText);
        Assert.Equal(new MdSourceSpan(2, 4), state.ActiveLines);
    }

    [Fact]
    public void A_blank_line_between_blocks_holds_no_caret()
    {
        var state = new MarkdownLiveState(Doc);

        Assert.False(state.Activate(1));
        Assert.Null(state.ActiveIndex);
    }

    /// <summary>An empty document is one blank line, and an editor still needs
    /// somewhere to type.</summary>
    [Fact]
    public void An_empty_document_can_still_be_typed_into()
    {
        var state = new MarkdownLiveState(null);

        Assert.True(state.Activate(0));
        Assert.Equal("", state.ActiveText);
        Assert.Equal("Hello", state.Type("Hello"));
    }

    [Fact]
    public void Typing_makes_the_whole_source_from_the_untouched_blocks_around_it()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(2);

        var source = state.Type("Changed");

        Assert.Equal("# Title\n\nChanged\n\n- [ ] one\n- [x] two\n\nLast.", source);
        Assert.Equal(source, state.Source);
        Assert.Equal("Changed", state.ActiveText);
    }

    /// <summary>A half-typed fence would swallow every block after it if the
    /// document were cut again on every keystroke; it is not, so the rest of the
    /// document stays as it was until the caret leaves.</summary>
    [Fact]
    public void The_blocks_stay_where_they_were_while_typing()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(2);

        state.Type("```");

        Assert.Equal(7, state.Document.Segments.Count);
        Assert.Equal(Doc, state.Document.Source);
        Assert.IsType<MdList>(state.Document.Segments[4].Block);
    }

    /// <summary>A textarea speaks <c>\n</c>; a <c>\r\n</c> file is offered as
    /// <c>\n</c> and written back as <c>\r\n</c>.</summary>
    [Fact]
    public void A_crlf_document_is_edited_in_newlines_and_written_back_in_its_own()
    {
        var state = new MarkdownLiveState("One\r\ntwo\r\n\r\nThree\r\n");
        state.Activate(0);

        Assert.Equal("One\ntwo", state.ActiveText);
        Assert.Equal("Uno\r\ndos\r\n\r\nThree\r\n", state.Type("Uno\ndos"));
    }

    [Fact]
    public void Leaving_a_block_cuts_the_document_again()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(2);
        state.Type("A\n\nB");

        state.Commit();

        Assert.Null(state.ActiveIndex);
        Assert.Equal(state.Source, state.Document.Source);
        Assert.Equal(
            new[] { "A\n", "B\n" },
            state.Document.Segments
                .Where(segment => segment.Block is MdParagraph && segment.Lines.StartLine is >= 2 and < 5)
                .Select(segment => segment.Text));
    }

    [Fact]
    public void Leaving_an_untouched_block_changes_nothing()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(4);

        state.Commit();

        Assert.Equal(Doc, state.Source);
        Assert.Equal(Doc, string.Concat(state.Document.Segments.Select(segment => segment.Text)));
    }

    [Fact]
    public void Stepping_down_lands_at_the_start_of_the_next_block()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(2);

        Assert.Equal(new CaretTarget(4, 0), state.Step(+1));
        Assert.Equal("- [ ] one\n- [x] two", state.ActiveText);
    }

    [Fact]
    public void Stepping_up_lands_at_the_end_of_the_block_above()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(2);

        Assert.Equal(new CaretTarget(0, "# Title".Length), state.Step(-1));
        Assert.Equal("# Title", state.ActiveText);
    }

    /// <summary>A paragraph typed into two leaves two blocks where one was, so
    /// the block below is two segments further on than it was — found by its
    /// lines, it is the list, not the paragraph just typed.</summary>
    [Fact]
    public void Stepping_after_an_edit_finds_the_neighbour_by_its_lines()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(2);
        state.Type("A\n\nB");

        var target = state.Step(+1);

        Assert.NotNull(target);
        Assert.IsType<MdList>(state.Document.Segments[target.Value.Index].Block);
        Assert.Equal("- [ ] one\n- [x] two", state.ActiveText);
    }

    [Fact]
    public void Stepping_up_after_an_edit_finds_the_block_above_by_its_lines()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(4);
        state.Type("- [ ] one\n\nA new paragraph");

        Assert.Equal(new CaretTarget(2, "A paragraph\nthat wraps.".Length), state.Step(-1));
    }

    [Fact]
    public void Stepping_past_the_first_or_last_block_goes_nowhere_and_keeps_the_edit()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(0);
        state.Type("# Retitled");

        Assert.Null(state.Step(-1));
        Assert.Equal(0, state.ActiveIndex);
        Assert.Equal("# Retitled", state.ActiveText);
        Assert.Equal(Doc, state.Document.Source);

        state.Activate(6);
        Assert.Null(state.Step(+1));
        Assert.Equal("Last.", state.ActiveText);
    }

    /// <summary>The raw footnote definitions are a stop like any block.</summary>
    [Fact]
    public void Stepping_lands_on_raw_lines_no_block_claims()
    {
        var state = new MarkdownLiveState("A note.[^1]\n\n[^1]: The note.");
        state.Activate(0);

        Assert.Equal(new CaretTarget(2, 0), state.Step(+1));
        Assert.Equal("[^1]: The note.", state.ActiveText);
    }

    /// <summary>Clicking a block below the one being edited, when the edit
    /// added lines: the click named the block as it was drawn, and it is found
    /// again after the document is cut anew.</summary>
    [Fact]
    public void Activating_another_block_after_an_edit_finds_the_block_that_was_clicked()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(0);
        state.Type("# Title\n\nAn added paragraph");

        Assert.True(state.Activate(4));

        Assert.Equal("- [ ] one\n- [x] two", state.ActiveText);
        Assert.Equal(new MdSourceSpan(7, 9), state.ActiveLines);
    }

    /// <summary>Backspace at the start of a block: the block above and the
    /// blank lines between join the region, and the caret stays in front of
    /// the character it was in front of.</summary>
    [Fact]
    public void Joining_up_takes_in_the_block_above_and_keeps_the_caret_in_place()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(2);

        var caret = state.Join(-1);

        Assert.Equal("# Title\n\n".Length, caret);
        Assert.Equal("# Title\n\nA paragraph\nthat wraps.", state.ActiveText);
        Assert.Equal(new MdSourceSpan(0, 4), state.ActiveLines);
        Assert.Equal(Doc, state.Source);
    }

    [Fact]
    public void Typing_after_a_join_replaces_the_joined_lines()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(2);
        state.Join(-1);

        var source = state.Type("# Title\nA paragraph\nthat wraps.");

        Assert.Equal("# Title\nA paragraph\nthat wraps.\n\n- [ ] one\n- [x] two\n\nLast.", source);
    }

    /// <summary>Delete at the end of a block, after typing in it: what was
    /// typed is kept, and the block below joins after it.</summary>
    [Fact]
    public void Joining_down_keeps_what_was_typed_and_takes_in_the_block_below()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(2);
        state.Type("Edited");

        var caret = state.Join(+1);

        Assert.Equal("Edited".Length, caret);
        Assert.Equal("Edited\n\n- [ ] one\n- [x] two", state.ActiveText);
        Assert.Equal("# Title\n\nEdited\n\n- [ ] one\n- [x] two\n\nLast.", state.Source);
    }

    [Fact]
    public void Joining_past_the_first_or_last_block_changes_nothing()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(0);

        Assert.Equal(0, state.Join(-1));
        Assert.Equal(new MdSourceSpan(0, 1), state.ActiveLines);

        state.Activate(6);
        Assert.Equal("Last.".Length, state.Join(+1));
        Assert.Equal("Last.", state.ActiveText);
    }

    /// <summary>The host stores what the editor emitted and hands it back as
    /// the parameter — sometimes with its edges trimmed, as every parser-backed
    /// host does. That is the editor's own text coming home, not a new
    /// document, and the block being edited stays open.</summary>
    [Theory]
    [InlineData("Para!\n")]
    [InlineData("Para!")]
    [InlineData("\nPara!\n\n")]
    public void The_hosts_echo_of_what_was_typed_is_ignored(string echo)
    {
        var state = new MarkdownLiveState("Para.\n");
        state.Activate(0);
        state.Type("Para!");

        Assert.False(state.Reset(echo));

        Assert.Equal("Para!\n", state.Source);
        Assert.Equal(0, state.ActiveIndex);
    }

    [Fact]
    public void Any_other_value_from_the_host_starts_over()
    {
        var state = new MarkdownLiveState("Para.\n");
        state.Activate(0);
        state.Type("Para!");

        Assert.True(state.Reset("Something else"));

        Assert.Equal("Something else", state.Source);
        Assert.Equal("Something else", state.Document.Source);
        Assert.Null(state.ActiveIndex);
    }

    [Fact]
    public void The_same_value_again_is_no_reset()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(2);

        Assert.False(state.Reset(Doc));
        Assert.Equal(2, state.ActiveIndex);
    }

    [Fact]
    public void Ticking_a_task_rewrites_that_task_line()
    {
        var state = new MarkdownLiveState(Doc);

        var source = state.ToggleTask(0);

        Assert.Equal("# Title\n\nA paragraph\nthat wraps.\n\n- [x] one\n- [x] two\n\nLast.", source);
        Assert.Equal(source, state.Source);
        Assert.Equal(source, state.Document.Source);
        Assert.Null(state.ActiveIndex);
    }

    /// <summary>The checkbox ticked was drawn from the document as it was cut;
    /// a task typed into the block above since then would shift every index
    /// after it by one and tick the wrong line.</summary>
    [Fact]
    public void Ticking_a_task_after_typing_a_task_above_it_ticks_the_one_clicked()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(2);
        state.Type("- [ ] new task");

        var source = state.ToggleTask(0);

        Assert.Equal("# Title\n\n- [ ] new task\n\n- [x] one\n- [x] two\n\nLast.", source);
        Assert.False(state.Reset(source));
    }

    /// <summary>An opening fence typed above a list turns the list into code.
    /// The checkbox ticked was drawn before that, so its line is a code sample
    /// now, and ticking it rewrites nothing — least of all the task above the
    /// edit, which a shift by the net task count would land on.</summary>
    [Fact]
    public void Ticking_a_task_an_edit_has_turned_into_code_changes_nothing()
    {
        const string body = "- [ ] a\n\nPara\n\n- [ ] b\n- [ ] c";
        var state = new MarkdownLiveState(body);
        state.Activate(2);
        var typed = state.Type("```");

        Assert.Equal(typed, state.ToggleTask(2));
        Assert.Equal("- [ ] a\n\n```\n\n- [ ] b\n- [ ] c", state.Source);
    }

    /// <summary>A fence typed above pairs with a fence further down, so a
    /// <c>- [ ]</c> that was drawn as a code sample becomes a task and the task
    /// that was clicked becomes code. The tick lands on neither.</summary>
    [Fact]
    public void Ticking_a_task_after_an_edit_never_lands_on_a_code_sample_turned_task()
    {
        const string body = "- [ ] a\n\nPara\n\n```\n- [ ] sample\n```\n\n- [ ] b";
        var state = new MarkdownLiveState(body);
        state.Activate(2);
        var typed = state.Type("```");

        Assert.Equal(typed, state.ToggleTask(1));
    }

    /// <summary>A tick rewrites one marker. Every other byte of a <c>\r\n</c>
    /// file stays what it was, line ends included.</summary>
    [Fact]
    public void Ticking_a_task_in_a_crlf_document_keeps_its_line_ends()
    {
        var state = new MarkdownLiveState("a\r\n\r\n- [ ] t\r\n- [ ] u\r\n");

        Assert.Equal("a\r\n\r\n- [ ] t\r\n- [x] u\r\n", state.ToggleTask(1));
    }

    /// <summary>An unclosed fence typed into a block runs on over everything
    /// below it, so the block below is now part of the one just edited. Down
    /// goes one line on inside it — not back to its first character.</summary>
    [Fact]
    public void Stepping_down_into_the_block_the_edit_merged_with_moves_one_line_on()
    {
        var state = new MarkdownLiveState(Doc);
        state.Activate(2);
        state.Type("```");

        Assert.Equal(new CaretTarget(2, "```\n".Length), state.Step(+1));
    }

    /// <summary>A list item typed into prose joins the paragraph above it. Up
    /// goes to the end of the line above inside that paragraph — not to the end
    /// of what was just typed.</summary>
    [Fact]
    public void Stepping_up_into_the_block_the_edit_merged_with_lands_at_the_end_of_the_line_above()
    {
        var state = new MarkdownLiveState("Para\n- item");
        state.Activate(1);
        state.Type("more text");

        Assert.Equal(new CaretTarget(0, "Para".Length), state.Step(-1));
        Assert.Equal("Para\nmore text", state.ActiveText);
    }
}
