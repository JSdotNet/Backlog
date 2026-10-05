using System.Globalization;

using Microsoft.JSInterop;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The graduated axis read date by date (local ADR 0019, §§4 to 6): a day head is a
/// button that blocks or unblocks its date, a date that breaks its weekday's pattern is
/// marked, a date not worked looks exactly like a weekend, and each head shows one hours
/// figure — the hours actually worked on a head that has begun, the hours planned on one
/// still to come — behind the host's Hours switch.
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

    /// <summary>A day 6rem wide: room to say "Thu 8 · 6.2h" on one line.</summary>
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

    // --- How a head looks ---------------------------------------------------------------

    /// <summary>
    /// ADR 0019 Verification 34, requirement "A date that is not worked is hatched": a
    /// blocked Wednesday's head wears exactly the classes a pattern Saturday's head wears,
    /// plus the marker, and is still the button that unblocks it. An unblocked Saturday's
    /// head wears a Friday head's classes, plus the marker.
    /// </summary>
    [Fact]
    public void ABlockedHead_WearsAWeekendHeadsClasses_AndAnUnblockedOneAWeekdays()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var week = new Week();
            var wednesday7 = new DateOnly(2026, 10, 7);
            week.Toggle(wednesday7);
            week.Toggle(Saturday10);
            var view = Render(context, week, Monday12OctoberMinusAWeek, toggled: _ => { });

            var blocked = ClassesOf(ColumnOf(view, wednesday7));
            Assert.Equal(ClassesOf(ColumnOf(view, Sunday11)).Append("roadmap-timeline__quarter--override").Order(), blocked.Order());
            Assert.Equal("Unblock Wed 7 Oct", ColumnOf(view, wednesday7).GetAttribute("aria-label"));

            var unblocked = ClassesOf(ColumnOf(view, Saturday10));
            Assert.Equal(ClassesOf(ColumnOf(view, Friday9)).Append("roadmap-timeline__quarter--override").Order(), unblocked.Order());
            Assert.Equal("Block Sat 10 Oct", ColumnOf(view, Saturday10).GetAttribute("aria-label"));
        });
    }

    /// <summary>Requirement "A day head does not look like a button": a worked day's head
    /// wears the head's own classes and the toggle hook, and no class of the library's
    /// button — no <c>btn</c>, no variant — so nothing but the toggle rule's reset styles
    /// it as anything other than the plain head beside it.</summary>
    [Fact]
    public void AWorkedHead_WearsNoButtonChrome()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var view = Render(context, new Week(), Monday12OctoberMinusAWeek, toggled: _ => { });

            Assert.Equal(
                ["roadmap-timeline__quarter", "roadmap-timeline__quarter--day", "roadmap-timeline__quarter--toggle"],
                ClassesOf(HeadButton(view, Thursday8)));
            Assert.All(view.FindAll(".roadmap-timeline__axis button"), head =>
                Assert.DoesNotContain(ClassesOf(head), name => name == "btn" || name.StartsWith("btn--", StringComparison.Ordinal)));
        });
    }

    // --- One hours figure per head -------------------------------------------------------

    /// <summary>
    /// ADR 0019 Verification 18 and scenario "A past day": stretches of 6.2 hours on
    /// Thursday 8 October read "6.2h", the actual alone — never "6.2 / 8.5h" — inline where
    /// the column is wide and on the hours line where it is not; and the tooltip carries
    /// both figures.
    /// </summary>
    [Fact]
    public void APastWorkedDay_ReadsItsActualHoursAlone()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var actual = new Dictionary<DateOnly, double> { [Thursday8] = 6.2 };

            var wide = Render(context, new Week(), Monday12October, actual: actual, quarterWidth: WideQuarter);
            Assert.Equal("Thu 8 · 6.2h", LabelOf(ColumnOf(wide, Thursday8)));
            Assert.Equal(string.Empty, HoursLineOf(ColumnOf(wide, Thursday8)));
            Assert.Equal("Thursday 8 October 2026 · 6.2h worked of 8.5h planned", ColumnOf(wide, Thursday8).GetAttribute("title"));

            var narrow = Render(context, new Week(), Monday12October, actual: actual);
            Assert.Equal("8", LabelOf(ColumnOf(narrow, Thursday8)));
            Assert.Equal("6.2h", HoursLineOf(ColumnOf(narrow, Thursday8)));
            Assert.Equal("Thursday 8 October 2026 · 6.2h worked of 8.5h planned", ColumnOf(narrow, Thursday8).GetAttribute("title"));
        });
    }

    /// <summary>ADR 0019 Verification 28 and scenario "A past worked day with no
    /// stretches": a begun worked day nobody worked reads "0h".</summary>
    [Fact]
    public void APastWorkedDayNobodyWorked_ReadsZero()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var view = Render(context, new Week(), Monday12October, actual: new Dictionary<DateOnly, double>());

            Assert.Equal("0h", HoursLineOf(ColumnOf(view, Friday9)));
            Assert.Equal("Friday 9 October 2026 · 0h worked of 8.5h planned", ColumnOf(view, Friday9).GetAttribute("title"));
        });
    }

    /// <summary>Scenario "Today": today has begun, and counts the hours so far.</summary>
    [Fact]
    public void Today_ReadsTheHoursSoFar()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var actual = new Dictionary<DateOnly, double> { [Monday12October] = 3 };
            var view = Render(context, new Week(), Monday12October, actual: actual);

            Assert.Equal("3.0h", HoursLineOf(ColumnOf(view, Monday12October)));
            Assert.Equal("Monday 12 October 2026 (today) · 3.0h worked of 8.5h planned", ColumnOf(view, Monday12October).GetAttribute("title"));
        });
    }

    /// <summary>ADR 0019 Verification 28, scenario "A past day not worked": two hours on
    /// Saturday 10 October read "2.0h"; Sunday 11 October, with none, reads nothing, as a
    /// day off always has.</summary>
    [Fact]
    public void APastDayOff_WithActivity_ReadsItsActualHours()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var actual = new Dictionary<DateOnly, double> { [Saturday10] = 2 };

            var wide = Render(context, new Week(), Monday12October, actual: actual, quarterWidth: WideQuarter);
            var saturday = ColumnOf(wide, Saturday10);
            Assert.Equal("Sat 10 · 2.0h", LabelOf(saturday));
            Assert.Contains("roadmap-timeline__quarter--weekend", saturday.ClassName);
            Assert.Equal("Saturday 10 October 2026 · not worked · 2.0h worked", saturday.GetAttribute("title"));
            Assert.Equal("Sun 11", LabelOf(ColumnOf(wide, Sunday11)));
            Assert.Equal("Sunday 11 October 2026 · not worked", ColumnOf(wide, Sunday11).GetAttribute("title"));

            var narrow = Render(context, new Week(), Monday12October, actual: actual);
            Assert.Equal("2.0h", HoursLineOf(ColumnOf(narrow, Saturday10)));
            Assert.Equal(string.Empty, HoursLineOf(ColumnOf(narrow, Sunday11)));
        });
    }

    /// <summary>ADR 0019 Verification 26, scenario "A day still to come": a worked date
    /// after today reads its planned hours alone and its tooltip keeps the wording it had;
    /// so does a week still to come.</summary>
    [Fact]
    public void AHeadStillToCome_ReadsItsPlannedHoursAlone()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var tuesday = Monday12October.AddDays(1);
            var actual = new Dictionary<DateOnly, double> { [Thursday8] = 6.2, [tuesday] = 4 };

            var wide = Render(context, new Week(), Monday12October, actual: actual, quarterWidth: WideQuarter);
            Assert.Equal("Tue 13 · 8.5h", LabelOf(ColumnOf(wide, tuesday)));
            Assert.Equal("Tuesday 13 October 2026 · 8.5h", ColumnOf(wide, tuesday).GetAttribute("title"));
            Assert.Equal("W44 · 42.5h", LabelOf(WeekColumn(wide, "Week 44")));

            var narrow = Render(context, new Week(), Monday12October, actual: actual);
            Assert.Equal("8.5h", HoursLineOf(ColumnOf(narrow, tuesday)));
            Assert.Equal("42.5h", HoursLineOf(WeekColumn(narrow, "Week 44")));
        });
    }

    /// <summary>A week that has begun reads the actual hours of its dates up to today,
    /// alone — "33.1h" — and its tooltip carries both figures.</summary>
    [Fact]
    public void ABegunWeek_ReadsTheActualHoursOfItsDates()
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
            Assert.Equal("W40 · 33.1h", LabelOf(WeekColumn(wide, "Week 40")));

            var narrow = Render(context, new Week(), Monday12October, actual: actual);
            Assert.Equal("W40", LabelOf(WeekColumn(narrow, "Week 40")));
            Assert.Equal("33.1h", HoursLineOf(WeekColumn(narrow, "Week 40")));
            Assert.EndsWith(" · 33.1h worked of 42.5h planned", WeekColumn(narrow, "Week 40").GetAttribute("title"), StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// At the band's default width every head carries exactly three lines — label,
    /// caption, hours — and no head says two figures, inline or stacked: the fourth line
    /// the stacked form needed, and the axis height it took, are gone. Every figure fits
    /// its column by the head's own measure.
    /// </summary>
    [Fact]
    public void AtTheDefaultWidth_EveryHeadSaysOneFigure()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var actual = new Dictionary<DateOnly, double>
            {
                [Thursday8] = 5.1,
                [Friday9] = 13.1,
                [Saturday10] = 2,
                [new DateOnly(2026, 9, 29)] = 8
            };
            var view = Render(context, new Week(), Monday12October, actual: actual, toggled: _ => { });

            Assert.Equal("roadmap-timeline roadmap-timeline--hours", Normalised(view.Find("section.roadmap-timeline").ClassName));
            Assert.Empty(view.FindAll(".roadmap-timeline__quarter-actual"));

            var heads = view.FindAll(".roadmap-timeline__quarter").Where(head => head.QuerySelector(".roadmap-timeline__quarter-label") is not null).ToList();
            Assert.NotEmpty(heads);
            Assert.All(heads, head =>
            {
                Assert.Equal(
                    ["roadmap-timeline__quarter-label", "roadmap-timeline__quarter-year", "roadmap-timeline__quarter-hours"],
                    head.Children.Select(line => line.ClassName).ToList());
                Assert.DoesNotContain("/", head.TextContent, StringComparison.Ordinal);
            });

            Assert.Equal("5.1h", HoursLineOf(ColumnOf(view, Thursday8)));
            Assert.Equal("13.1", HoursLineOf(ColumnOf(view, Friday9))); // "13.1h" does not fit a 2rem day: the unit goes
            Assert.Equal("2.0h", HoursLineOf(ColumnOf(view, Saturday10)));

            Assert.All(view.FindAll(".roadmap-timeline__quarter--day"), day =>
                Assert.True(CaptionRem(HoursLineOf(day)) <= 2 - 0.0625, $"{HoursLineOf(day)} overflows a day"));
        });
    }

    /// <summary>
    /// ADR 0019 Verification 33, requirement "A begun head shows no hours when actual
    /// hours cannot be read": with no actual hours, today and every earlier head show no
    /// figure — never their planned hours — and heads still to come keep theirs. The
    /// tooltip names the planned hours as planned.
    /// </summary>
    [Fact]
    public void WithoutActualHours_BegunHeadsShowNoFigure()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var tuesday = Monday12October.AddDays(1);

            var narrow = Render(context, new Week(), Monday12October, actual: null);
            Assert.Equal(string.Empty, HoursLineOf(ColumnOf(narrow, Thursday8)));
            Assert.Equal(string.Empty, HoursLineOf(ColumnOf(narrow, Monday12October)));
            Assert.Equal(string.Empty, HoursLineOf(WeekColumn(narrow, "Week 40")));
            Assert.Equal("8.5h", HoursLineOf(ColumnOf(narrow, tuesday)));
            Assert.Equal("42.5h", HoursLineOf(WeekColumn(narrow, "Week 44")));
            Assert.Equal("Thursday 8 October 2026 · 8.5h planned", ColumnOf(narrow, Thursday8).GetAttribute("title"));

            var wide = Render(context, new Week(), Monday12October, actual: null, quarterWidth: WideQuarter);
            Assert.Equal("Thu 8", LabelOf(ColumnOf(wide, Thursday8)));
            Assert.Equal("W40", LabelOf(WeekColumn(wide, "Week 40")));
            Assert.Equal("Tue 13 · 8.5h", LabelOf(ColumnOf(wide, tuesday)));
        });
    }

    /// <summary>
    /// ADR 0019 Verification 32, requirement "The Hours switch shows or hides the hours
    /// line": switched off, no day or week head shows an hours figure, inline or on a line
    /// of its own, the axis loses the line, and every column keeps its width. The days off
    /// stay shaded and the tooltips still say the hours.
    /// </summary>
    [Fact]
    public void TheHoursSwitchedOff_NoHeadShowsAFigure()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var actual = new Dictionary<DateOnly, double> { [Thursday8] = 6.2 };

            var on = Render(context, new Week(), Monday12October, actual: actual, quarterWidth: WideQuarter);
            var off = Render(context, new Week(), Monday12October, actual: actual, quarterWidth: WideQuarter, showHours: false);

            Assert.Empty(off.FindAll(".roadmap-timeline__quarter-hours"));
            Assert.DoesNotContain("roadmap-timeline--hours", off.Find("section.roadmap-timeline").ClassName);
            Assert.Equal("Thu 8", LabelOf(ColumnOf(off, Thursday8)));
            Assert.Equal("Tue 13", LabelOf(ColumnOf(off, Monday12October.AddDays(1))));
            Assert.Equal("W44", LabelOf(WeekColumn(off, "Week 44")));
            Assert.All(off.FindAll(".roadmap-timeline__quarter-label"), label => Assert.DoesNotContain(" · ", label.TextContent, StringComparison.Ordinal));

            Assert.Contains("roadmap-timeline__quarter--weekend", ColumnOf(off, Saturday10).ClassName);
            Assert.Equal("Thursday 8 October 2026 · 6.2h worked of 8.5h planned", ColumnOf(off, Thursday8).GetAttribute("title"));

            Assert.Equal(
                on.FindAll(".roadmap-timeline__quarter").Select(column => column.GetAttribute("style")),
                off.FindAll(".roadmap-timeline__quarter").Select(column => column.GetAttribute("style")));
        });
    }

    /// <summary>
    /// A begun head's actual hours open the report behind them where the host listens: a
    /// button of its own beside the day head's button, never inside it, named for what it
    /// opens, and pressing it raises the column's date without toggling the day.
    /// </summary>
    [Fact]
    public void A_begun_heads_hours_are_a_button_that_opens_the_report()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var opened = new List<DateOnly>();
            var toggled = new List<DateOnly>();
            var actual = new Dictionary<DateOnly, double> { [Thursday8] = 6.2 };

            var view = Render(context, new Week(), Monday12October, actual: actual, toggled: toggled.Add, opened: opened.Add);

            var hours = view.Find("#roadmap-hours-2026-10-08");
            Assert.Equal("BUTTON", hours.TagName);
            Assert.Equal("6.2h", hours.TextContent.Trim());
            Assert.Equal("Hours worked on Thu 8 Oct, 6.2h, show the stretches", hours.GetAttribute("aria-label"));
            Assert.Null(hours.Closest("button.roadmap-timeline__quarter--toggle"));

            hours.Click();

            Assert.Equal([Thursday8], opened);
            Assert.Empty(toggled);
        });
    }

    /// <summary>A wide head that would say its hours beside its name keeps them on their
    /// own line once they can be pressed, and the head's own line is not read twice.</summary>
    [Fact]
    public void An_openable_head_says_its_hours_on_their_own_line_once()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var actual = new Dictionary<DateOnly, double> { [Thursday8] = 6.2 };

            var view = Render(context, new Week(), Monday12October, actual: actual, quarterWidth: WideQuarter, opened: _ => { });

            var head = ColumnOf(view, Thursday8);
            Assert.Equal("Thu 8", LabelOf(head));
            Assert.Equal("true", head.QuerySelector(".roadmap-timeline__quarter-hours")!.GetAttribute("aria-hidden"));
            Assert.Equal("6.2h", view.Find("#roadmap-hours-2026-10-08").TextContent.Trim());
        });
    }

    /// <summary>A head still to come, and every head while the actual hours are unread
    /// or the host does not listen, has no hours button.</summary>
    [Fact]
    public void Only_a_begun_head_with_read_hours_offers_the_report()
    {
        WithCulture("en-US", () =>
        {
            using var context = Loose();
            var actual = new Dictionary<DateOnly, double> { [Thursday8] = 6.2 };

            var listening = Render(context, new Week(), Monday12October, actual: actual, opened: _ => { });
            Assert.Empty(listening.FindAll("#roadmap-hours-2026-10-13"));

            var unread = Render(context, new Week(), Monday12October, opened: _ => { });
            Assert.Empty(unread.FindAll(".roadmap-timeline__hours-open"));

            var deaf = Render(context, new Week(), Monday12October, actual: actual);
            Assert.Empty(deaf.FindAll(".roadmap-timeline__hours-open"));
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
        double quarterWidth = 16,
        bool showHours = true,
        Action<DateOnly>? opened = null) =>
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
                .Add(timeline => timeline.ActualHoursByDate, actual)
                .Add(timeline => timeline.ShowHours, showHours);

            if (toggled is not null) parameters.Add(timeline => timeline.OnDayToggled, toggled);
            if (opened is not null) parameters.Add(timeline => timeline.OnHoursOpened, opened);
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

    /// <summary>The classes a head wears, in the order it wears them.</summary>
    private static List<string> ClassesOf(AngleSharp.Dom.IElement head) => [.. head.ClassList];

    /// <summary>A class attribute with its runs of spaces collapsed.</summary>
    private static string Normalised(string? classes) =>
        string.Join(' ', (classes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A caption's width in rem at the caption size, by the head's own measure:
    /// 0.65em a digit or letter, 0.3em a point, at 0.75rem.</summary>
    private static double CaptionRem(string text) => text.Sum(character => character == '.' ? 0.3 : 0.65) * 0.75;

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
