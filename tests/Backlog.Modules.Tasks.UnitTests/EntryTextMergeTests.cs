using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// Two edits to one entry, made from the same starting text, applied together.
/// Written as the edits that actually meet: a reader typing in the body while an
/// agent comments on the entry or moves its status.
/// </summary>
public sealed class EntryTextMergeTests
{
    private const string Base = "# Draft the rollout\n`task` `*medium` `!ready`\n\nFirst paragraph.\n\nSecond paragraph.\n";

    [Fact]
    public void Edits_to_different_lines_both_land()
    {
        var ours = Base.Replace("Second paragraph.", "Second paragraph, reworded.", StringComparison.Ordinal);
        var theirs = Base.Replace("`!ready`", "`!in-progress`", StringComparison.Ordinal);

        var merged = EntryTextMerge.Merge(Base, ours, theirs);

        Assert.False(merged.Overlapped);
        Assert.Equal(
            "# Draft the rollout\n`task` `*medium` `!in-progress`\n\nFirst paragraph.\n\nSecond paragraph, reworded.\n",
            merged.Text);
    }

    /// <summary>The reported case: the reader is still typing at the end of the
    /// last paragraph, and the comment is appended right after it.</summary>
    [Fact]
    public void A_comment_appended_after_the_line_being_typed_is_carried_onto_it()
    {
        var ours = Base.Replace("Second paragraph.", "Second paragraph, still being typed", StringComparison.Ordinal);
        var theirs = Base.TrimEnd('\n') + "\n\n2026-09-26: Picked up by an agent.\n";

        var merged = EntryTextMerge.Merge(Base, ours, theirs);

        Assert.False(merged.Overlapped);
        Assert.Equal(
            "# Draft the rollout\n`task` `*medium` `!ready`\n\nFirst paragraph.\n\nSecond paragraph, still being typed\n\n2026-09-26: Picked up by an agent.\n",
            merged.Text);
    }

    [Fact]
    public void The_same_edit_made_twice_lands_once()
    {
        var both = Base.Replace("First paragraph.", "First paragraph, fixed.", StringComparison.Ordinal);

        var merged = EntryTextMerge.Merge(Base, both, both);

        Assert.False(merged.Overlapped);
        Assert.Equal(both, merged.Text);
    }

    [Fact]
    public void Two_rewrites_of_the_same_body_line_keep_both_the_readers_first()
    {
        var ours = Base.Replace("First paragraph.", "Mine.", StringComparison.Ordinal);
        var theirs = Base.Replace("First paragraph.", "Theirs.", StringComparison.Ordinal);

        var merged = EntryTextMerge.Merge(Base, ours, theirs);

        Assert.True(merged.Overlapped);
        Assert.Contains("\nMine.\nTheirs.\n", merged.Text, StringComparison.Ordinal);
    }

    /// <summary>The metadata line is one line by grammar; a second one would be
    /// read as body text. The reader's stands, and the overlap is reported.</summary>
    [Fact]
    public void Two_rewrites_of_the_metadata_line_keep_the_readers()
    {
        var ours = Base.Replace("`!ready`", "`!blocked`", StringComparison.Ordinal);
        var theirs = Base.Replace("`!ready`", "`!done`", StringComparison.Ordinal);

        var merged = EntryTextMerge.Merge(Base, ours, theirs);

        Assert.True(merged.Overlapped);
        Assert.Equal(ours, merged.Text);
    }

    [Fact]
    public void A_side_that_did_not_change_takes_the_other()
    {
        var edited = Base.Replace("First", "Opening", StringComparison.Ordinal);

        Assert.Equal(edited, EntryTextMerge.Merge(Base, Base, edited).Text);
        Assert.Equal(edited, EntryTextMerge.Merge(Base, edited, Base).Text);
    }
}
