using Backlog.Infrastructure.Devbook.Building;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The link writer's edit (<see cref="BrainNoteLinks.Append"/>, local ADR 0052)
/// read back through the builder's own parse: the block it appends to is the
/// block the parse calls the file's, and the value it adds to a flow list is the
/// value the parse finds there — so the two cannot disagree about which block is
/// the note's top one.
/// </summary>
public sealed class BrainNoteLinksParityTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void An_appended_task_id_is_in_the_file_s_block_as_the_parse_reads_it(string eol)
    {
        var text = string.Join(eol, "```text", "# Not the title", "```", "", "# Idea", "", "```meta", "type: note", "tasks: [a1]", "```", "", "## Later", "", "```meta", "tasks: [z9]", "```", "");

        var edit = BrainNoteLinks.Append(text, BrainLinkField.Tasks, "b2");

        var parsed = DevbookMarkdown.Parse(edit.Text!);
        Assert.Equal("Idea", parsed.FileTitle);
        Assert.Equal(["a1", "b2"], (string[])parsed.FileMeta!["tasks"]!);
        Assert.Equal(["z9"], (string[])parsed.Chapters.Single(c => c.Text == "Later").Meta!["tasks"]!);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void A_missing_roadmap_field_is_added_where_the_parse_reads_it(string eol)
    {
        var text = BrainNoteText.Compose("Plan the sync", "Body.", BrainNoteSource.Manual).Replace("\n", eol, StringComparison.Ordinal);

        var edit = BrainNoteLinks.Append(text, BrainLinkField.Roadmap, "+gtd-para");

        Assert.Equal(["+gtd-para"], (string[])DevbookMarkdown.Parse(edit.Text!).FileMeta!["roadmap"]!);
    }
}
