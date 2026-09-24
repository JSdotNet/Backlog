using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Tasks.Services;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// An open band reads the backlog again when a task it draws from is written. Found
/// in the desktop harness while validating the combined import: a step marked done
/// while the band was on screen stayed drawn the old way until the page was
/// reloaded, because the band reloaded on plan writes and a task write is not one.
/// </summary>
public sealed class RoadmapBandReloadTests : RoadmapBandHarness
{
    [Fact]
    public async Task A_task_change_signal_redraws_an_open_band_from_what_is_stored_now()
    {
        using var context = Context();
        var band = await PlannedAsync(context);
        Assert.Empty(band.FindAll("[data-testid=\"roadmap-shelf\"]"));

        // Tasks written behind the band's back, and no plan write to hear: a
        // task-level import with no `plan` entry touches only Tasks.
        var imported = await TasksTestHost.EntriesFor(Settings)
            .ImportPlanAsync("# Later\n`prompt` `+later` `effort:2`\n");
        Assert.True(imported.IsSuccess);
        Assert.Empty(band.FindAll("[data-testid=\"roadmap-shelf\"]"));

        WorkChanges.Raise();

        band.WaitForAssertion(() =>
            Assert.NotEmpty(band.FindAll("[data-testid=\"roadmap-shelf-row-later\"]")));
    }

    [Fact]
    public void A_local_task_write_is_heard_as_a_roadmap_change()
    {
        var tasks = new TaskChangeSignal();
        using var changes = new RoadmapWorkChanges(tasks);
        var heard = 0;
        changes.Changed += () => heard++;

        tasks.Raise();

        Assert.Equal(1, heard);
    }
}
