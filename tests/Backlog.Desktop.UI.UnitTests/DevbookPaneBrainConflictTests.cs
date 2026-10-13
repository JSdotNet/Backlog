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

        var component = harness.RenderBrain();

        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll(".devbook-menu__item--file").Count));
        Assert.Empty(component.FindAll("[data-testid='devbook-brain-conflict']"));
    }

    private static string Snapshot(params string[] paths) => string.Join(
        "|",
        paths.Select(path => $"{Convert.ToBase64String(File.ReadAllBytes(path))}@{File.GetLastWriteTimeUtc(path).Ticks}"));
}
