using System.Globalization;

using Microsoft.JSInterop;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Every day column head says which day it is. A wide day reads "Wed 23" on its own
/// line as before; a narrow one keeps its number there and names the day on the caption
/// line in two letters of the culture's abbreviation, and the week's first day adds the week's number
/// when both fit.
/// </summary>
public sealed class RoadmapDayHeadWeekdayTests
{
    // Friday 25 September 2026: this week, Monday the 21st to Sunday the 27th, is ruled a
    // day a column.
    private static readonly DateOnly Today = new(2026, 9, 25);

    /// <summary>A day 2.75rem wide, the narrow width the app draws: too narrow for
    /// "Wed 23", wide enough for "Mo" under it.</summary>
    private const double NarrowQuarter = 22;

    /// <summary>A day 2.76rem wide, where QA saw "Mo W39" clip: Inter lays it out
    /// 45.25px in a 44.16px head, so the week's number has to stand alone.</summary>
    private const double ClippingQuarter = 2.76 * 8;

    /// <summary>A day 3.2rem wide: still too narrow for "Wed 23", wide enough for
    /// "Mo W39".</summary>
    private const double RoomyQuarter = 3.2 * 8;

    /// <summary>The default 2rem day: too narrow for "Mo W39".</summary>
    private const double TightQuarter = 16;

    /// <summary>A 6rem day, wide enough to say "Wed 23" on its own line.</summary>
    private const double WideQuarter = 48;

    [Fact]
    public void ANarrowDay_KeepsItsNumber_AndNamesItsWeekdayUnderIt()
    {
        WithCulture("en-US", () =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var view = Render(context, NarrowQuarter);

            Assert.Equal("23", LabelOf(view, "Wednesday 23"));
            Assert.Equal("We", CaptionOf(view, "Wednesday 23"));
            Assert.Equal("Sa", CaptionOf(view, "Saturday 26"));
        });
    }

    [Fact]
    public void TheWeeksFirstDay_NamesItsWeekdayAndTheWeek_WhereBothFit()
    {
        WithCulture("en-US", () =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var view = Render(context, RoomyQuarter);

            Assert.Equal("21", LabelOf(view, "Monday 21"));
            Assert.Equal("Mo W39", CaptionOf(view, "Monday 21"));
        });
    }

    /// <summary>The width QA measured clipping at: the estimate must not claim room
    /// Inter does not give.</summary>
    [Fact]
    public void TheWeeksFirstDay_KeepsOnlyTheWeek_WhereInterWouldClipBoth()
    {
        WithCulture("en-US", () =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var view = Render(context, ClippingQuarter);

            Assert.Equal("21", LabelOf(view, "Monday 21"));
            Assert.Equal("W39", CaptionOf(view, "Monday 21"));
            Assert.Equal("Tu", CaptionOf(view, "Tuesday 22"));
        });
    }

    /// <summary>The hours keep their unit at the widths QA checked — 2rem, 2.76rem and
    /// 3.36rem — whatever the caption line above them says.</summary>
    [Theory]
    [InlineData(2.0)]
    [InlineData(2.76)]
    [InlineData(3.36)]
    public void TheHoursKeepTheirUnit_AtTheWidthsQaChecked(double dayRem)
    {
        WithCulture("en-US", () =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var view = Render(context, dayRem * 8, workingHours: FiveDays);

            Assert.Equal("8.5h", ColumnOf(view, "Wednesday 23").QuerySelector(".roadmap-timeline__quarter-hours")!.TextContent.Trim());
        });
    }

    [Fact]
    public void TheWeeksFirstDay_KeepsOnlyTheWeek_WhereBothDoNotFit()
    {
        WithCulture("en-US", () =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var view = Render(context, TightQuarter);

            Assert.Equal("W39", CaptionOf(view, "Monday 21"));
            Assert.Equal("Tu", CaptionOf(view, "Tuesday 22"));
            Assert.StartsWith("Monday 21", ColumnOf(view, "Monday 21").GetAttribute("title"), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TheWeekStartsWhereTheTimelineSaysItDoes()
    {
        WithCulture("en-US", () =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var view = Render(context, RoomyQuarter, DayOfWeek.Sunday);

            Assert.StartsWith("Su W", CaptionOf(view, "Sunday 20"), StringComparison.Ordinal);
            Assert.Equal("Mo", CaptionOf(view, "Monday 21"));
        });
    }

    [Fact]
    public void AWideDay_ReadsAsBefore()
    {
        WithCulture("en-US", () =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var view = Render(context, WideQuarter);

            Assert.Equal("Wed 23", LabelOf(view, "Wednesday 23"));
            Assert.Equal(string.Empty, CaptionOf(view, "Wednesday 23"));
            Assert.Equal("Mon 21", LabelOf(view, "Monday 21"));
            Assert.Equal("W39", CaptionOf(view, "Monday 21"));
        });
    }

    [Fact]
    public void TheWeekdayIsTheCulturesOwn()
    {
        WithCulture("nl-NL", () =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var view = Render(context, RoomyQuarter);

            Assert.Equal("wo", CaptionOf(view, "woensdag 23"));
            Assert.Equal("ma W39", CaptionOf(view, "maandag 21"));
        });
    }

    /// <summary>The weekday shares the caption line, so a working week's hours still
    /// ride on the third and a day off is still shaded.</summary>
    [Fact]
    public void TheWeekdaySitsBesideTheHoursAndTheShading()
    {
        WithCulture("en-US", () =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var view = Render(context, NarrowQuarter, workingHours: FiveDays);

            Assert.Equal("We", CaptionOf(view, "Wednesday 23"));
            Assert.Equal("8.5h", ColumnOf(view, "Wednesday 23").QuerySelector(".roadmap-timeline__quarter-hours")!.TextContent.Trim());

            var saturday = ColumnOf(view, "Saturday 26");
            Assert.Equal("Sa", CaptionOf(view, "Saturday 26"));
            Assert.Contains("roadmap-timeline__quarter--weekend", saturday.ClassName);
        });
    }

    /// <summary>Monday to Friday, 8.5 hours a day.</summary>
    private static readonly IReadOnlyDictionary<DayOfWeek, double> FiveDays = new Dictionary<DayOfWeek, double>
    {
        [DayOfWeek.Monday] = 8.5,
        [DayOfWeek.Tuesday] = 8.5,
        [DayOfWeek.Wednesday] = 8.5,
        [DayOfWeek.Thursday] = 8.5,
        [DayOfWeek.Friday] = 8.5
    };

    private static IRenderedComponent<RoadmapTimeline> Render(
        BunitContext context,
        double quarterWidth,
        DayOfWeek weekStart = DayOfWeek.Monday,
        IReadOnlyDictionary<DayOfWeek, double>? workingHours = null) =>
        context.Render<RoadmapTimeline>(parameters => parameters
            .Add(timeline => timeline.Groups, [new RoadmapGroup("backlog", "backlog", [new RoadmapRow("backlog::Planned", string.Empty)])])
            .Add(timeline => timeline.Bars, [new RoadmapBar("a", "backlog::Planned", "A", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 30))])
            .Add(timeline => timeline.Today, Today)
            .Add(timeline => timeline.Graduated, true)
            .Add(timeline => timeline.WeekStart, weekStart)
            .Add(timeline => timeline.QuarterWidth, quarterWidth)
            .Add(timeline => timeline.WorkingHoursByDay, workingHours));

    private static AngleSharp.Dom.IElement ColumnOf(IRenderedComponent<RoadmapTimeline> view, string titleStart) =>
        Assert.Single(view.FindAll(".roadmap-timeline__quarter--day"), column => (column.GetAttribute("title") ?? string.Empty).StartsWith(titleStart, StringComparison.Ordinal));

    private static string LabelOf(IRenderedComponent<RoadmapTimeline> view, string titleStart) =>
        ColumnOf(view, titleStart).QuerySelector(".roadmap-timeline__quarter-label")!.TextContent.Trim();

    private static string CaptionOf(IRenderedComponent<RoadmapTimeline> view, string titleStart) =>
        ColumnOf(view, titleStart).QuerySelector(".roadmap-timeline__quarter-year")!.TextContent.Trim();

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
