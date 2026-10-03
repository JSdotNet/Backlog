using System.Globalization;

using Microsoft.JSInterop;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The graduated axis given a working week (local ADR 0019, §4): a worked day's head and
/// a week's head show their working hours, a day not worked is shaded and shows none, and
/// every column keeps the width it had without a week.
/// </summary>
public sealed class RoadmapWorkingHoursAxisTests
{
    // Sunday 20 September 2026: the week after it, Monday the 21st to Sunday the 27th, is
    // ruled a day a column and still to come, so its heads say their planned hours (local
    // ADR 0019, §4); week 41 starts on Monday 5 October.
    private static readonly DateOnly Today = new(2026, 9, 20);

    /// <summary>The default week: Monday to Friday, nine to half five.</summary>
    private static readonly IReadOnlyDictionary<DayOfWeek, double> DefaultWeek = new Dictionary<DayOfWeek, double>
    {
        [DayOfWeek.Monday] = 8.5,
        [DayOfWeek.Tuesday] = 8.5,
        [DayOfWeek.Wednesday] = 8.5,
        [DayOfWeek.Thursday] = 8.5,
        [DayOfWeek.Friday] = 8.5,
        [DayOfWeek.Saturday] = 0,
        [DayOfWeek.Sunday] = 0
    };

    /// <summary>ADR 0019 Verification 9: on the default week a day head reads
    /// "Wed 23 · 8.5h", a week head reads "W41 · 42.5h", and Saturday and Sunday are
    /// shaded at the same width as the other days.</summary>
    [Fact]
    public void TheAxisShowsTheHoursAndShadesTheDaysOff()
    {
        WithCulture("en-US", () =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var view = Render(context, DefaultWeek, quarterWidth: 48);

            Assert.Equal("Wed 23 · 8.5h", HeadOf(view, "Wednesday 23").TextContent.Trim());
            Assert.Equal("W41 · 42.5h", HeadOf(view, "Week 41").TextContent.Trim());

            // Said inline, so not said twice on the hours line.
            Assert.Equal(string.Empty, HoursLineOf(view, "Wednesday 23").TextContent.Trim());

            var saturday = ColumnOf(view, "Saturday 26");
            var sunday = ColumnOf(view, "Sunday 27");
            var wednesday = ColumnOf(view, "Wednesday 23");

            Assert.Contains("roadmap-timeline__quarter--weekend", saturday.ClassName);
            Assert.Contains("roadmap-timeline__quarter--weekend", sunday.ClassName);
            Assert.DoesNotContain("roadmap-timeline__quarter--weekend", wednesday.ClassName);
            Assert.Equal("Sat 26", HeadOf(view, "Saturday 26").TextContent.Trim());
            Assert.Contains("not worked", saturday.GetAttribute("title"));
            Assert.Equal(ShadedTitles(view), DayTitles(view, DayOfWeek.Saturday, DayOfWeek.Sunday));
            Assert.Equal(ShadedTitles(view).Count, view.FindAll(".roadmap-timeline__weekend").Count);

            Assert.Equal(WidthOf(wednesday), WidthOf(saturday), 6);
            Assert.Equal(WidthOf(wednesday), WidthOf(sunday), 6);
        });
    }

    /// <summary>
    /// At the default width — a day column 2rem, a week 3rem — there is no room for
    /// "Wed 23 · 8.5h", so the hours ride on the head's own third line: "8.5h" under a
    /// worked day, "42.5h" under a week, nothing under a day off or a month. The
    /// existing captions stay where they were.
    /// </summary>
    [Fact]
    public void AtTheDefaultWidth_TheHoursRideOnTheirOwnLine()
    {
        WithCulture("en-US", () =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var view = Render(context, DefaultWeek);

            Assert.Equal("23", HeadOf(view, "Wednesday 23").TextContent.Trim());
            Assert.Equal("8.5h", HoursLineOf(view, "Wednesday 23").TextContent.Trim());
            Assert.Equal("8.5h", HoursLineOf(view, "Monday 21").TextContent.Trim());
            Assert.Equal("W39", CaptionOf(view, "Monday 21").TextContent.Trim()); // the week's number still rides under its first day

            Assert.Equal(string.Empty, HoursLineOf(view, "Saturday 26").TextContent.Trim());
            Assert.Equal(string.Empty, HoursLineOf(view, "Sunday 27").TextContent.Trim());

            Assert.Equal("W41", HeadOf(view, "Week 41").TextContent.Trim());
            Assert.Equal("42.5h", HoursLineOf(view, "Week 41").TextContent.Trim());
            Assert.Equal("Oct", CaptionOf(view, "Week 41").TextContent.Trim()); // the month still rides under the week it enters

            Assert.Equal(string.Empty, HoursLineOf(view, "November 2026").TextContent.Trim());

            // The tooltip still says them.
            Assert.EndsWith(" · 8.5h", ColumnOf(view, "Wednesday 23").GetAttribute("title"));
            Assert.Contains("roadmap-timeline--hours", view.Find("section.roadmap-timeline").ClassName);

        });
    }

    /// <summary>A figure that would not fit under a 2rem day with its unit gives the
    /// unit up rather than spilling over the column beside it.</summary>
    [Fact]
    public void AtTheDefaultWidth_ALongFigureDropsItsUnit()
    {
        WithCulture("en-US", () =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var week = new Dictionary<DayOfWeek, double>(DefaultWeek) { [DayOfWeek.Monday] = 8 + 20 / 60d };
            var view = Render(context, week);

            Assert.Equal("8.33", HoursLineOf(view, "Monday 21").TextContent.Trim());
            Assert.Equal("8.5h", HoursLineOf(view, "Tuesday 22").TextContent.Trim());
        });
    }

    /// <summary>The week moves no column: the axis is still time.</summary>
    [Fact]
    public void AWorkingWeekChangesNoColumnsWidth()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var plain = Render(context, null).FindAll(".roadmap-timeline__quarter").Select(column => column.GetAttribute("style")).ToList();
        var counted = Render(context, DefaultWeek).FindAll(".roadmap-timeline__quarter").Select(column => column.GetAttribute("style")).ToList();

        Assert.Equal(plain, counted);
    }

    /// <summary>Without a week the axis says no hours, and the days it shades are the
    /// weekend's — the same shading a week's days off get, never a second one.</summary>
    [Fact]
    public void WithoutAWeek_NoHours_AndTheWeekendIsShaded()
    {
        WithCulture("en-US", () =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var view = Render(context, null, quarterWidth: 48);

            Assert.Equal("Wed 23", HeadOf(view, "Wednesday 23").TextContent.Trim());
            Assert.Equal("W41", HeadOf(view, "Week 41").TextContent.Trim());
            Assert.Empty(view.FindAll(".roadmap-timeline__quarter-hours"));

            Assert.Contains("roadmap-timeline__quarter--weekend", ColumnOf(view, "Saturday 26").ClassName);
            Assert.Contains("roadmap-timeline__quarter--weekend", ColumnOf(view, "Sunday 27").ClassName);
            Assert.Equal(ShadedTitles(view), DayTitles(view, DayOfWeek.Saturday, DayOfWeek.Sunday));
            Assert.Equal(ShadedTitles(view).Count, view.FindAll(".roadmap-timeline__weekend").Count);

            // Nothing says a day is not worked when nobody gave a week to say it by.
            Assert.DoesNotContain("not worked", ColumnOf(view, "Saturday 26").GetAttribute("title"));
        });
    }

    /// <summary>Given a week, its days off are the ones shaded, not the weekend: a week
    /// worked Tuesday to Saturday shades Sunday and Monday, and Saturday shows its hours.</summary>
    [Fact]
    public void AWeeksOwnDaysOffAreShaded_NotTheWeekend()
    {
        WithCulture("en-US", () =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var week = new Dictionary<DayOfWeek, double>(DefaultWeek) { [DayOfWeek.Monday] = 0, [DayOfWeek.Saturday] = 8.5 };
            var view = Render(context, week, quarterWidth: 48);

            Assert.Equal(ShadedTitles(view), DayTitles(view, DayOfWeek.Sunday, DayOfWeek.Monday));
            Assert.Contains(ShadedTitles(view), title => title.StartsWith("Monday 21", StringComparison.Ordinal));
            Assert.Equal(ShadedTitles(view).Count, view.FindAll(".roadmap-timeline__weekend").Count);
            Assert.DoesNotContain("roadmap-timeline__quarter--weekend", ColumnOf(view, "Saturday 26").ClassName);
            Assert.Equal("Sat 26 · 8.5h", HeadOf(view, "Saturday 26").TextContent.Trim());
        });
    }

    /// <summary>A whole hour drops its decimals, and the hours are written invariant
    /// whatever the reader's culture spells a decimal with.</summary>
    [Fact]
    public void HoursAreWrittenInvariant_WholeHoursWithoutDecimals()
    {
        WithCulture("nl-NL", () =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var week = new Dictionary<DayOfWeek, double>(DefaultWeek) { [DayOfWeek.Monday] = 8, [DayOfWeek.Friday] = 4.25 };
            var view = Render(context, week, quarterWidth: 48);

            Assert.EndsWith(" · 8h", HeadOf(view, "maandag 21").TextContent.Trim());
            Assert.EndsWith(" · 4.25h", HeadOf(view, "vrijdag 25").TextContent.Trim());
            Assert.EndsWith(" · 37.75h", HeadOf(view, "Week 41").TextContent.Trim());
        });
    }

    private static IRenderedComponent<RoadmapTimeline> Render(
        BunitContext context,
        IReadOnlyDictionary<DayOfWeek, double>? week,
        double quarterWidth = 16) =>
        context.Render<RoadmapTimeline>(parameters => parameters
            .Add(timeline => timeline.Groups, [new RoadmapGroup("backlog", "backlog", [new RoadmapRow("backlog::Planned", string.Empty)])])
            .Add(timeline => timeline.Bars, [new RoadmapBar("a", "backlog::Planned", "A", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 30))])
            .Add(timeline => timeline.Today, Today)
            .Add(timeline => timeline.Graduated, true)
            // 16 is the default; 48 is wide enough for every head to say its hours inline.
            .Add(timeline => timeline.QuarterWidth, quarterWidth)
            .Add(timeline => timeline.PlannedHoursOn, ByWeekday(week)));

    /// <summary>A week by weekday as the per-date hours the timeline takes: every date
    /// reads its weekday's hours, a weekday missing at zero.</summary>
    internal static Func<DateOnly, double>? ByWeekday(IReadOnlyDictionary<DayOfWeek, double>? week) =>
        week is null ? null : date => week.TryGetValue(date.DayOfWeek, out var hours) ? hours : 0;

    /// <summary>The tooltips of the day heads shaded as days off, left to right.</summary>
    private static List<string> ShadedTitles(IRenderedComponent<RoadmapTimeline> view) =>
        view.FindAll(".roadmap-timeline__quarter--weekend").Select(column => column.GetAttribute("title") ?? string.Empty).ToList();

    /// <summary>The tooltips of the day heads that fall on <paramref name="days"/>, left
    /// to right — however many weeks the axis rules in days.</summary>
    private static List<string> DayTitles(IRenderedComponent<RoadmapTimeline> view, params DayOfWeek[] days)
    {
        var names = days.Select(day => CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day) + " ").ToList();

        return view.FindAll(".roadmap-timeline__quarter--day")
            .Select(column => column.GetAttribute("title") ?? string.Empty)
            .Where(title => names.Any(name => title.StartsWith(name, StringComparison.Ordinal)))
            .ToList();
    }

    private static AngleSharp.Dom.IElement ColumnOf(IRenderedComponent<RoadmapTimeline> view, string titleStart) =>
        Assert.Single(view.FindAll(".roadmap-timeline__quarter"), column => (column.GetAttribute("title") ?? string.Empty).StartsWith(titleStart, StringComparison.Ordinal));

    private static AngleSharp.Dom.IElement HeadOf(IRenderedComponent<RoadmapTimeline> view, string titleStart) =>
        ColumnOf(view, titleStart).QuerySelector(".roadmap-timeline__quarter-label")!;

    private static AngleSharp.Dom.IElement HoursLineOf(IRenderedComponent<RoadmapTimeline> view, string titleStart) =>
        ColumnOf(view, titleStart).QuerySelector(".roadmap-timeline__quarter-hours")!;

    private static AngleSharp.Dom.IElement CaptionOf(IRenderedComponent<RoadmapTimeline> view, string titleStart) =>
        ColumnOf(view, titleStart).QuerySelector(".roadmap-timeline__quarter-year")!;

    private static double WidthOf(AngleSharp.Dom.IElement column)
    {
        var style = column.GetAttribute("style")!;
        var start = style.IndexOf("width:", StringComparison.Ordinal) + "width:".Length;
        var end = style.IndexOf("rem", start, StringComparison.Ordinal);

        return double.Parse(style[start..end].Trim(), CultureInfo.InvariantCulture);
    }

    private static void WithCulture(string name, Action test)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(name);
        try
        {
            test();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
