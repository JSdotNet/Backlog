using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// One day's planned hours merged into an entry's blocks — the rule the Tasks detail panel
/// and the Calendar both write by (local ADR 0056 §2): the day replaced, removed for no
/// figure, the other days kept in day order, and nothing to write when the day already
/// reads so.
/// </summary>
public sealed class PlannedHoursMergeTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);

    [Fact]
    public void A_figure_for_a_new_day_adds_its_block_in_day_order()
    {
        var next = PlannedHoursMerge.WithDay([new PlannedHoursDto(Monday.AddDays(2), 1m)], Monday, 3m);

        Assert.Equal([new PlannedHoursDto(Monday, 3m), new PlannedHoursDto(Monday.AddDays(2), 1m)], next);
    }

    [Fact]
    public void A_figure_for_a_held_day_replaces_its_block()
    {
        var next = PlannedHoursMerge.WithDay([new PlannedHoursDto(Monday, 1m), new PlannedHoursDto(Monday.AddDays(2), 1m)], Monday, 4m);

        Assert.Equal([new PlannedHoursDto(Monday, 4m), new PlannedHoursDto(Monday.AddDays(2), 1m)], next);
    }

    [Fact]
    public void No_figure_removes_the_days_block_and_keeps_the_others()
    {
        var next = PlannedHoursMerge.WithDay([new PlannedHoursDto(Monday, 1m), new PlannedHoursDto(Monday.AddDays(2), 1m)], Monday, null);

        Assert.Equal([new PlannedHoursDto(Monday.AddDays(2), 1m)], next);
    }

    [Fact]
    public void The_same_figure_again_or_removing_a_day_not_held_is_nothing_to_write()
    {
        Assert.Null(PlannedHoursMerge.WithDay([new PlannedHoursDto(Monday, 2m)], Monday, 2m));
        Assert.Null(PlannedHoursMerge.WithDay([new PlannedHoursDto(Monday, 2m)], Monday.AddDays(1), null));
    }
}
