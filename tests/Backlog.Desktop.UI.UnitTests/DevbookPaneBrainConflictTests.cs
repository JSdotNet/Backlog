using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// OneDrive's conflict copies (local ADR 0026 §5): when the Brain holds
/// <c>plan-LAPTOP.md</c> beside <c>plan.md</c>, the pane says so — passively, as a
/// status — names both files, and changes neither.
/// </summary>
public sealed class DevbookPaneBrainConflictTests
{
    [Fact]
    public async Task A_conflict_copy_is_named_beside_its_original_and_neither_file_changes()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        var original = harness.WriteNote("projects/plan.md", "# Plan\n\nThe one on this machine.\n");
        var copy = harness.WriteNote("projects/plan-LAPTOP.md", "# Plan\n\nThe one OneDrive kept.\n");
        var before = Snapshot(original, copy);

        var component = harness.RenderBrain();

        var warning = component.WaitForElement("[data-testid='devbook-brain-conflict']");
        Assert.Equal("status", warning.GetAttribute("role"));
        Assert.Contains("projects/plan.md", warning.TextContent, StringComparison.Ordinal);
        Assert.Contains("projects/plan-LAPTOP.md", warning.TextContent, StringComparison.Ordinal);

        Assert.Equal(before, Snapshot(original, copy));
    }

    [Fact]
    public async Task No_pair_draws_no_warning()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Projects — `projects`\n");
        harness.WriteNote("projects/plan.md", "# Plan\n\nBody.\n");
        harness.WriteNote("projects/plan-2.md", "# Plan, part two\n");

        var brain = CountingBrainFolderSource.Install(harness);

        var component = harness.RenderBrain();

        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll(".devbook-menu__item--file").Count));

        // The walk runs off the UI thread and can land after the menu does, and an
        // answer of nothing draws nothing. Asserted once the pane has read the
        // answer and the turn it read it in is over, so the absence is the walk's
        // and not the walk still being out.
        Assert.True(brain.AnswerRead.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        await component.InvokeAsync(() => { });
        Assert.Empty(component.FindAll("[data-testid='devbook-brain-conflict']"));
    }

    /// <summary>
    /// The Brain is read when the pane crosses onto it and when the Brain says it
    /// changed — not on every parameter set, because the read walks the whole
    /// folder. A pair that appears between two parameter sets is not looked for
    /// until the Brain announces a change, and then it is named.
    /// </summary>
    [Fact]
    public async Task A_parameter_set_does_not_read_the_brain_again_but_a_brain_change_does()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote("_inbox/plan.md", "# Plan\n\nBody.\n");
        var brain = CountingBrainFolderSource.Install(harness);

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".devbook-menu__item--file")));

        // The first conflict walk runs off the UI thread; the folder is changed
        // only once its answer has landed, so the walk cannot see the change.
        Assert.True(brain.AnswerRead.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        await component.InvokeAsync(() => { });

        harness.WriteNote("_inbox/plan-LAPTOP.md", "# Plan\n\nThe one OneDrive kept.\n");
        component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, string.Empty));
        Assert.Empty(component.FindAll("[data-testid='devbook-brain-conflict']"));

        await Task.Run(harness.Brain.NotifyContentChanged, TestContext.Current.CancellationToken);

        var warning = component.WaitForElement("[data-testid='devbook-brain-conflict']");
        Assert.Contains("_inbox/plan-LAPTOP.md", warning.TextContent, StringComparison.Ordinal);
    }

    /// <summary>A Brain folder that goes away under the pane — a drive unmounted,
    /// the folder renamed — raises no change, so the next parameter set notices it
    /// is gone rather than keeping its tabs up over nothing.</summary>
    [Fact]
    public async Task A_brain_folder_that_vanished_is_noticed_on_the_next_parameter_set()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote("_inbox/plan.md", "# Plan\n\nBody.\n");
        var brain = CountingBrainFolderSource.Install(harness);

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".devbook-menu__item--file")));

        // The first conflict walk runs off the UI thread; the folder is changed
        // only once its answer has landed, so the walk cannot see the change.
        Assert.True(brain.AnswerRead.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        await component.InvokeAsync(() => { });

        // The pane opens the note on its own, reading it off the renderer's thread,
        // and a note being read cannot be deleted on Windows: the delete waits for
        // that read to land, and gives way to any other handle on a file in the
        // folder — "plan.md is being used by another process" under load.
        component.WaitForAssertion(() => Assert.Contains("Body.", component.Find("[data-testid='brain-note']").TextContent, StringComparison.Ordinal));
        harness.DeleteBrainFolder();
        component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, string.Empty));

        var message = component.WaitForElement("[data-testid='devbook-brain-unavailable']");
        Assert.Contains("was not found", message.TextContent, StringComparison.Ordinal);
    }

    private static string Snapshot(params string[] paths) => string.Join(
        "|",
        paths.Select(path => $"{Convert.ToBase64String(File.ReadAllBytes(path))}@{File.GetLastWriteTimeUtc(path).Ticks}"));
}
