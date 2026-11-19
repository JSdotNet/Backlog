using System.Globalization;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Planned hours in the Tasks detail panel, over a real store in a temporary folder: a
/// block made, changed and removed from the panel's Planned hours section, each written
/// to the entry as its own field and read back from the store, and a figure the module
/// refuses. The Calendar's own hours are <see cref="CalendarSurfaceTests"/>'.
/// <para>
/// Dates are relative to the real today, because the pane reads its own clock.
/// </para>
/// </summary>
public sealed class TasksPanePlannedHoursTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Another day of today's month.</summary>
    private static DateOnly Day => Today.Day <= 15 ? Today.AddDays(3) : Today.AddDays(-3);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<IReadOnlyList<PlannedHoursDto>> StoredAsync(TasksPaneHost host, EntryRow row)
    {
        await host.State.ReloadFromStoreAsync();
        return Assert.Single(host.State.Rows, candidate => candidate.Id == row.Id).PlannedHours;
    }

    [Fact]
    public async Task The_detail_panels_planned_hours_add_change_and_remove_a_block()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Refactor sync\n`task` `!ready`\n");
        await host.OpenAsync(row);
        var pane = host.Render();

        var section = pane.Find("[data-testid='entry-planned-hours']");
        Assert.Contains("Planned hours", section.TextContent, StringComparison.Ordinal);

        await pane.InputAsync("[data-testid='entry-planned-hours-editor-add-day'] input", Iso(Day));
        await pane.InputAsync("[data-testid='entry-planned-hours-editor-add-hours'] input", "4");
        await pane.ClickAsync("[data-testid='entry-planned-hours-editor-add']");

        pane.WaitForAssertion(() => Assert.Equal(
            "4",
            pane.Find($"[data-testid='entry-planned-hours-editor-hours-{Iso(Day)}'] input").GetAttribute("value")));
        Assert.Equal([new PlannedHoursDto(Day, 4m)], row.PlannedHours);

        await pane.ChangeAsync($"[data-testid='entry-planned-hours-editor-hours-{Iso(Day)}'] input", "1.5");
        pane.WaitForAssertion(() => Assert.Equal([new PlannedHoursDto(Day, 1.5m)], row.PlannedHours));

        await pane.ClickAsync($"[data-testid='entry-planned-hours-editor-remove-{Iso(Day)}']");
        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='entry-planned-hours-editor-row']")));
        Assert.Empty(await StoredAsync(host, row));
    }

    /// <summary>A figure the module refuses is said on the entry and the band reads Error;
    /// nothing is written.</summary>
    [Fact]
    public async Task A_refused_figure_is_said_and_nothing_is_written()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Refactor sync\n`task` `!ready`\n");

        await host.State.SetPlannedHoursAsync(row, Day, 30m);

        Assert.NotNull(row.PlannedHoursError);
        Assert.Contains("at most 24", row.PlannedHoursError, StringComparison.Ordinal);
        Assert.Empty(await StoredAsync(host, row));
    }
}
