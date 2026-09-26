using Microsoft.JSInterop;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The graduated axis: this week a column a day, three weeks after it, months for
/// about three after, quarters beyond — and history before it, weeks and then
/// months. Pinned as columns and distances, because a ruler that changes scale is
/// only honest if every column meets the next exactly and a day is wider near
/// today than a year out.
/// </summary>
public sealed class RoadmapGraduatedAxisTests
{
    // Friday 25 September 2026. Its week starts on Monday the 21st and is ruled a
    // day a column; the weeks run from the 28th, and four weeks from the 21st is
    // Monday 19 October, where the months take over — the rest of October as a
    // column of its own — and the first quarter start three months after that is
    // 1 April 2027. Four weeks back is Monday 24 August, where history turns from
    // weeks to months.
    private static readonly DateOnly Today = new(2026, 9, 25);

    private static RoadmapWindow Window(params DateOnly[] dates) =>
        RoadmapWindow.Graduated(dates, Today, DayOfWeek.Monday);

    [Fact]
    public void Days_ThenWeeks_ThenMonths_ThenQuarters_EachTierStartingOnABoundaryOfTheNext()
    {
        var window = Window(new DateOnly(2026, 10, 1), new DateOnly(2027, 8, 15));

        Assert.True(window.IsGraduated);
        Assert.Equal(new DateOnly(2026, 9, 21), window.Start);
        Assert.Equal(new DateOnly(2027, 9, 30), window.End);

        var days = window.Columns.Where(column => column.Scale == RoadmapColumnScale.Day).ToList();
        var weeks = window.Columns.Where(column => column.Scale == RoadmapColumnScale.Week).ToList();
        var months = window.Columns.Where(column => column.Scale == RoadmapColumnScale.Month).ToList();
        var quarters = window.Columns.Where(column => column.Scale == RoadmapColumnScale.Quarter).ToList();

        Assert.Equal(7, days.Count);
        Assert.All(days, day => Assert.Equal(1, day.TotalDays));
        Assert.Equal(new DateOnly(2026, 9, 21), days[0].Start);
        Assert.Equal(new DateOnly(2026, 9, 27), days[^1].End);

        Assert.Equal(RoadmapWindow.GraduatedWeeks - 1, weeks.Count);
        Assert.All(weeks, week => Assert.Equal(7, week.TotalDays));
        Assert.Equal(new DateOnly(2026, 10, 18), weeks[^1].End);
        Assert.Equal(new DateOnly(2026, 10, 19), months[0].Start);
        Assert.Equal(new DateOnly(2026, 10, 31), months[0].End);
        Assert.Equal([10, 11, 12, 1, 2, 3], months.Select(month => month.Start.Month));
        Assert.All(months.Skip(1), month => Assert.Equal(1, month.Start.Day));
        Assert.Equal(["Q2 2027", "Q3 2027"], quarters.Select(quarter => quarter.LongLabel));

        // In order, and with no day left out or ruled twice.
        Assert.Equal(
            [RoadmapColumnScale.Day, RoadmapColumnScale.Week, RoadmapColumnScale.Month, RoadmapColumnScale.Quarter],
            window.Columns.Select(column => column.Scale).Distinct());
        AssertContiguous(window);
    }

    [Fact]
    public void AShortPlan_StillShowsTheWholeWeeklyAndMonthlyHorizon()
    {
        var window = Window(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 30));

        Assert.Equal(new DateOnly(2027, 3, 31), window.End);
        Assert.DoesNotContain(window.Columns, column => column.Scale == RoadmapColumnScale.Quarter);
    }

    [Fact]
    public void LongAgo_IsRuledInMonths_ThenTheFourWeeksBeforeThisOne_InWeeks()
    {
        var window = Window(new DateOnly(2026, 8, 10), new DateOnly(2026, 11, 1));

        Assert.Equal(new DateOnly(2026, 8, 1), window.Start);
        Assert.Equal(RoadmapColumnScale.Month, window.Columns[0].Scale);
        Assert.Equal(new DateOnly(2026, 8, 23), window.Columns[0].End);

        var history = window.Columns.Skip(1).TakeWhile(column => column.Scale == RoadmapColumnScale.Week).ToList();
        Assert.Equal(
            [new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 14)],
            history.Select(week => week.Start));
        Assert.Equal(RoadmapColumnScale.Day, window.Columns[1 + history.Count].Scale);
        Assert.Equal(new DateOnly(2026, 9, 21), window.Columns[1 + history.Count].Start);
        AssertContiguous(window);
    }

    [Fact]
    public void RecentHistory_IsRuledOnlyAsFarBackAsTheWeekItBeganIn()
    {
        // Wednesday 9 September: two weeks of history, from Monday the 7th.
        var window = Window(new DateOnly(2026, 9, 9), new DateOnly(2026, 11, 1));

        Assert.Equal(new DateOnly(2026, 9, 7), window.Start);
        Assert.Equal(
            [RoadmapColumnScale.Week, RoadmapColumnScale.Week, RoadmapColumnScale.Day],
            window.Columns.Take(3).Select(column => column.Scale));
        Assert.DoesNotContain(window.Columns.TakeWhile(column => column.Start < Today), column => column.Scale == RoadmapColumnScale.Month);
        AssertContiguous(window);
    }

    [Fact]
    public void ThisWeeksDays_AreNamedByWeekday_TheFirstCarryingTheWeekNumber()
    {
        var days = Window(new DateOnly(2026, 10, 1)).Columns
            .Where(column => column.Scale == RoadmapColumnScale.Day)
            .ToList();

        // Monday 21 September 2026 opens ISO week 39.
        Assert.Equal("W39", days[0].Caption);
        Assert.All(days.Skip(1), day => Assert.Null(day.Caption));
        Assert.EndsWith("21", days[0].Label);
        Assert.EndsWith("27", days[^1].Label);
    }

    [Fact]
    public void AWeekHead_IsItsWeekNumber_AndNamesTheMonthOnlyWhereTheMonthChanges()
    {
        var weeks = Window(new DateOnly(2026, 10, 1)).Columns
            .Where(column => column.Scale == RoadmapColumnScale.Week)
            .ToList();

        // The week after this one, still in September, so no month under it.
        Assert.Equal("W40", weeks[0].Label);
        Assert.Equal("W41", weeks[1].Label);
        Assert.Null(weeks[0].Caption);
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
    public void ADay_IsWidestThisWeek_ThenInTheWeeks_ThenTheMonths_ThenTheQuarters()
    {
        var geometry = new RoadmapGeometry(Window(new DateOnly(2027, 8, 15)));

        var thisWeek = geometry.WeekWidthAt(new DateOnly(2026, 9, 21));
        var week = geometry.WeekWidthAt(new DateOnly(2026, 10, 5));
        var month = geometry.WeekWidthAt(new DateOnly(2027, 2, 1));
        var quarter = geometry.WeekWidthAt(new DateOnly(2027, 5, 3));

        Assert.Equal(14, thisWeek, 6);
        Assert.Equal(3, week, 6);
        Assert.True(thisWeek > week && week > month && month > quarter, $"{thisWeek} > {week} > {month} > {quarter}");
    }

    [Fact]
    public void EveryWholeDay_Week_Month_AndQuarter_IsDrawnTheSameWidth()
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
    public void AGraduatedTimeline_RulesThisWeekInDays_AndNamesAGroupOnce()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = RenderPlan(context);

        Assert.Equal(7, view.FindAll(".roadmap-timeline__quarter--day").Count);
        Assert.Equal(3, view.FindAll(".roadmap-timeline__quarter--week").Count);
        Assert.Equal(6, view.FindAll(".roadmap-timeline__quarter--month").Count);

        // The rest of October, though under a fortnight, is still wide enough to be named.
        Assert.Equal(1, view.FindAll(".roadmap-timeline__quarter--month")[0].QuerySelectorAll(".roadmap-timeline__quarter-label").Length);
        Assert.Equal(2, view.FindAll(".roadmap-timeline__rule--scale").Count);

        // One label column row per track row, the group named once, and the lane
        // named where it changes — never the unnamed lane, and never twice.
        Assert.Equal(3, view.FindAll(".roadmap-timeline__row-name").Count);
        Assert.Equal("backlog", Assert.Single(view.FindAll(".roadmap-timeline__group-name")).TextContent.Trim());
        Assert.Equal("platform", Assert.Single(view.FindAll(".roadmap-timeline__lane-name")).TextContent.Trim());

        // Today is marked in the head, and this week shaded down the chart. At its
        // natural width a day is too narrow for its weekday, so it keeps the number.
        var current = Assert.Single(view.FindAll(".roadmap-timeline__quarter--current"));
        Assert.Equal("25", current.QuerySelector(".roadmap-timeline__quarter-label")!.TextContent.Trim());
        Assert.Equal("date", current.GetAttribute("aria-current"));
        var shade = Assert.Single(view.FindAll(".roadmap-timeline__current-week"));
        Assert.Contains("width: 14rem", shade.GetAttribute("style"));

        // The weeks near today give a drag a wider step than a quarter would.
        var grip = view.Find("[data-roadmap-bar='a'] [data-roadmap-grip='move']");
        Assert.Equal("3", grip.GetAttribute("data-roadmap-week-rem"));
    }

    [Fact]
    public void AMeasuredScroller_StretchesTheChart_SoFromThisWeekOnItFillsTheWidth()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = RenderPlan(context);
        var track = view.Find(".roadmap-timeline__track");
        var natural = RemOf(track.GetAttribute("style")!);

        // Wider than the chart: every column widens by the same factor until the
        // chart is exactly the scroller's width, and the days name their weekdays.
        view.InvokeAsync(() => view.Instance.Measured(natural * 2));

        Assert.Equal(natural * 2, RemOf(view.Find(".roadmap-timeline__track").GetAttribute("style")!), 2);
        Assert.Contains(" ", view.Find(".roadmap-timeline__quarter--current .roadmap-timeline__quarter-label").TextContent.Trim());

        // Narrower than the chart: never squeezed below its natural width.
        view.InvokeAsync(() => view.Instance.Measured(natural / 2));

        Assert.Equal(natural, RemOf(view.Find(".roadmap-timeline__track").GetAttribute("style")!), 2);
    }

    [Fact]
    public void AMeasuredScroller_OpensOnThisWeek_WithHistoryToTheLeft()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = RenderPlan(context, new RoadmapBar("old", "backlog::platform", "Old", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 10), Locked: true));

        view.InvokeAsync(() => view.Instance.Measured(60));

        var scroll = context.JSInterop.Invocations.Last(invocation => invocation.Identifier == "backlogRoadmapTimeline.scrollTo");
        Assert.True((double)scroll.Arguments[1]! > 0, "History before this week sits to the left of where the chart opens.");
    }

    private static IRenderedComponent<RoadmapTimeline> RenderPlan(BunitContext context, params RoadmapBar[] extra) =>
        context.Render<RoadmapTimeline>(parameters => parameters
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
                new RoadmapBar("c", "backlog::platform", "C", new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 20)),
                .. extra
            ])
            .Add(timeline => timeline.Today, Today)
            .Add(timeline => timeline.Graduated, true));

    private static double RemOf(string style)
    {
        var start = style.IndexOf("width:", StringComparison.Ordinal) + "width:".Length;
        var end = style.IndexOf("rem", start, StringComparison.Ordinal);

        return double.Parse(style[start..end].Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AssertContiguous(RoadmapWindow window)
    {
        for (var index = 1; index < window.Columns.Count; index++)
        {
            Assert.Equal(window.Columns[index - 1].End.AddDays(1), window.Columns[index].Start);
        }
    }
}
