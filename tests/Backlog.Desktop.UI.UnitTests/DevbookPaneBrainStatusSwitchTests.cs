using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A Brain note's chapter statuses follow the devbook status switch, as every
/// devbook file's do; its Edit control follows nothing, because a note is always
/// editable.
/// </summary>
public sealed class DevbookPaneBrainStatusSwitchTests
{
    private const string Note = "# Plan\n\nThe body.\n\n## Goals\n\n```meta\nstatus: draft\n```\n\nWhat it is for.\n";

    [Fact]
    public async Task A_notes_chapter_status_is_hidden_while_devbook_status_is_off_and_edit_stays()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Projects — `projects`\n");
        harness.WriteNote("projects/plan.md", Note);

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Contains("What it is for.", component.Markup, StringComparison.Ordinal));

        Assert.Contains("Goals", component.Markup, StringComparison.Ordinal);
        Assert.Empty(component.FindAll("[data-testid='brain-note-file'] .badge--status"));
        Assert.Single(component.FindAll("[data-testid='brain-note-file-edit']"));
    }

    [Fact]
    public async Task Switching_devbook_status_on_shows_a_notes_chapter_status()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Projects — `projects`\n");
        harness.WriteNote("projects/plan.md", Note);

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Contains("What it is for.", component.Markup, StringComparison.Ordinal));

        harness.Context.Services.GetRequiredService<IAppFeatureSettings>().SetEnabled(DevbookFeatures.Status, true);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='brain-note-file'] .badge--status")));
    }
}
