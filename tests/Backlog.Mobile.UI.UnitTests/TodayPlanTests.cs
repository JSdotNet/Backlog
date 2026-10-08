using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The Today screen's grouping on its own: which task the card shows, and where
/// every other task picked for today lands
/// (<c>.devbook/domain/tasks/features.md#my-day</c>).
/// </summary>
public sealed class TodayPlanTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 7);

    [Fact]
    public void Overlapping_blocks_put_the_earlier_start_on_the_card()
    {
        var early = Row("Early", "10:00", 60);
        var late = Row("Late", "10:15", 30);

        var plan = TodayPlan.For([late, early], new TimeOnly(10, 20));

        Assert.Equal("Early", plan.Card!.Task.Task.Title);
        Assert.True(plan.Card.IsNow);
        Assert.Equal(["Late"], plan.Agenda.Select(row => row.Task.Title));
    }

    [Fact]
    public void A_block_is_current_from_its_start_and_over_at_its_end()
    {
        var block = new AgendaTime(new TimeOnly(10, 45), 45);

        Assert.False(TodayPlan.Contains(block, new TimeOnly(10, 44)));
        Assert.True(TodayPlan.Contains(block, new TimeOnly(10, 45)));
        Assert.True(TodayPlan.Contains(block, new TimeOnly(11, 29)));
        Assert.False(TodayPlan.Contains(block, new TimeOnly(11, 30)));
    }

    [Fact]
    public void A_block_running_past_midnight_is_current_until_the_day_ends()
    {
        var late = new AgendaTime(new TimeOnly(23, 30), 60);

        Assert.True(TodayPlan.Contains(late, new TimeOnly(23, 59)));
        Assert.False(TodayPlan.Contains(late, new TimeOnly(0, 15)));
    }

    [Fact]
    public void A_ticked_task_is_done_whatever_its_status_and_an_unticked_done_task_is_open()
    {
        var ticked = Row("Ticked", completedOn: Today, status: "in_progress");
        var unticked = Row("Unticked", status: "done");

        var plan = TodayPlan.For([ticked, unticked], new TimeOnly(9, 0));

        Assert.Equal(["Ticked"], plan.Done.Select(row => row.Task.Title));
        Assert.Equal(["Unticked"], plan.Anytime.Select(row => row.Task.Title));
        Assert.Equal(2, plan.Total);
    }

    private static TaskViewRow Row(string title, string? at = null, int? minutes = null, DateOnly? completedOn = null, string status = "ready")
    {
        var change = TestTasks.Task(title, Stamp, inMyDayOn: Today, status: status);
        var task = change.Task with { AgendaAt = at, AgendaMinutes = minutes, CompletedOn = completedOn };
        return new TaskViewRow(change.Id, change.UpdatedAt, null, 0, task);
    }
}
