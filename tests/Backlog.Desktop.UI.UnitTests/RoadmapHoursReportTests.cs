using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.UI;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The hours report a begun head's hours open (local ADR 0019, §6): the week the pressed
/// date falls in, each working day with its total and its stretches, the pressed day
/// marked, and a step to the week before. A host without the report, and a read that
/// fails, say so in the dialog.
/// </summary>
public sealed class RoadmapHoursReportTests
{
    private static readonly DateOnly Monday28September = new(2026, 9, 28);
    private static readonly DateOnly Wednesday30September = new(2026, 9, 30);
    private static readonly DateOnly Today = new(2026, 10, 5);

    private static readonly TimeSpan Summer = TimeSpan.FromHours(2);

    [Fact]
    public void The_report_shows_the_pressed_dates_week_with_its_stretches()
    {
        var report = new StubReport();
        using var context = Context(report);

        var dialog = Render(context, Wednesday30September);

        dialog.WaitForAssertion(() => Assert.NotEmpty(dialog.FindAll("[data-testid='roadmap-hours-stretch']")));
        Assert.Equal([(Monday28September, Monday28September.AddDays(6))], report.Asked);
        Assert.Contains("W40", dialog.Find("[data-testid='roadmap-hours'] .modal__title").TextContent, StringComparison.Ordinal);
        Assert.Equal("3.5h worked this week", dialog.Find("[data-testid='roadmap-hours-total']").TextContent.Trim());

        var wednesday = dialog.Find("[data-testid='roadmap-hours-day-2026-09-30']");
        Assert.Contains("roadmap-hours__day--focused", wednesday.ClassList);
        Assert.Contains("3.5h", wednesday.QuerySelector(".roadmap-hours__day-total")!.TextContent, StringComparison.Ordinal);

        var stretch = wednesday.QuerySelector("[data-testid='roadmap-hours-stretch']")!;
        Assert.Equal("22:30–02:00", stretch.QuerySelector(".roadmap-hours__when")!.TextContent.Trim());
        Assert.Equal("3:30", stretch.QuerySelector(".roadmap-hours__length")!.TextContent.Trim());
        Assert.Equal("4 prompts", stretch.QuerySelector(".roadmap-hours__turns")!.TextContent.Trim());
        Assert.DoesNotContain("Roadmap heads", stretch.TextContent, StringComparison.Ordinal);

        Assert.Equal("No stretches.", dialog.Find("[data-testid='roadmap-hours-day-2026-09-28'] .roadmap-hours__none").TextContent.Trim());
    }

    [Fact]
    public void Previous_week_reads_the_week_before_and_next_week_stops_at_today()
    {
        var report = new StubReport();
        using var context = Context(report);
        var dialog = Render(context, Wednesday30September);
        dialog.WaitForAssertion(() => Assert.NotEmpty(dialog.FindAll("[data-testid='roadmap-hours-days']")));

        dialog.Find("[data-testid='roadmap-hours-previous']").Click();

        dialog.WaitForAssertion(() => Assert.Equal(2, report.Asked.Count));
        Assert.Equal((Monday28September.AddDays(-7), Monday28September.AddDays(-1)), report.Asked[1]);

        var thisWeek = Render(context, Today);
        Assert.True(thisWeek.Find("[data-testid='roadmap-hours-next']").HasAttribute("disabled"));
    }

    [Fact]
    public void A_host_without_the_report_says_the_hours_cannot_be_read()
    {
        using var context = Context(report: null);

        var dialog = Render(context, Wednesday30September);

        dialog.WaitForAssertion(() =>
            Assert.Contains("cannot be read", dialog.Find("[data-testid='roadmap-hours-error']").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_read_says_so_in_the_dialog()
    {
        using var context = Context(new StubReport { Failure = new IOException("transcript locked") });

        var dialog = Render(context, Wednesday30September);

        dialog.WaitForAssertion(() =>
            Assert.Contains("could not be read", dialog.Find("[data-testid='roadmap-hours-error']").TextContent, StringComparison.Ordinal));
    }

    private static BunitContext Context(IRoadmapHoursReport? report)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        if (report is not null) context.Services.AddSingleton(report);
        return context;
    }

    private static IRenderedComponent<RoadmapHoursReport> Render(BunitContext context, DateOnly date) =>
        context.Render<RoadmapHoursReport>(parameters => parameters
            .Add(dialog => dialog.Open, true)
            .Add(dialog => dialog.Date, date)
            .Add(dialog => dialog.Today, Today));

    private sealed class StubReport : IRoadmapHoursReport
    {
        public List<(DateOnly From, DateOnly Through)> Asked { get; } = [];

        public Exception? Failure { get; init; }

        public Task<IReadOnlyList<RoadmapWorkedDayDto>?> ReadDaysAsync(DateOnly from, DateOnly through, CancellationToken cancellationToken = default)
        {
            Asked.Add((from, through));
            if (Failure is not null) return Task.FromException<IReadOnlyList<RoadmapWorkedDayDto>?>(Failure);

            var days = new List<RoadmapWorkedDayDto>();
            for (var date = from; date <= through && date <= Today; date = date.AddDays(1))
            {
                days.Add(date == Wednesday30September
                    ? new RoadmapWorkedDayDto(date, TimeSpan.FromHours(3.5),
                    [
                        new RoadmapWorkedStretchDto(
                            new DateTimeOffset(2026, 9, 30, 22, 30, 0, Summer),
                            new DateTimeOffset(2026, 10, 1, 2, 0, 0, Summer),
                            4,
                            [new("Roadmap heads", "JSdotNet/Backlog", "tower"), new("Hours report", null, "tower")])
                    ])
                    : new RoadmapWorkedDayDto(date, TimeSpan.Zero, []));
            }

            return Task.FromResult<IReadOnlyList<RoadmapWorkedDayDto>?>(days);
        }
    }
}
