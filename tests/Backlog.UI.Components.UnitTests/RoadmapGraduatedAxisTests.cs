using Microsoft.JSInterop;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The graduated axis, the same both ways from today: last week, this week and next
/// a column a day, three weeks either side of those, months for about a quarter
/// beyond, quarters beyond that. Pinned as columns and distances, because a ruler
/// that changes scale is only honest if every column meets the next exactly and a
/// day is wider near today than a year out.
/// </summary>
public sealed class RoadmapGraduatedAxisTests
{
    // Friday 25 September 2026. Its week starts on Monday the 21st; the days run
    // from Monday 14 September to Sunday 4 October. Forward, the weeks run from
    // 5 October to Monday 26 October, where the months take over — the rest of
    // October as a column of its own — and the first quarter start three months
    // after that is 1 April 2027. Backward, the weeks run from Monday 24 August,
    // the months before that from 1 April 2026, and quarters before that.
    private static readonly DateOnly Today = new(2026, 9, 25);

    private static RoadmapWindow Window(params DateOnly[] dates) =>
        RoadmapWindow.Graduated(dates, Today, DayOfWeek.Monday);

    [Fact]
    public void Days_ThenWeeks_ThenMonths_ThenQuarters_EachTierStartingOnABoundaryOfTheNext()
    {
        var window = Window(new DateOnly(2026, 10, 1), new DateOnly(2027, 8, 15));

        Assert.True(window.IsGraduated);
        Assert.Equal(new DateOnly(2026, 8, 24), window.Start);
        Assert.Equal(new DateOnly(2027, 9, 30), window.End);

        var days = window.Columns.Where(column => column.Scale == RoadmapColumnScale.Day).ToList();
        var history = window.Columns.Where(column => column.Scale == RoadmapColumnScale.Week && column.Start < Today).ToList();
        var weeks = window.Columns.Where(column => column.Scale == RoadmapColumnScale.Week && column.Start > Today).ToList();
        var months = window.Columns.Where(column => column.Scale == RoadmapColumnScale.Month).ToList();
        var quarters = window.Columns.Where(column => column.Scale == RoadmapColumnScale.Quarter).ToList();

        Assert.Equal(21, days.Count);
        Assert.All(days, day => Assert.Equal(1, day.TotalDays));
        Assert.Equal(new DateOnly(2026, 9, 14), days[0].Start);
        Assert.Equal(new DateOnly(2026, 10, 4), days[^1].End);

        Assert.Equal(RoadmapWindow.GraduatedWeeks, history.Count);
        Assert.Equal(RoadmapWindow.GraduatedWeeks, weeks.Count);
        Assert.All(weeks, week => Assert.Equal(7, week.TotalDays));
        Assert.Equal(new DateOnly(2026, 10, 25), weeks[^1].End);
        Assert.Equal(new DateOnly(2026, 10, 26), months[0].Start);
        Assert.Equal(new DateOnly(2026, 10, 31), months[0].End);
        Assert.Equal([10, 11, 12, 1, 2, 3], months.Select(month => month.Start.Month));
        Assert.All(months.Skip(1), month => Assert.Equal(1, month.Start.Day));
        Assert.Equal(["Q2 2027", "Q3 2027"], quarters.Select(quarter => quarter.LongLabel));

        // In order, and with no day left out or ruled twice.
        Assert.Equal(
            [RoadmapColumnScale.Week, RoadmapColumnScale.Day, RoadmapColumnScale.Month, RoadmapColumnScale.Quarter],
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
    public void RecentHistory_IsRuledInMonths_ThenThreeWeeks_ThenLastWeeksDays()
    {
        var window = Window(new DateOnly(2026, 8, 10), new DateOnly(2026, 11, 1));

        Assert.Equal(new DateOnly(2026, 8, 1), window.Start);
        Assert.Equal(RoadmapColumnScale.Month, window.Columns[0].Scale);
        Assert.Equal(new DateOnly(2026, 8, 23), window.Columns[0].End);

        var history = window.Columns.Skip(1).TakeWhile(column => column.Scale == RoadmapColumnScale.Week).ToList();
        Assert.Equal(
            [new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 7)],
            history.Select(week => week.Start));
        Assert.Equal(RoadmapColumnScale.Day, window.Columns[1 + history.Count].Scale);
        Assert.Equal(new DateOnly(2026, 9, 14), window.Columns[1 + history.Count].Start);
        AssertContiguous(window);
    }

    [Fact]
    public void LongAgo_IsRuledInQuarters_ThenWholeMonthsFromAQuarterStart()
    {
        // The mirror of the horizon: months for at least a quarter before the
        // weeks, beginning on a quarter start, and quarters before that.
        var window = Window(new DateOnly(2025, 11, 10), new DateOnly(2026, 11, 1));

        var quarters = window.Columns.TakeWhile(column => column.Scale == RoadmapColumnScale.Quarter).ToList();
        Assert.Equal(["Q4 2025", "Q1 2026"], quarters.Select(quarter => quarter.LongLabel));
        Assert.Equal(new DateOnly(2025, 10, 1), window.Start);

        var months = window.Columns.Skip(quarters.Count).TakeWhile(column => column.Scale == RoadmapColumnScale.Month).ToList();
        Assert.Equal([4, 5, 6, 7, 8], months.Select(month => month.Start.Month));
        Assert.All(months, month => Assert.Equal(1, month.Start.Day));
        Assert.Equal(new DateOnly(2026, 8, 23), months[^1].End);
        AssertContiguous(window);
    }

    [Fact]
    public void RecentHistory_AlwaysOffersThreeWeeksAndLastWeeksDays_EvenWhenNothingStartedThen()
    {
        // Nothing drawn before this week: the weeks and days before it are still
        // there to scroll back into, from Monday 24 August, and nothing earlier.
        var window = Window(new DateOnly(2026, 9, 24), new DateOnly(2026, 11, 1));

        Assert.Equal(new DateOnly(2026, 8, 24), window.Start);
        Assert.Equal(
            [RoadmapColumnScale.Week, RoadmapColumnScale.Week, RoadmapColumnScale.Week, RoadmapColumnScale.Day],
            window.Columns.Take(4).Select(column => column.Scale));
        Assert.DoesNotContain(window.Columns.TakeWhile(column => column.Start < Today), column => column.Scale == RoadmapColumnScale.Month);
        AssertContiguous(window);
    }

    [Fact]
    public void AnEmptyPlan_StillOffersTheRecentHistory()
    {
        var window = Window();

        Assert.Equal(new DateOnly(2026, 8, 24), window.Start);
        AssertContiguous(window);
    }

    [Fact]
    public void TheDays_RunFromLastWeekToNext_EachWeeksFirstCarryingItsNumber()
    {
        var days = Window(new DateOnly(2026, 10, 1)).Columns
            .Where(column => column.Scale == RoadmapColumnScale.Day)
            .ToList();

        // Mondays 14, 21 and 28 September 2026 open ISO weeks 38, 39 and 40.
        Assert.Equal(["W38", "W39", "W40"], days.Where(day => day.Caption is not null).Select(day => day.Caption));
        Assert.Equal([0, 7, 14], days.Select((day, index) => (day, index)).Where(pair => pair.day.Caption is not null).Select(pair => pair.index));
        Assert.EndsWith("14", days[0].Label);
        Assert.EndsWith("4", days[^1].Label);
    }

    [Fact]
    public void AWeekHead_IsItsWeekNumber_AndNamesTheMonthOnlyWhereTheMonthChanges()
    {
        var weeks = Window(new DateOnly(2026, 10, 1)).Columns
            .Where(column => column.Scale == RoadmapColumnScale.Week && column.Start > Today)
            .ToList();

        // The first week after the days enters October, which no day names; the
        // next is still in it.
        Assert.Equal("W41", weeks[0].Label);
        Assert.Equal("W42", weeks[1].Label);
        Assert.NotNull(weeks[0].Caption);
        Assert.Null(weeks[1].Caption);
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

        // The rest of October after the weeks is its share of a month.
        var october = window.Columns.First(column => column.Scale == RoadmapColumnScale.Month);
        Assert.Equal(6, october.TotalDays);
        Assert.Equal(geometry.ColumnWidthRem(RoadmapColumnScale.Month) * 6 / 31, geometry.WidthFor(october.Start, october.End), 6);
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
        var across = geometry.WidthFor(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 31));
        Assert.Equal(
            geometry.ColumnWidthRem(RoadmapColumnScale.Week) + geometry.ColumnWidthRem(RoadmapColumnScale.Month) * 6 / 31,
            across,
            6);
    }

    [Fact]
    public void AGraduatedTimeline_RulesThisWeekInDays_AndNamesAGroupOnce()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = RenderPlan(context);

        // Last week, this week and next in days; three weeks either side of them.
        Assert.Equal(21, view.FindAll(".roadmap-timeline__quarter--day").Count);
        Assert.Equal(6, view.FindAll(".roadmap-timeline__quarter--week").Count);
        Assert.Equal(6, view.FindAll(".roadmap-timeline__quarter--month").Count);
        // Where history's weeks meet the days, then weeks, then months.
        Assert.Equal(3, view.FindAll(".roadmap-timeline__rule--scale").Count);

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
    public void AGraduatedTimeline_ShadesItsWeekendDays_AndNoCoarserColumn()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = RenderPlan(context);

        // The days run 14 September to 4 October: three weekends, six days.
        var heads = view.FindAll(".roadmap-timeline__quarter--weekend");
        Assert.Equal(6, heads.Count);
        Assert.All(heads, head => Assert.Contains("roadmap-timeline__quarter--day", head.ClassList));
        Assert.Equal(["19", "20", "26", "27", "3", "4"], heads.Select(head => head.QuerySelector(".roadmap-timeline__quarter-label")!.TextContent.Trim()));

        // Shaded down the chart too, each a day wide where its head is.
        var bands = view.FindAll(".roadmap-timeline__weekend");
        Assert.Equal(6, bands.Count);
        Assert.Equal(
            heads.Select(head => LeftOf(head.GetAttribute("style")!)),
            bands.Select(band => LeftOf(band.GetAttribute("style")!)));
        Assert.All(bands, band => Assert.Contains("width: 2rem", band.GetAttribute("style")));
    }

    [Fact]
    public void AMeasuredScroller_StretchesTheChart_SoFromLastWeekOnItFillsTheWidth()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = RenderPlan(context);
        var track = view.Find(".roadmap-timeline__track");
        var natural = RemOf(track.GetAttribute("style")!);
        var history = LeftOf(view.Find(".roadmap-timeline__quarter--day").GetAttribute("style")!);
        var shown = natural - history;

        // Wider than the chart from last week on: every column widens by the same
        // factor until that stretch is exactly the scroller's width — the history to
        // its left widening with it — and the days name their weekdays.
        view.InvokeAsync(() => view.Instance.Measured(shown * 2));

        Assert.Equal(natural * 2, RemOf(view.Find(".roadmap-timeline__track").GetAttribute("style")!), 2);
        Assert.Contains(" ", view.Find(".roadmap-timeline__quarter--current .roadmap-timeline__quarter-label").TextContent.Trim());

        // Narrower than the chart: never squeezed below its natural width.
        view.InvokeAsync(() => view.Instance.Measured(shown / 2));

        Assert.Equal(natural, RemOf(view.Find(".roadmap-timeline__track").GetAttribute("style")!), 2);
    }

    [Fact]
    public void AMeasuredScroller_OpensOnLastWeek_WithOlderHistoryToTheLeft()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = RenderPlan(context, new RoadmapBar("old", "backlog::platform", "Old", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 10), Locked: true));

        view.InvokeAsync(() => view.Instance.Measured(60));

        var scroll = context.JSInterop.Invocations.Last(invocation => invocation.Identifier == "backlogRoadmapTimeline.scrollTo");
        Assert.True((double)scroll.Arguments[1]! > 0, "History before this week sits to the left of where the chart opens.");
    }

    // --- Resizing snaps to the columns ----------------------------------------

    [Fact]
    public void TheColumnBoundaries_AreEveryColumnStart_AndTheDayAfterTheLast()
    {
        var window = Window(new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 15));
        var lines = window.ColumnBoundaries;

        Assert.Equal(window.Start, lines[0]);
        Assert.Equal(window.End.AddDays(1), lines[^1]);
        Assert.Equal(window.Columns.Count + 1, lines.Count);

        // A line every day around today, every week beyond, then a month.
        Assert.Contains(new DateOnly(2026, 9, 24), lines);
        Assert.Contains(new DateOnly(2026, 9, 25), lines);
        Assert.DoesNotContain(new DateOnly(2026, 10, 6), lines);
        Assert.Contains(new DateOnly(2026, 10, 12), lines);
        Assert.Contains(new DateOnly(2026, 11, 1), lines);
        Assert.DoesNotContain(new DateOnly(2026, 11, 2), lines);
    }

    [Fact]
    public void AnEdgeAmongTheDays_MovesADayAStep()
    {
        var lines = Window(new DateOnly(2026, 12, 15)).ColumnBoundaries;
        var bar = new RoadmapBar("d", "row", "D", new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 25));

        var later = RoadmapChange.For(bar, RoadmapDrag.ResizeEnd, 1, null, DayOfWeek.Monday, lines);
        var sooner = RoadmapChange.For(bar, RoadmapDrag.ResizeEnd, -2, null, DayOfWeek.Monday, lines);
        var earlier = RoadmapChange.For(bar, RoadmapDrag.ResizeStart, -1, null, DayOfWeek.Monday, lines);

        Assert.Equal(new DateOnly(2026, 9, 26), later!.End);
        Assert.Equal(new DateOnly(2026, 9, 23), sooner!.End);
        Assert.Equal(new DateOnly(2026, 9, 20), earlier!.Start);
        Assert.Equal(bar.End, earlier.End);
    }

    [Fact]
    public void AnEdgeAmongTheMonths_MovesToTheNextMonth_AndAnEdgeBetweenLinesReachesTheNearerOneFirst()
    {
        var lines = Window(new DateOnly(2027, 2, 15)).ColumnBoundaries;

        // Ends on Friday 20 November, mid-month: one step out is the end of
        // November, two the end of December.
        var bar = new RoadmapBar("c", "row", "C", new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 20));

        Assert.Equal(new DateOnly(2026, 11, 30), RoadmapChange.For(bar, RoadmapDrag.ResizeEnd, 1, null, DayOfWeek.Monday, lines)!.End);
        Assert.Equal(new DateOnly(2026, 12, 31), RoadmapChange.For(bar, RoadmapDrag.ResizeEnd, 2, null, DayOfWeek.Monday, lines)!.End);

        // A start on the 2nd steps back to the 1st, not a whole month further.
        Assert.Equal(new DateOnly(2026, 11, 1), RoadmapChange.For(bar, RoadmapDrag.ResizeStart, -1, null, DayOfWeek.Monday, lines)!.Start);
    }

    [Fact]
    public void AnEdgePulledPastTheOppositeEdge_ClampsToTheShortestBarTheColumnsAllow()
    {
        var lines = Window(new DateOnly(2027, 2, 15)).ColumnBoundaries;
        var bar = new RoadmapBar("c", "row", "C", new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 20));

        var end = RoadmapChange.For(bar, RoadmapDrag.ResizeEnd, -3, null, DayOfWeek.Monday, lines);
        var start = RoadmapChange.For(bar, RoadmapDrag.ResizeStart, 3, null, DayOfWeek.Monday, lines);

        Assert.Equal(bar.Start, end!.End);
        Assert.Equal(bar.End, start!.Start);
    }

    [Fact]
    public void ShiftAndAnArrow_OnAGraduatedAxis_MovesTheEndByAColumn()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        RoadmapChange? reported = null;

        var view = context.Render<RoadmapTimeline>(parameters => parameters
            .Add(timeline => timeline.Groups, [new RoadmapGroup("g", "g", [new RoadmapRow("row", "Row")])])
            .Add(timeline => timeline.Bars, [new RoadmapBar("d", "row", "D", new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 25))])
            .Add(timeline => timeline.Today, Today)
            .Add(timeline => timeline.Graduated, true)
            .Add(timeline => timeline.TestId, "rm")
            .Add(timeline => timeline.OnBarChanged, (RoadmapChange change) => reported = change));

        // The script reads where the lines are from the track, and where the edge is
        // from its grip.
        Assert.False(string.IsNullOrEmpty(view.Find(".roadmap-timeline__track").GetAttribute("data-roadmap-snaps")));
        Assert.False(string.IsNullOrEmpty(view.Find("[data-roadmap-grip='end']").GetAttribute("data-roadmap-edge-rem")));

        var bar = view.Find("[data-testid='rm-bar-d'] .roadmap-bar__body");

        bar.KeyDown(new KeyboardEventArgs { Key = " " });
        bar.KeyDown(new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });
        bar.KeyDown(new KeyboardEventArgs { Key = " " });

        Assert.NotNull(reported);
        Assert.Equal(RoadmapDrag.ResizeEnd, reported.Kind);
        Assert.Equal(new DateOnly(2026, 9, 26), reported.End);
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

    private static double LeftOf(string style)
    {
        var start = style.IndexOf("left:", StringComparison.Ordinal) + "left:".Length;
        var end = style.IndexOf("rem", start, StringComparison.Ordinal);

        return double.Parse(style[start..end].Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

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
