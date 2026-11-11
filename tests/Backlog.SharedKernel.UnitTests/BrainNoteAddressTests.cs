namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// The address a plan writes to name a note in its owner's Brain (local ADR 0053 §1):
/// <c>#brain:&lt;path&gt;</c>, optionally followed by <c>#&lt;slug&gt;</c>. One spelling,
/// shared by Roadmap, Import and the MCP tools, so no reader can mistake a Brain note
/// for a repository chapter.
/// </summary>
public sealed class BrainNoteAddressTests
{
    [Theory]
    [InlineData("#brain:projects/sync.md", "projects/sync.md", null)]
    [InlineData("#brain:projects/sync.md#the-plan", "projects/sync.md", "the-plan")]
    [InlineData("  #brain:projects/sync.md  ", "projects/sync.md", null)]
    [InlineData("#brain:projects\\sync.md", "projects/sync.md", null)]
    [InlineData("#brain:/projects/sync.md", "projects/sync.md", null)]
    [InlineData("#brain:projects/sync.md#", "projects/sync.md", null)]
    [InlineData("#Brain:projects/sync.md", "projects/sync.md", null)]
    public void An_address_reads_as_the_note_path_and_its_optional_slug(string text, string path, string? slug)
    {
        Assert.True(BrainNoteAddress.TryParse(text, out var address));
        Assert.Equal(path, address.Path);
        Assert.Equal(slug, address.Slug);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#brain:")]
    [InlineData("#brain:   ")]
    [InlineData("#brain:#slug")]
    [InlineData("#brain:../outside.md")]
    [InlineData("#brain:projects/../../outside.md")]
    [InlineData("brain:projects/sync.md")]
    [InlineData("#brainprojects/sync.md")]
    [InlineData("#brain projects/sync.md")]
    public void Anything_that_is_not_a_note_in_the_brain_is_refused(string? text) =>
        Assert.False(BrainNoteAddress.TryParse(text, out _));

    [Theory]
    [InlineData(".devbook/domain/tasks/domain.md#task")]
    [InlineData("domain/x.md#y")]
    [InlineData("arc42/adr/0053-a-plan-or-a-task-may-reference-a-brain-note.md")]
    [InlineData("#task")]
    public void A_repository_reference_is_never_a_brain_address(string reference)
    {
        Assert.False(BrainNoteAddress.Is(reference));
        Assert.False(BrainNoteAddress.TryParse(reference, out _));
    }

    [Theory]
    [InlineData("#brain:projects/sync.md")]
    [InlineData("#BRAIN:projects/sync.md")]
    [InlineData("#brain:../outside.md")]
    [InlineData("#brain:")]
    public void Every_reference_wearing_the_prefix_claims_the_brain_even_one_that_names_no_note(string reference) =>
        Assert.True(BrainNoteAddress.Is(reference));

    [Theory]
    [InlineData("projects/sync.md", null, "#brain:projects/sync.md")]
    [InlineData("projects/sync.md", "the-plan", "#brain:projects/sync.md#the-plan")]
    public void Format_writes_the_one_spelling_and_reads_back_to_the_same_note(string path, string? slug, string expected)
    {
        var written = BrainNoteAddress.Format(path, slug);

        Assert.Equal(expected, written);
        Assert.True(BrainNoteAddress.TryParse(written, out var address));
        Assert.Equal(path, address.Path);
        Assert.Equal(slug, address.Slug);
    }

    [Fact]
    public void A_parsed_address_names_its_note_the_way_the_devbook_pane_opens_one()
    {
        Assert.True(BrainNoteAddress.TryParse("#Brain:projects\\sync.md#the-plan", out var address));

        Assert.Equal("#brain:projects/sync.md#the-plan", address.Value);
        Assert.Equal("projects/sync.md#the-plan", address.Chapter);
    }
}
