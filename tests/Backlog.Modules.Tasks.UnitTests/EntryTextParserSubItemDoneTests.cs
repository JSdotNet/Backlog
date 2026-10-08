using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// <see cref="EntryTextParser.WithSubItemDone"/>: the explicit form of the step
/// checkbox, numbered the way the parse numbers sub-items and written the way the
/// desktop writes each kind of step.
/// </summary>
public class EntryTextParserSubItemDoneTests
{
    [Fact]
    public void A_plain_heading_is_marked_done_on_its_own_metadata_line()
    {
        const string raw = "# Trip\n`!ready`\n\n## Pack\n\n## Book the train\n";

        var rewritten = EntryTextParser.WithSubItemDone(raw, 1, done: true);

        var steps = EntryTextParser.Parse(rewritten).SubItems;
        Assert.False(steps[0].Done);
        Assert.True(steps[1].Done);
        Assert.Matches(@"## Book the train\n`[^\n]*!done`", rewritten);
    }

    [Fact]
    public void A_plain_heading_marked_done_is_unmarked_as_ready()
    {
        const string raw = "# Trip\n`!ready`\n\n## Pack\n`!done`\n";

        var rewritten = EntryTextParser.WithSubItemDone(raw, 0, done: false);

        Assert.False(Assert.Single(EntryTextParser.Parse(rewritten).SubItems).Done);
        Assert.Matches(@"## Pack\n`[^\n]*!ready`", rewritten);
    }

    [Fact]
    public void A_heading_with_a_checkbox_has_its_marker_flipped()
    {
        const string raw = "# Trip\n`!ready`\n\n## [ ] Pack\n";

        var rewritten = EntryTextParser.WithSubItemDone(raw, 0, done: true);

        Assert.Equal("# Trip\n`!ready`\n\n## [x] Pack\n", rewritten);
    }

    /// <summary>A step ticked by its checkbox and then cascaded <c>!done</c> by
    /// its task being marked done reads done through either, so unticking it
    /// clears both.</summary>
    [Fact]
    public void A_step_done_by_its_marker_and_its_metadata_line_is_unticked_by_both()
    {
        const string raw = "# Trip\n`!done`\n\n## [x] Pack\n`!done`\n";

        var rewritten = EntryTextParser.WithSubItemDone(raw, 0, done: false);

        Assert.False(Assert.Single(EntryTextParser.Parse(rewritten).SubItems).Done);
    }

    [Fact]
    public void A_checklist_line_counts_in_the_parses_order_and_has_its_marker_flipped()
    {
        const string raw = "# Trip\n`!ready`\n\n- [ ] Charger\n\n## Pack\n\n- [ ] Tickets\n";

        var rewritten = EntryTextParser.WithSubItemDone(raw, 2, done: true);

        Assert.Equal("# Trip\n`!ready`\n\n- [ ] Charger\n\n## Pack\n\n- [x] Tickets\n", rewritten);
        Assert.Equal([false, false, true], EntryTextParser.Parse(rewritten).SubItems.Select(step => step.Done));
    }

    [Fact]
    public void A_heading_inside_a_fence_is_not_a_step()
    {
        const string raw = "# Trip\n`!ready`\n\n```\n## not a step\n```\n\n## Pack\n";

        var rewritten = EntryTextParser.WithSubItemDone(raw, 0, done: true);

        Assert.True(Assert.Single(EntryTextParser.Parse(rewritten).SubItems).Done);
        Assert.Contains("```\n## not a step\n```", rewritten);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(-1, true)]
    public void A_step_already_so_or_past_the_end_leaves_the_text_alone(int index, bool done)
    {
        const string raw = "# Trip\n`!ready`\n\n## [x] Pack\n";

        Assert.Same(raw, EntryTextParser.WithSubItemDone(raw, index, done));
    }
}
