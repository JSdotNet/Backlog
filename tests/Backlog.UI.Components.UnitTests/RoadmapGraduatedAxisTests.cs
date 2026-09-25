using Microsoft.JSInterop;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The graduated axis: four weeks from this one, months for about three
/// after, quarters beyond. Pinned as columns and distances, because a ruler that
/// changes scale is only honest if every column meets the next exactly and a
/// week is wider near today than a year out.
/// </summary>
public sealed class RoadmapGraduatedAxisTests
{
    // Friday 25 September 2026. Its week starts on Monday the 21st; four weeks on
    // is Monday 19 October, where the months take over — the rest of October as a
    // column of its own — and the first quarter start three months after that is
    // 1 April 2027.
    private static readonly DateOnly Today = new(2026, 9, 25);

    private static RoadmapWindow Window(params DateOnly[] dates) =>
        RoadmapWindow.Graduated(dates, Today, DayOfWeek.Monday);

    [Fact]
    public void Weeks_ThenMonths_ThenQuarters_EachTierStartingOnABoundaryOfTheNext()
    {
        var window = Window(new DateOnly(2026, 10, 1), new DateOnly(2027, 8, 15));

        Assert.True(window.IsGraduated);
        Assert.Equal(new DateOnly(2026, 9, 21), window.Start);
        Assert.Equal(new DateOnly(2027, 9, 30), window.End);

        var weeks = window.Columns.Where(column => column.Scale == RoadmapColumnScale.Week).ToList();
        var months = window.Columns.Where(column => column.Scale == RoadmapColumnScale.Month).ToList();
        var quarters = window.Columns.Where(column => column.Scale == RoadmapColumnScale.Quarter).ToList();

        Assert.Equal(RoadmapWindow.GraduatedWeeks, weeks.Count);
        Assert.All(weeks, week => Assert.Equal(7, week.TotalDays));
        Assert.Equal(new DateOnly(2026, 10, 18), weeks[^1].End);
        Assert.Equal(new DateOnly(2026, 10, 19), months[0].Start);
        Assert.Equal(new DateOnly(2026, 10, 31), months[0].End);
        Assert.Equal([10, 11, 12, 1, 2, 3], months.Select(month => month.Start.Month));
        Assert.All(months.Skip(1), month => Assert.Equal(1, month.Start.Day));
        Assert.Equal(["Q2 2027", "Q3 2027"], quarters.Select(quarter => quarter.LongLabel));

        // In order, and with no day left out or ruled twice.
        Assert.Equal(
            [RoadmapColumnScale.Week, RoadmapColumnScale.Month, RoadmapColumnScale.Quarter],
            window.Columns.Select(column => column.Scale).Distinct());
        for (var index = 1; index < window.Columns.Count; index++)
        {
            Assert.Equal(window.Columns[index - 1].End.AddDays(1), window.Columns[index].Start);
        }
    }

    [Fact]
    public void AShortPlan_StillShowsTheWholeWeeklyAndMonthlyHorizon()
    {
        var window = Window(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 30));

        Assert.Equal(new DateOnly(2027, 3, 31), window.End);
        Assert.DoesNotContain(window.Columns, column => column.Scale == RoadmapColumnScale.Quarter);
    }

    [Fact]
    public void WorkBeforeThisWeek_IsRuledInMonths_TheLastClippedToMeetTheFirstWeek()
    {
        var window = Window(new DateOnly(2026, 8, 10), new DateOnly(2026, 11, 1));

        Assert.Equal(new DateOnly(2026, 8, 1), window.Start);
        Assert.Equal(RoadmapColumnScale.Month, window.Columns[0].Scale);
        Assert.Equal(new DateOnly(2026, 9, 20), window.Columns[1].End);
        Assert.Equal(new DateOnly(2026, 9, 21), window.Columns[2].Start);
        Assert.Equal(RoadmapColumnScale.Week, window.Columns[2].Scale);
    }

    [Fact]
    public void AWeekHead_IsItsWeekNumber_AndNamesTheMonthOnlyWhereTheMonthChanges()
    {
        var weeks = Window(new DateOnly(2026, 10, 1)).Columns
            .Where(column => column.Scale == RoadmapColumnScale.Week)
            .ToList();

        // Monday 21 September 2026 opens ISO week 39.
        Assert.Equal("W39", weeks[0].Label);
        Assert.Equal("W40", weeks[1].Label);
        Assert.NotNull(weeks[0].Caption);
        Assert.Null(weeks[1].Caption);
        Assert.NotNull(weeks.Single(week => week.Start == new DateOnly(2026, 10, 5)).Caption);
    }

    [Fact]
    public void WeekNumbers_RollOverAtTheYearEnd_TheIsoWay()
    {
        // 28 December 2026 opens ISO week 53; 4 January 2027 opens week 1.
        Assert.Equal(53, RoadmapWindow.WeekNumber(new DateOnly(2026, 12, 28), DayOfWeek.Monday));
        Assert.Equal(1, RoadmapWindow.WeekNumber(new DateOnly(2027, 1, 4), DayOfWeek.Monday));
    }

    [Fact]
    public void ADay_IsWidestInTheWeeks_ThenTheMonths_ThenTheQuarters()
    {
        var geometry = new RoadmapGeometry(Window(new DateOnly(2027, 8, 15)));

        var week = geometry.WeekWidthAt(new DateOnly(2026, 10, 5));
        var month = geometry.WeekWidthAt(new DateOnly(2027, 2, 1));
        var quarter = geometry.WeekWidthAt(new DateOnly(2027, 5, 3));

        Assert.Equal(3, week, 6);
        Assert.True(week > month && month > quarter, $"{week} > {month} > {quarter}");
    }

    [Fact]
    public void EveryWholeWeek_Month_AndQuarter_IsDrawnTheSameWidth()
    {
        var window = Window(new DateOnly(2027, 12, 15));
        var geometry = new RoadmapGeometry(window);

        foreach (var column in window.Columns.Where(column => column.TotalDays == column.NominalDays))
        {
            Assert.Equal(geometry.ColumnWidthRem(column.Scale), geometry.WidthFor(column.Start, column.End), 6);
        }

        // February and March, 28 and 31 days, are one width.
        Assert.Contains(window.Columns, column => column.Start == new DateOnly(2027, 2, 1));

        // The rest of October after the fourth week is its share of a month.
        var october = window.Columns.First(column => column.Scale == RoadmapColumnScale.Month);
        Assert.Equal(13, october.TotalDays);
        Assert.Equal(geometry.ColumnWidthRem(RoadmapColumnScale.Month) * 13 / 31, geometry.WidthFor(october.Start, october.End), 6);
    }

    [Fact]
    public void ColumnsMeetEdgeToEdge_AndTheTrackEndsWhereTheLastColumnDoes()
    {
        var window = Window(new DateOnly(2027, 8, 15));
        var geometry = new RoadmapGeometry(window);

        Assert.Equal(0, geometry.XFor(window.Start));
        foreach (var column in window.Columns)
        {
            Assert.Equal(geometry.XFor(column.End.AddDays(1)), geometry.XFor(column.Start) + geometry.WidthFor(column.Start, column.End), 6);
        }

        Assert.Equal(geometry.XFor(window.End.AddDays(1)), geometry.TrackWidthRem, 6);

        // A bar spanning two tiers is as wide as its share of each.
        var across = geometry.WidthFor(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 25));
        Assert.Equal(
            geometry.ColumnWidthRem(RoadmapColumnScale.Week) + geometry.ColumnWidthRem(RoadmapColumnScale.Month) * 7 / 31,
            across,
            6);
    }

    [Fact]
    public void AGraduatedTimeline_RulesItsAxisInWeeks_AndNamesAGroupOnce()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<RoadmapTimeline>(parameters => parameters
            .Add(timeline => timeline.Groups,
            [
                new RoadmapGroup("backlog", "backlog",
                [
                    new RoadmapRow("backlog::Planned", string.Empty),
                    new RoadmapRow("backlog::Planned::2", string.Empty),
                    new RoadmapRow("backlog::platform", "platform")
                ])
            ])
            .Add(timeline => timeline.Bars,
            [
                new RoadmapBar("a", "backlog::Planned", "A", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 30)),
                new RoadmapBar("b", "backlog::Planned::2", "B", new DateOnly(2026, 10, 12), new DateOnly(2026, 11, 6)),
                new RoadmapBar("c", "backlog::platform", "C", new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 20))
            ])
            .Add(timeline => timeline.Today, Today)
            .Add(timeline => timeline.Graduated, true));

        Assert.Equal(4, view.FindAll(".roadmap-timeline__quarter--week").Count);
        Assert.Equal(6, view.FindAll(".roadmap-timeline__quarter--month").Count);

        // The rest of October, though under a fortnight, is still wide enough to be named.
        Assert.Equal(1, view.FindAll(".roadmap-timeline__quarter--month")[0].QuerySelectorAll(".roadmap-timeline__quarter-label").Length);
        Assert.Single(view.FindAll(".roadmap-timeline__rule--scale"));

        // One label column row per track row, the group named once, and the lane
        // named where it changes — never the unnamed lane, and never twice.
        Assert.Equal(3, view.FindAll(".roadmap-timeline__row-name").Count);
        Assert.Equal("backlog", Assert.Single(view.FindAll(".roadmap-timeline__group-name")).TextContent.Trim());
        Assert.Equal("platform", Assert.Single(view.FindAll(".roadmap-timeline__lane-name")).TextContent.Trim());

        // This week is marked in the head and shaded down the chart.
        var current = Assert.Single(view.FindAll(".roadmap-timeline__quarter--current"));
        Assert.Equal("W39", current.QuerySelector(".roadmap-timeline__quarter-label")!.TextContent.Trim());
        Assert.Equal("date", current.GetAttribute("aria-current"));
        Assert.Single(view.FindAll(".roadmap-timeline__current-week"));

        // The weeks near today give a drag a wider step than a quarter would.
        var grip = view.Find("[data-roadmap-bar='a'] [data-roadmap-grip='move']");
        Assert.Equal("3", grip.GetAttribute("data-roadmap-week-rem"));
    }
}
