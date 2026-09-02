
namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The first thing typed into an entry is its title, so it is written as a
/// heading rather than left to whoever remembers the <c>#</c>.
/// </summary>
public sealed class TitleHeadingTests
{
    [Fact]
    public void A_plain_first_line_becomes_a_heading()
    {
        Assert.Equal("# Buy milk", TasksDesktopState.EnsureTitleHeading("Buy milk"));
    }

    [Fact]
    public void A_line_that_is_already_a_heading_is_left_alone()
    {
        const string raw = "# Buy milk\n\nand bread";

        Assert.Equal(raw, TasksDesktopState.EnsureTitleHeading(raw));
    }

    [Fact]
    public void Only_the_first_line_is_touched()
    {
        var result = TasksDesktopState.EnsureTitleHeading("Buy milk\n\nand bread\n## a sub-item");

        Assert.Equal("# Buy milk\n\nand bread\n## a sub-item", result);
    }

    [Fact]
    public void Blank_lines_above_the_title_do_not_count_as_the_first_line()
    {
        var result = TasksDesktopState.EnsureTitleHeading("\n\nBuy milk");

        Assert.Equal("\n\n# Buy milk", result);
    }

    [Fact]
    public void Leading_whitespace_on_the_title_is_absorbed()
    {
        Assert.Equal("# Buy milk", TasksDesktopState.EnsureTitleHeading("   Buy milk"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void Nothing_typed_stays_nothing(string raw)
    {
        Assert.Equal(raw, TasksDesktopState.EnsureTitleHeading(raw));
    }

    [Fact]
    public void A_deeper_heading_on_the_first_line_is_not_promoted()
    {
        // Someone who opened with `## ` meant that. Rewriting it to `# ` would
        // change the structure of what they wrote.
        const string raw = "## a sub-item first";

        Assert.Equal(raw, TasksDesktopState.EnsureTitleHeading(raw));
    }

    [Fact]
    public void A_first_line_that_opens_a_code_fence_is_left_alone()
    {
        const string raw = "```\nnot a title\n```";

        Assert.Equal(raw, TasksDesktopState.EnsureTitleHeading(raw));
    }

    [Fact]
    public void The_result_parses_to_the_same_title_as_the_input()
    {
        const string raw = "Buy milk\n`task`\n\nnotes";

        var normalized = TasksDesktopState.EnsureTitleHeading(raw);

        Assert.Equal(
            EntryTextParser.Parse(raw).Title,
            EntryTextParser.Parse(normalized).Title);
    }

    [Fact]
    public void Normalizing_twice_changes_nothing_the_second_time()
    {
        var once = TasksDesktopState.EnsureTitleHeading("Buy milk\n\nnotes");

        Assert.Equal(once, TasksDesktopState.EnsureTitleHeading(once));
    }

    [Fact]
    public void Windows_line_endings_survive_as_newlines()
    {
        var result = TasksDesktopState.EnsureTitleHeading("Buy milk\r\nand bread");

        Assert.Equal("# Buy milk\nand bread", result);
    }
}
