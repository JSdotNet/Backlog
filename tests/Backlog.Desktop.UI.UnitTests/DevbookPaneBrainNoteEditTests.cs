using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A Brain note is always editable (requirement <em>A Brain note is always
/// editable</em>): the file view offers Edit, the shared chapter editor takes the
/// body, and what is typed lands in the note's own file in the Brain folder.
/// </summary>
public sealed class DevbookPaneBrainNoteEditTests
{
    [Fact]
    public async Task Editing_a_note_writes_the_file_in_the_brain_folder()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Projects — `projects`\n");
        var note = harness.WriteNote("projects/plan.md", "# Plan\n\nOld body.\n");

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Contains("Old body.", component.Markup, StringComparison.Ordinal));

        await component.InvokeAsync(() => component.Find("[data-testid='brain-note-file-edit']").Click());
        var editor = component.WaitForElement("[data-testid='devbook-chapter-surface'] textarea");
        Assert.Equal("# Plan\n\nOld body.\n", editor.GetAttribute("value"));

        component.Find("[data-testid='devbook-chapter-surface'] textarea").Input("# Plan\n\nNew body.\n");
        component.Find("[data-testid='devbook-chapter-surface'] textarea").Blur();

        component.WaitForAssertion(
            () => Assert.Equal("# Plan\n\nNew body.\n", File.ReadAllText(note)),
            TimeSpan.FromSeconds(5));
        Assert.Single(component.FindComponents<DevbookChapterEditor>());
    }

    /// <summary>
    /// The setting moved to a copy of the Brain holding the same note at the same
    /// path. The open note is read again from the new folder, and an edit lands
    /// there — never in the folder the setting no longer names.
    /// </summary>
    [Fact]
    public async Task A_brain_moved_to_another_folder_reads_and_writes_the_note_there()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        const string Conventions = "## Areas\n\n- Projects — `projects`\n";
        harness.WriteNote(BrainConventions.FileName, Conventions);
        var oldNote = harness.WriteNote("projects/plan.md", "# Plan\n\nIn the old folder.\n");

        var moved = Path.Combine(harness.Root, "OneDrive", "Brain");
        Directory.CreateDirectory(Path.Combine(moved, "projects"));
        File.WriteAllText(Path.Combine(moved, BrainConventions.FileName), Conventions);
        var newNote = Path.Combine(moved, "projects", "plan.md");
        File.WriteAllText(newNote, "# Plan\n\nIn the new folder.\n");

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Contains("In the old folder.", component.Markup, StringComparison.Ordinal));

        harness.Settings.TrySetBrainFolder(moved);
        await Task.Run(harness.Brain.NotifyContentChanged, TestContext.Current.CancellationToken);

        component.WaitForAssertion(() =>
        {
            Assert.Contains("In the new folder.", component.Find("[data-testid='brain-note']").TextContent, StringComparison.Ordinal);
            Assert.Equal(moved, component.Find("[data-testid='devbook-brain-path']").TextContent.Trim());
        });

        await component.InvokeAsync(() => component.Find("[data-testid='brain-note-file-edit']").Click());
        component.WaitForElement("[data-testid='devbook-chapter-surface'] textarea").Input("# Plan\n\nTyped after the move.\n");
        component.Find("[data-testid='devbook-chapter-surface'] textarea").Blur();

        component.WaitForAssertion(
            () => Assert.Equal("# Plan\n\nTyped after the move.\n", File.ReadAllText(newNote)),
            TimeSpan.FromSeconds(5));
        Assert.Equal("# Plan\n\nIn the old folder.\n", File.ReadAllText(oldNote));
    }

    /// <summary>OneDrive brought down an edit made on another machine, and the
    /// watch said so: the open note shows the new text without being reselected.</summary>
    [Fact]
    public async Task A_note_changed_on_disk_is_read_again_when_the_brain_says_so()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Projects — `projects`\n");
        harness.WriteNote("projects/plan.md", "# Plan\n\nBefore the sync.\n");

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Contains("Before the sync.", component.Markup, StringComparison.Ordinal));

        harness.WriteNote("projects/plan.md", "# Plan\n\nAfter the sync.\n");
        await Task.Run(harness.Brain.NotifyContentChanged, TestContext.Current.CancellationToken);

        component.WaitForAssertion(() =>
            Assert.Contains("After the sync.", component.Find("[data-testid='brain-note']").TextContent, StringComparison.Ordinal));
    }
}
