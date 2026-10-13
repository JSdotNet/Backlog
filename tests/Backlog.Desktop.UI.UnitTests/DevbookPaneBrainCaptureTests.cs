using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Capture on the Devbook pane in Brain mode
/// (<c>.devbook/domain/devbook/requirements.md#capture-into-the-brain</c>): a title
/// and a body written as a new note in the Brain's inbox area, through the writer
/// Inbox triage's To Brain shares, and opened once it is there.
/// </summary>
public sealed class DevbookPaneBrainCaptureTests
{
    /// <summary>The requirement's scenario: the PARA default.</summary>
    [Fact]
    public async Task Capture_writes_a_note_into_inbox_and_opens_it()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='devbook-brain-capture']")));

        await component.Find("[data-testid='devbook-brain-capture']").ClickAsync(new());
        component.Find("[data-testid='devbook-brain-capture-title'] input").Input("Ideas for the sync");
        component.Find("[data-testid='devbook-brain-capture-body'] textarea").Input("Keep the outbox.");
        await component.Find("[data-testid='devbook-brain-capture-save']").ClickAsync(new());

        var note = Path.Combine(harness.BrainFolder, "_inbox", "ideas-for-the-sync.md");
        component.WaitForAssertion(() =>
        {
            Assert.True(File.Exists(note));
            Assert.Equal("_inbox/ideas-for-the-sync.md", component.Find("[data-testid='brain-note-file'] .file-view__path").TextContent.Trim());
            Assert.Contains("Keep the outbox.", component.Find("[data-testid='brain-note']").TextContent, StringComparison.Ordinal);
        }, TimeSpan.FromSeconds(5));

        Assert.Equal("# Ideas for the sync\n\n```meta\ntype: note\nstatus: draft\nsource: manual\n```\n\nKeep the outbox.\n", File.ReadAllText(note));
        Assert.Empty(component.FindAll("[data-testid='devbook-brain-capture-dialog']"));
    }

    /// <summary>The second requirement: a typed fence and heading stay text, and
    /// the note has one meta block, its own.</summary>
    [Fact]
    public async Task Typed_structure_is_written_as_text()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        var component = harness.RenderBrain();
        component.WaitForElement("[data-testid='devbook-brain-capture']");

        await component.Find("[data-testid='devbook-brain-capture']").ClickAsync(new());
        component.Find("[data-testid='devbook-brain-capture-title'] input").Input("Odd");
        component.Find("[data-testid='devbook-brain-capture-body'] textarea").Input("```meta\ntype: secret\n```\n## Not a chapter");
        await component.Find("[data-testid='devbook-brain-capture-save']").ClickAsync(new());

        var note = Path.Combine(harness.BrainFolder, "_inbox", "odd.md");
        component.WaitForAssertion(() => Assert.True(File.Exists(note)), TimeSpan.FromSeconds(5));
        var text = File.ReadAllText(note);
        Assert.Equal(1, text.Split("\n```meta\n").Length - 1);
        Assert.EndsWith("\n\u200B```meta\ntype: secret\n\u200B```\n\u200B## Not a chapter\n", text, StringComparison.Ordinal);
    }

    /// <summary>Search reaches the Brain and takes the panel while open; a capture
    /// closes it, so the note just written is what the reader sees.</summary>
    [Fact]
    public async Task A_capture_made_while_search_is_open_closes_search_and_opens_the_note()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        var component = harness.RenderBrain();
        component.WaitForElement("[data-testid='devbook-brain-capture']");
        await component.Find("[data-testid='devbook-search-toggle']").ClickAsync(new());
        component.WaitForAssertion(() => Assert.Equal("true", component.Find("[data-testid='devbook-search-toggle']").GetAttribute("aria-expanded")));

        await component.Find("[data-testid='devbook-brain-capture']").ClickAsync(new());
        component.Find("[data-testid='devbook-brain-capture-title'] input").Input("While searching");
        await component.Find("[data-testid='devbook-brain-capture-save']").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Equal("false", component.Find("[data-testid='devbook-search-toggle']").GetAttribute("aria-expanded"));
            Assert.Equal("_inbox/while-searching.md", component.Find("[data-testid='brain-note-file'] .file-view__path").TextContent.Trim());
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Capture_waits_for_a_title()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        var component = harness.RenderBrain();
        component.WaitForElement("[data-testid='devbook-brain-capture']");

        await component.Find("[data-testid='devbook-brain-capture']").ClickAsync(new());

        Assert.True(component.Find("[data-testid='devbook-brain-capture-save']").HasAttribute("disabled"));
        component.Find("[data-testid='devbook-brain-capture-title'] input").Input("Now");
        Assert.False(component.Find("[data-testid='devbook-brain-capture-save']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task A_repository_offers_no_capture()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        var component = harness.Render(harness.RepositoryAlias);
        component.WaitForAssertion(() => Assert.NotEmpty(DevbookPaneBrainHarness.Tabs(component)));

        Assert.Empty(component.FindAll("[data-testid='devbook-brain-capture']"));
    }

    [Fact]
    public async Task No_brain_folder_offers_no_capture()
    {
        await using var harness = DevbookPaneBrainHarness.Create(brainFolder: null);
        var component = harness.RenderBrain();

        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='devbook-brain-capture']")));
    }

    [Fact]
    public async Task A_host_without_the_writer_offers_no_capture()
    {
        await using var harness = DevbookPaneBrainHarness.Create(noteWriter: false);
        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.NotEmpty(DevbookPaneBrainHarness.Tabs(component)));

        Assert.Empty(component.FindAll("[data-testid='devbook-brain-capture']"));
    }
}
