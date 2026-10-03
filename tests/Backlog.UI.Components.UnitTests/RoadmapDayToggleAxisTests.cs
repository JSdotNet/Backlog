using System.Globalization;

using Microsoft.JSInterop;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The graduated axis read date by date (local ADR 0019, §§4 to 6): a day head is a
/// button that blocks or unblocks its date, a date that breaks its weekday's pattern is
/// marked, a date not worked is hatched, and a head that has begun shows the hours
/// actually worked over the hours planned.
/// </summary>
public sealed class RoadmapDayToggleAxisTests
{
    /// <summary>Monday 12 October 2026: last week, 5 to 11 October, is ruled a column a
    /// day and has passed; week 40, 28 September to 4 October, is a week column behind it.</summary>
    private static readonly DateOnly Monday12October = new(2026, 10, 12);

    /// <summary>Friday 25 September 2026: week 41, 5 to 11 October, is a week column
    /// still to come.</summary>
    private static readonly DateOnly Friday25September = new(2026, 9, 25);

    private static readonly DateOnly Thursday8 = new(2026, 10, 8);
    private static readonly DateOnly Friday9 = new(2026, 10, 9);
    private static readonly DateOnly Saturday10 = new(2026, 10, 10);
    private static readonly DateOnly Sunday11 = new(2026, 10, 11);

    /// <summary>A day 6rem wide: room to say "Thu 8" on its own line, and the hours
    /// under it, but not "Thu 8 · 6.2 / 8.5h" on one line.</summary>
    private const double WideQuarter = 48;

    // --- The day head is a toggle ------------------------------------------------

    /// <summary>Requirement "A day head is a toggle", scenario "The names of a worked
    /// and a day off": on the default week, Friday 9 October is offered to block and
    /// Saturday 10 October to unblock.</summary>
    [Fact]
    public void ADayHead_IsAButton_NamedForWhatPressingItDoes()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var view = Render(context, new Week(), Monday12October, toggled: _ => { });

            var friday = HeadButton(view, Friday9);
            Assert.Equal("BUTTON", friday.TagName);
            Assert.Equal("button", friday.GetAttribute("type"));
            Assert.Equal("Block Fri 9 Oct", friday.GetAttribute("aria-label"));
            Assert.Null(friday.GetAttribute("aria-pressed"));

            Assert.Equal("Unblock Sat 10 Oct", HeadButton(view, Saturday10).GetAttribute("aria-label"));
        });
    }

    /// <summary>
    /// ADR 0019 Verification 17, and scenario "Blocking from the keyboard". The head is a
    /// native button, so Enter and Space press it the way a click does — the browser's
    /// own activation, which bUnit cannot dispatch, and which no keydown handler here
    /// gets in the way of. Pressing it hands the date to the host, and the head is then
    /// named for undoing it.
    /// </summary>
    [Fact]
    public void PressingADayHead_HandsItsDateOver_AndItsNameThenOffersTheReverse()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var week = new Week();
            var pressed = new List<DateOnly>();
            var view = Render(context, week, Monday12OctoberMinusAWeek, toggled: date =>
            {
                pressed.Add(date);
                week.Toggle(date);
            });

            var friday = HeadButton(view, Friday9);
            Assert.Equal("Block Fri 9 Oct", friday.GetAttribute("aria-label"));
            Assert.False(friday.HasAttribute("blazor:onkeydown"));

            friday.Click();

            Assert.Equal([Friday9], pressed);
            view.WaitForAssertion(() =>
                Assert.Equal("Unblock Fri 9 Oct", HeadButton(view, Friday9).GetAttribute("aria-label")));
        });
    }

    /// <summary>Week, month and quarter heads are not toggles: only a date can be
    /// blocked.</summary>
    [Fact]
    public void OnlyDayHeadsAreButtons()
    {
        using var context = Loose();
        var view = Render(context, new Week(), Monday12October, toggled: _ => { });

        Assert.All(view.FindAll(".roadmap-timeline__quarter--week, .roadmap-timeline__quarter--month, .roadmap-timeline__quarter--quarter"),
            head => Assert.Equal("DIV", head.TagName));
        Assert.NotEmpty(view.FindAll(".roadmap-timeline__quarter--week"));
        Assert.All(view.FindAll(".roadmap-timeline__quarter--day"), head => Assert.Equal("BUTTON", head.TagName));
    }

    /// <summary>A host that does not listen gets the heads it had: nothing to press.</summary>
    [Fact]
    public void WithoutAListener_NoHeadIsAButton()
    {
        using var context = Loose();
        var view = Render(context, new Week(), Monday12October);

        Assert.Empty(view.FindAll(".roadmap-timeline__axis button"));
        Assert.All(view.FindAll(".roadmap-timeline__quarter--day"), head => Assert.Equal("DIV", head.TagName));
    }

    // --- Hatching and the marker ----------------------------------------------------

    /// <summary>Requirements "A date that is not worked is hatched" and "A date that
    /// breaks the pattern is marked", scenario "A blocked Friday": Friday 9 October is
    /// hatched as Sunday 11 October is, shows no planned hours, and carries the marker
    /// that Sunday does not.</summary>
    [Fact]
    public void ABlockedWeekday_IsHatched_ShowsNoHours_AndIsMarked()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var week = new Week();
            week.Toggle(Friday9);
            var view = Render(context, week, Monday12OctoberMinusAWeek, toggled: _ => { });

            var friday = ColumnOf(view, Friday9);
            var sunday = ColumnOf(view, Sunday11);

            Assert.Contains("roadmap-timeline__quarter--weekend", friday.ClassName);
            Assert.Contains("roadmap-timeline__quarter--override", friday.ClassName);
            Assert.Contains("roadmap-timeline__quarter--weekend", sunday.ClassName);
            Assert.DoesNotContain("roadmap-timeline__quarter--override", sunday.ClassName);

            Assert.Equal(string.Empty, HoursLineOf(friday));
            Assert.Equal("Friday 9 October 2026 · not worked · blocked", friday.GetAttribute("title"));
            Assert.Equal("Sunday 11 October 2026 · not worked", sunday.GetAttribute("title"));

            // Shaded down the chart too, at the day's own place.
            Assert.Contains(view.FindAll(".roadmap-timeline__weekend"), shade => shade.GetAttribute("style") == ShadeStyleOf(friday));
        });
    }

    /// <summary>Requirement "A date that is not worked is hatched", scenario "An
    /// unblocked Saturday": Saturday 10 October is not hatched, shows its hours and
    /// carries the marker; Sunday 11 October still reads as a day off.</summary>
    [Fact]
    public void AnUnblockedSaturday_IsNotHatched_ShowsItsHours_AndIsMarked()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var week = new Week();
            week.Toggle(Saturday10);
            var view = Render(context, week, Monday12OctoberMinusAWeek, toggled: _ => { });

            var saturday = ColumnOf(view, Saturday10);

            Assert.DoesNotContain("roadmap-timeline__quarter--weekend", saturday.ClassName);
            Assert.Contains("roadmap-timeline__quarter--override", saturday.ClassName);
            Assert.Equal("8.5h", HoursLineOf(saturday));
            Assert.Equal("Saturday 10 October 2026 · 8.5h · unblocked", saturday.GetAttribute("title"));
            Assert.Equal("Block Sat 10 Oct", saturday.GetAttribute("aria-label"));

            Assert.Contains("roadmap-timeline__quarter--weekend", ColumnOf(view, Sunday11).ClassName);
        });
    }

    /// <summary>Requirement "A week head sums its dates", scenario "A week with one
    /// blocked and one unblocked date": Friday 9 October blocked and Saturday 10 October
    /// unblocked at 10:00 to 14:00 make week 41 read 38 hours.</summary>
    [Fact]
    public void AWeekHead_SumsItsDates_OverridesIncluded()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var week = new Week(saturdayHours: 4);
            week.Toggle(Friday9);
            week.Toggle(Saturday10);
            var view = Render(context, week, Friday25September);

            Assert.Equal("38h", HoursLineOf(WeekColumn(view, "Week 41")));
            Assert.Equal("42.5h", HoursLineOf(WeekColumn(view, "Week 42")));
        });
    }

    // --- Actual over planned ----------------------------------------------------------

    /// <summary>ADR 0019 Verification 18 and scenario "A past day": agents active 6.2
    /// hours on Thursday 8 October read "6.2 / 8.5h"; a worked day nobody worked reads
    /// "0.0 / 8.5h"; and the tooltip says the same.</summary>
    [Fact]
    public void APastWorkedDay_ReadsActualOverPlanned()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var actual = new Dictionary<DateOnly, double> { [Thursday8] = 6.2 };
            var view = Render(context, new Week(), Monday12October, actual: actual, quarterWidth: WideQuarter);

            Assert.Equal("Thu 8", LabelOf(ColumnOf(view, Thursday8)));
            Assert.Equal("6.2 / 8.5h", HoursLineOf(ColumnOf(view, Thursday8)));
            Assert.Equal("0.0 / 8.5h", HoursLineOf(ColumnOf(view, Friday9)));
            Assert.Equal("Thursday 8 October 2026 · 6.2 / 8.5h", ColumnOf(view, Thursday8).GetAttribute("title"));
        });
    }

    /// <summary>Scenario "Today": today counts the hours so far, over its planned
    /// hours.</summary>
    [Fact]
    public void Today_ReadsTheHoursSoFar()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var actual = new Dictionary<DateOnly, double> { [Monday12October] = 3 };
            var view = Render(context, new Week(), Monday12October, actual: actual, quarterWidth: WideQuarter);

            Assert.Equal("3.0 / 8.5h", HoursLineOf(ColumnOf(view, Monday12October)));
        });
    }

    /// <summary>ADR 0019 Verification 22, scenario "A past day not worked": two hours on
    /// Saturday 10 October read "2.0h", the actual alone; Sunday 11 October, with no
    /// activity, reads nothing, as a day off always has.</summary>
    [Fact]
    public void APastDayOff_WithActivity_ReadsTheActualAlone()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var actual = new Dictionary<DateOnly, double> { [Saturday10] = 2 };
            var view = Render(context, new Week(), Monday12October, actual: actual, quarterWidth: WideQuarter);

            var saturday = ColumnOf(view, Saturday10);
            Assert.Equal("Sat 10 · 2.0h", LabelOf(saturday));
            Assert.Contains("roadmap-timeline__quarter--weekend", saturday.ClassName);
            Assert.Equal("Saturday 10 October 2026 · not worked · 2.0h", saturday.GetAttribute("title"));

            Assert.Equal("Sun 11", LabelOf(ColumnOf(view, Sunday11)));
            Assert.Equal(string.Empty, HoursLineOf(ColumnOf(view, Sunday11)));
        });
    }

    /// <summary>ADR 0019 Verification 20, scenario "A day still to come": a date after
    /// today shows its planned hours alone.</summary>
    [Fact]
    public void AFutureDay_ShowsPlannedHoursOnly()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var tuesday = Monday12October.AddDays(1);
            var actual = new Dictionary<DateOnly, double> { [Thursday8] = 6.2 };
            var view = Render(context, new Week(), Monday12October, actual: actual, quarterWidth: WideQuarter);

            Assert.Equal("Tue 13 · 8.5h", LabelOf(ColumnOf(view, tuesday)));
            Assert.Equal(string.Empty, HoursLineOf(ColumnOf(view, tuesday)));
        });
    }

    /// <summary>A week behind today reads the actual hours of its dates over its planned
    /// hours — inline where wide, on its own line where narrow, dropping the unit where
    /// even that will not fit. A week to come reads its planned hours alone.</summary>
    [Fact]
    public void APastWeek_SumsActualOverPlanned()
    {
        WithCulture("en-US", () =>
        {
            var actual = new Dictionary<DateOnly, double>
            {
                [new DateOnly(2026, 9, 28)] = 6.2,
                [new DateOnly(2026, 9, 29)] = 8,
                [new DateOnly(2026, 9, 30)] = 7.9,
                [new DateOnly(2026, 10, 1)] = 6,
                [new DateOnly(2026, 10, 2)] = 5
            };

            using var context = Loose();
            var wide = Render(context, new Week(), Monday12October, actual: actual, quarterWidth: WideQuarter);
            Assert.Equal("W40 · 33.1 / 42.5h", LabelOf(WeekColumn(wide, "Week 40")));
            Assert.Equal("W44 · 42.5h", LabelOf(WeekColumn(wide, "Week 44")));

            var narrow = Render(context, new Week(), Monday12October, actual: actual);
            Assert.Equal("W40", LabelOf(WeekColumn(narrow, "Week 40")));
            Assert.Equal("33.1 / 42.5", HoursLineOf(WeekColumn(narrow, "Week 40")));
            Assert.Equal("42.5h", HoursLineOf(WeekColumn(narrow, "Week 44")));
        });
    }

    /// <summary>Requirement "Heads fall back to planned hours": with no actual hours to
    /// show, every head reads as it did before there were any.</summary>
    [Fact]
    public void WithoutActualHours_EveryHeadReadsAsBefore()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var view = Render(context, new Week(), Monday12October, actual: null, quarterWidth: WideQuarter);

            Assert.Equal("Thu 8 · 8.5h", LabelOf(ColumnOf(view, Thursday8)));
            Assert.Equal(string.Empty, HoursLineOf(ColumnOf(view, Thursday8)));
            Assert.Equal("Sat 10", LabelOf(ColumnOf(view, Saturday10)));
            Assert.Equal("W40 · 42.5h", LabelOf(WeekColumn(view, "Week 40")));
            Assert.Equal("Thursday 8 October 2026 · 8.5h", ColumnOf(view, Thursday8).GetAttribute("title"));
        });
    }

    /// <summary>Monday 5 October 2026: the week of 5 to 11 October is this week, every
    /// date in it from Tuesday still to come.</summary>
    private static readonly DateOnly Monday12OctoberMinusAWeek = Monday12October.AddDays(-7);

    /// <summary>
    /// A working week as a host would hand one over: the default pattern, Monday to
    /// Friday at 8.5 hours with Saturday's stored hours kept for an unblocked Saturday,
    /// plus the dates the person toggled.
    /// </summary>
    private sealed class Week(double saturdayHours = 8.5)
    {
        private readonly HashSet<DateOnly> _overrides = [];

        public void Toggle(DateOnly date)
        {
            if (!_overrides.Remove(date)) _overrides.Add(date);
        }

        private static bool PatternWorks(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        public double PlannedOn(DateOnly date)
        {
            var worked = PatternWorks(date) != _overrides.Contains(date);
            if (!worked) return 0;
            return date.DayOfWeek == DayOfWeek.Saturday ? saturdayHours : 8.5;
        }

        public bool IsOverridden(DateOnly date) => _overrides.Contains(date);
    }

    private static BunitContext Loose()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static IRenderedComponent<RoadmapTimeline> Render(
        BunitContext context,
        Week week,
        DateOnly today,
        IReadOnlyDictionary<DateOnly, double>? actual = null,
        Action<DateOnly>? toggled = null,
        double quarterWidth = 16) =>
        context.Render<RoadmapTimeline>(parameters =>
        {
            parameters
                .Add(timeline => timeline.Groups, [new RoadmapGroup("backlog", "backlog", [new RoadmapRow("backlog::Planned", string.Empty)])])
                .Add(timeline => timeline.Bars, [new RoadmapBar("a", "backlog::Planned", "A", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 30))])
                .Add(timeline => timeline.Today, today)
                .Add(timeline => timeline.Graduated, true)
                .Add(timeline => timeline.QuarterWidth, quarterWidth)
                .Add(timeline => timeline.PlannedHoursOn, week.PlannedOn)
                .Add(timeline => timeline.IsOverridden, week.IsOverridden)
                .Add(timeline => timeline.ActualHoursByDate, actual);

            if (toggled is not null) parameters.Add(timeline => timeline.OnDayToggled, toggled);
        });

    /// <summary>A day column found by its tooltip's long date, whatever element it is.</summary>
    private static AngleSharp.Dom.IElement ColumnOf(IRenderedComponent<RoadmapTimeline> view, DateOnly date)
    {
        var title = date.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture);
        return Assert.Single(view.FindAll(".roadmap-timeline__quarter--day"), column => (column.GetAttribute("title") ?? string.Empty).StartsWith(title, StringComparison.Ordinal));
    }

    private static AngleSharp.Dom.IElement HeadButton(IRenderedComponent<RoadmapTimeline> view, DateOnly date)
    {
        var head = ColumnOf(view, date);
        Assert.Equal("BUTTON", head.TagName);
        return head;
    }

    private static AngleSharp.Dom.IElement WeekColumn(IRenderedComponent<RoadmapTimeline> view, string titleStart) =>
        Assert.Single(view.FindAll(".roadmap-timeline__quarter--week"), column => (column.GetAttribute("title") ?? string.Empty).StartsWith(titleStart, StringComparison.Ordinal));

    private static string LabelOf(AngleSharp.Dom.IElement column) =>
        column.QuerySelector(".roadmap-timeline__quarter-label")!.TextContent.Trim();

    private static string HoursLineOf(AngleSharp.Dom.IElement column) =>
        column.QuerySelector(".roadmap-timeline__quarter-hours")!.TextContent.Trim();

    /// <summary>The left and width a column's shade down the chart is drawn at — the
    /// column's own.</summary>
    private static string ShadeStyleOf(AngleSharp.Dom.IElement column) => column.GetAttribute("style")!;

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
