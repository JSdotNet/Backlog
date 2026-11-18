namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A Brain note's address as the pane follows it: relative to the Brain's root,
/// its first segment the area folder — the way the Brain's database and its menu
/// both name a note.
/// </summary>
public sealed class DevbookBrainNoteLinkTests
{
    [Fact]
    public void A_note_resolves_to_its_area_and_keeps_its_whole_path()
    {
        var link = DevbookChapterLink.ForBrainNote("projects/plan.md#next-step");

        Assert.NotNull(link);
        Assert.Equal(DevbookMenu.PersonalTabKey("projects"), link.AreaKey);
        Assert.Equal("projects/plan.md", link.RelativePath);
        Assert.Equal("next-step", link.Anchor);
    }

    [Theory]
    [InlineData("./work/standup.md", "work/standup.md")]
    [InlineData("work\\standup.md", "work/standup.md")]
    [InlineData("/work/deep/standup.md", "work/deep/standup.md")]
    public void Spellings_of_the_same_note_resolve_alike(string reference, string expected)
    {
        Assert.Equal(expected, DevbookChapterLink.ForBrainNote(reference)?.RelativePath);
    }

    /// <summary>The area key is lower-cased the way the pane's tabs are keyed, so
    /// an area folder spelled with capitals still lands on its tab.</summary>
    [Fact]
    public void An_area_folder_with_capitals_lands_on_its_tab()
    {
        Assert.Equal(DevbookMenu.PersonalTabKey("projects"), DevbookChapterLink.ForBrainNote("Projects/plan.md")?.AreaKey);
    }

    /// <summary>An area folder alone names its tab — what the Inbox's queue health
    /// opens the Brain's inbox area by.</summary>
    [Theory]
    [InlineData("_inbox/")]
    [InlineData("./_Inbox/")]
    public void An_area_folder_names_its_tab(string reference)
    {
        var link = DevbookChapterLink.ForBrainNote(reference);

        Assert.NotNull(link);
        Assert.Equal(DevbookMenu.PersonalTabKey("_inbox"), link.AreaKey);
        Assert.True(link.IsFolder);
        Assert.Null(link.Anchor);
    }

    [Fact]
    public void A_note_is_not_a_folder() =>
        Assert.False(DevbookChapterLink.ForBrainNote("_inbox/standup.md")!.IsFolder);

    [Theory]
    [InlineData("plan.md")]
    [InlineData("_inbox")]
    [InlineData("_inbox/#top")]
    [InlineData("projects/diagram.png")]
    [InlineData("")]
    [InlineData(null)]
    public void What_sits_in_no_area_or_is_not_a_note_resolves_to_nothing(string? reference)
    {
        Assert.Null(DevbookChapterLink.ForBrainNote(reference));
    }
}
