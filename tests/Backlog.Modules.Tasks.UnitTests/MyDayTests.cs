using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// The one My Day rule both heads read (local ADR 0032, §5), held to the table the
/// owner settled: a task is in today's My Day while its stamp is today; with Clear
/// My Day overnight off, an unfinished task with an earlier stamp stays until it is
/// finished, and one finished today stays ticked; a later stamp and no stamp are
/// never in.
/// </summary>
public sealed class MyDayTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly DateOnly Yesterday = new(2026, 10, 14);
    private static readonly DateOnly Tomorrow = new(2026, 10, 16);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_task_stamped_today_is_in_whether_open_or_done(bool clearsOvernight)
    {
        Assert.True(MyDay.Includes(Today, completedOn: null, Today, clearsOvernight));
        Assert.True(MyDay.Includes(Today, completedOn: Today, Today, clearsOvernight));
    }

    [Fact]
    public void An_open_task_stamped_yesterday_is_out_while_my_day_clears_overnight()
    {
        Assert.False(MyDay.Includes(Yesterday, completedOn: null, Today, clearsOvernight: true));
    }

    [Fact]
    public void An_open_task_stamped_yesterday_carries_over_while_my_day_does_not_clear()
    {
        Assert.True(MyDay.Includes(Yesterday, completedOn: null, Today, clearsOvernight: false));
    }

    [Fact]
    public void A_carried_task_finished_yesterday_is_out()
    {
        Assert.False(MyDay.Includes(Yesterday, completedOn: Yesterday, Today, clearsOvernight: false));
    }

    [Fact]
    public void A_carried_task_finished_today_stays_ticked()
    {
        Assert.True(MyDay.Includes(Yesterday, completedOn: Today, Today, clearsOvernight: false));
    }

    [Fact]
    public void A_task_carried_for_a_week_is_still_in_while_unfinished()
    {
        Assert.True(MyDay.Includes(Today.AddDays(-7), completedOn: null, Today, clearsOvernight: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_task_stamped_for_a_later_day_is_never_in_today(bool clearsOvernight)
    {
        Assert.False(MyDay.Includes(Tomorrow, completedOn: null, Today, clearsOvernight));
        Assert.False(MyDay.Includes(Tomorrow, completedOn: Today, Today, clearsOvernight));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_task_with_no_stamp_is_never_in(bool clearsOvernight)
    {
        Assert.False(MyDay.Includes(null, completedOn: null, Today, clearsOvernight));
        Assert.False(MyDay.Includes(null, completedOn: Today, Today, clearsOvernight));
    }

    /// <summary>The agenda slot belongs to the stamp's day (ADR 0032, §5): a carried
    /// task is in My Day and still has no slot.</summary>
    [Fact]
    public void Only_a_task_stamped_today_is_planned_for_today()
    {
        Assert.True(MyDay.IsPlannedFor(Today, Today));
        Assert.False(MyDay.IsPlannedFor(Yesterday, Today));
        Assert.False(MyDay.IsPlannedFor(Tomorrow, Today));
        Assert.False(MyDay.IsPlannedFor(null, Today));
    }
}
