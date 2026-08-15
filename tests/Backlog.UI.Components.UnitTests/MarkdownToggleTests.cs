namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The round trip: the read view reports a task index, and the rewriter has to
/// land on the line the parser gave that index to. Every test here is really the
/// same assertion — that the two halves count the same lines — because when they
/// drift the symptom is a checkbox that does not move while some other line
/// silently changes.
/// </summary>
public sealed class MarkdownToggleTests
{
    [Fact]
    public void Toggling_flips_the_task_the_index_names()
    {
        const string source = "- [ ] One\n- [ ] Two\n";

        Assert.Equal("- [ ] One\n- [x] Two\n", MarkdownPreview.ToggleTask(source, 1));
    }

    [Fact]
    public void Toggling_a_done_task_clears_it()
    {
        Assert.Equal("- [ ] One\n", MarkdownPreview.ToggleTask("- [x] One\n", 0));
    }

    [Fact]
    public void A_task_inside_a_fence_is_a_code_sample_and_never_counted()
    {
        // The parser folds fenced lines into an MdCode, so they get no index. A
        // rewriter that counted them would shift every real task by one and
        // edit the code sample instead of the checkbox that was clicked.
        const string source = "```\n- [ ] not a task\n```\n\n- [ ] real one\n- [ ] real two\n";

        var toggled = MarkdownPreview.ToggleTask(source, 0);

        Assert.Equal("```\n- [ ] not a task\n```\n\n- [x] real one\n- [ ] real two\n", toggled);
    }

    [Fact]
    public void An_empty_checkbox_line_is_not_a_task_on_either_side()
    {
        // `- [ ]` with nothing after it renders as a plain bullet reading "[ ]",
        // so it has no index to be nth of.
        const string source = "- [ ] \n- [ ] real task\n";

        Assert.False(MarkdownPreview.IsTaskLine("- [ ] "));
        Assert.Equal("- [ ] \n- [x] real task\n", MarkdownPreview.ToggleTask(source, 0));
    }

    [Fact]
    public void A_quoted_task_line_is_not_counted_either()
    {
        const string source = "> - [ ] quoted\n\n- [ ] real\n";

        Assert.Equal("> - [ ] quoted\n\n- [x] real\n", MarkdownPreview.ToggleTask(source, 0));
    }

    [Fact]
    public void Indentation_and_star_markers_survive_the_rewrite()
    {
        Assert.Equal("  * [x] Indented\n", MarkdownPreview.ToggleTask("  * [ ] Indented\n", 0));
    }

    [Fact]
    public void Trailing_text_after_the_marker_is_left_exactly_as_it_was()
    {
        const string source = "- [ ] Ship it #now `today`\n";

        Assert.Equal("- [x] Ship it #now `today`\n", MarkdownPreview.ToggleTask(source, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void An_index_that_names_no_task_leaves_the_source_alone(int taskIndex)
    {
        // Untouched means untouched, line endings included: the caller's text is
        // the source of truth and a no-op must not rewrite it.
        const string source = "- [ ] One\r\n- [ ] Two\r\n";

        Assert.Same(source, MarkdownPreview.ToggleTask(source, taskIndex));
    }

    [Fact]
    public void Every_index_the_parser_hands_out_can_be_toggled()
    {
        // The property the round trip actually needs, over a body with all the
        // decoys in it: for each index the parser assigned, toggling it flips
        // that task and nothing else.
        const string source = """
            # Body

            - [ ] alpha
            - a plain bullet
            - [x] beta

            ```
            - [ ] decoy in a fence
            ```

            > - [ ] decoy in a quote

            - [ ] gamma
            """;

        var indexes = MarkdownPreview.Parse(source)
            .OfType<MdList>()
            .SelectMany(list => list.Items)
            .Select(item => item.TaskIndex)
            .OfType<int>()
            .ToArray();

        Assert.Equal([0, 1, 2], indexes);

        foreach (var index in indexes)
        {
            var before = Tasks(source);
            var after = Tasks(MarkdownPreview.ToggleTask(source, index));

            var changed = before.Zip(after).Select((pair, i) => (i, pair.First, pair.Second))
                .Where(entry => entry.First != entry.Second)
                .ToArray();

            var flipped = Assert.Single(changed);
            Assert.Equal(index, flipped.i);
        }

        static bool[] Tasks(string markdown) => MarkdownPreview.Parse(markdown)
            .OfType<MdList>()
            .SelectMany(list => list.Items)
            .Where(item => item.TaskIndex is not null)
            .Select(item => item.Done == true)
            .ToArray();
    }
}
