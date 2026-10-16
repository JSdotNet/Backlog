using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A task carried over into today with Clear My Day overnight off
/// (.devbook/arc42/adr/0032, Verification 7): it is in My Day, but its agenda slot
/// belonged to the day it was picked for, so today it has none — it sits in Anytime
/// today, with no block on the card.
/// </summary>
public sealed class CarriedTaskTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 7);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    [Fact]
    public void A_task_picked_for_today_keeps_its_slot_today()
    {
        var row = Row("Standup", Today, "10:00", 30);

        Assert.Equal(new AgendaTime(new TimeOnly(10, 0), 30), row.AgendaTimeOn(Today));
    }

    [Fact]
    public void A_carried_task_has_no_slot_today_and_keeps_it_on_its_own_day()
    {
        var row = Row("Standup", Yesterday, "10:00", 30);

        Assert.Null(row.AgendaTimeOn(Today));
        Assert.Equal(new AgendaTime(new TimeOnly(10, 0), 30), row.AgendaTimeOn(Yesterday));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_carried_open_task_is_in_My_Day_only_with_the_switch_off(bool clearsOvernight, bool expected)
    {
        Assert.Equal(expected, Row("Carried", Yesterday).IsInMyDay(Today, clearsOvernight));
    }

    [Fact]
    public void A_carried_task_ticked_today_is_not_in_My_Days_open_list()
    {
        var row = Row("Carried", Yesterday, completedOn: Today, status: "done");

        Assert.False(row.IsInMyDay(Today, clearsOvernight: false));
    }

    [Fact]
    public void A_carried_timed_task_is_planned_as_anytime_not_on_the_card()
    {
        var carried = Row("Carried standup", Yesterday, "10:00", 60);
        var today = Row("Review", Today, "11:00", 30);

        var plan = TodayPlan.For([carried, today], Today, new TimeOnly(10, 15));

        Assert.Equal("Review", plan.Card!.Task.Task.Title);
        Assert.False(plan.Card.IsNow);
        Assert.Empty(plan.Agenda);
        Assert.Equal(["Carried standup"], plan.Anytime.Select(row => row.Task.Title));
    }

    [Fact]
    public void A_carried_task_ticked_today_is_under_done_today()
    {
        var carried = Row("Carried", Yesterday, "10:00", 60, completedOn: Today, status: "done");

        var plan = TodayPlan.For([carried], Today, new TimeOnly(9, 0));

        Assert.Equal(["Carried"], plan.Done.Select(row => row.Task.Title));
        Assert.Null(plan.Card);
    }

    private static TaskViewRow Row(string title, DateOnly pickedFor, string? at = null, int? minutes = null, DateOnly? completedOn = null, string status = "ready")
    {
        var change = TestTasks.Task(title, Stamp, inMyDayOn: pickedFor, status: status);
        var task = change.Task with { AgendaAt = at, AgendaMinutes = minutes, CompletedOn = completedOn };
        return new TaskViewRow(change.Id, change.UpdatedAt, null, 0, task);
    }
}
