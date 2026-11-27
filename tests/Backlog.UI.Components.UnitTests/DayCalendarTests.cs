using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A calendar given no month and no today takes both from the host's clock (app-defaults
/// ADR 0005), so a test pins the date with a <see cref="FakeTimeProvider"/>; with none
/// registered, as in Storybook, it still renders from the system clock.
/// </summary>
public sealed class DayCalendarTests
{
    [Fact]
    public void Without_a_month_or_today_the_calendar_opens_on_the_injected_clock_s_date()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(
            new FakeTimeProvider(new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero)));

        var calendar = context.Render<DayCalendar>(parameters => parameters.Add(p => p.TestId, "cal"));

        Assert.Contains("day-calendar__day--today", calendar.Find("[data-testid='cal-2026-03-04']").ClassList);
        Assert.Single(calendar.FindAll(".day-calendar__day--today"));
    }

    [Fact]
    public void A_host_with_no_clock_registered_still_renders_the_calendar()
    {
        using var context = new BunitContext();

        var calendar = context.Render<DayCalendar>(parameters => parameters.Add(p => p.TestId, "cal"));

        Assert.Single(calendar.FindAll(".day-calendar__day--today"));
    }
}
