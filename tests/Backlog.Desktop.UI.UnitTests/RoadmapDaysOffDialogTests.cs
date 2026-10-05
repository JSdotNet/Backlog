using System.Globalization;

using Backlog.Modules.Roadmap.UI;
using Backlog.SharedKernel;
using Backlog.UI.Components.Roadmap;

using Bunit;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Days off dialog through the band (local ADR 0019, §4; requirements "Setting days
/// off in a list"): a Days off button on the toolbar opens it; it lists every date set off
/// the pattern, the ones to come first; a calendar flips a date with a press and takes a
/// run of days off with a Shift press, and the list removes an entry; every change is
/// written through the same port as a head press, at once, and the bars move while the
/// dialog stays open.
/// </summary>
public sealed class RoadmapDaysOffDialogTests : RoadmapBandHarness
{
    private static readonly DateOnly Monday12October = new(2026, 10, 12);
    private static readonly DateOnly Sunday18October = new(2026, 10, 18);
    private static readonly DateOnly Saturday17October = new(2026, 10, 17);
    private static readonly DateOnly Friday9October = new(2026, 10, 9);

    /// <summary>Requirement "The Days off button opens the dialog": with nothing set it
    /// says every date follows the pattern.</summary>
    [Fact]
    public async Task The_days_off_button_opens_the_dialog()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a");
        using var context = GatheringContext(14);
        var band = Drawn(context);

        Open(band, "roadmap-band-days-off", "roadmap-days-off");

        Assert.Equal("Days off", band.Find("[data-testid='roadmap-days-off'] .modal__title").TextContent.Trim());
        Assert.NotNull(band.Find("[data-testid='roadmap-days-off-none']"));
    }

    /// <summary>ADR 0019 Verification 35, through the calendar: Mon 12 pressed and Sun 18
    /// pressed with Shift blocks Monday to Friday and none of the weekend, and the dialog
    /// lists them.</summary>
    [Fact]
    public async Task A_shift_press_blocks_the_worked_dates_of_the_run()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a");
        using var context = GatheringContext(14);
        var band = Drawn(context);
        Open(band, "roadmap-band-days-off", "roadmap-days-off");

        Press(band, Monday12October);
        Press(band, Sunday18October, shift: true);

        Assert.Equal(
            [.. Enumerable.Range(0, 5).Select(offset => new DayOverride(Monday12October.AddDays(offset), false))],
            PaceFile.WorkingWeek.Overrides);
        band.WaitForAssertion(() => Assert.Equal(
            ["2026-10-12", "2026-10-13", "2026-10-14", "2026-10-15", "2026-10-16"],
            ListedDates(band)));
        Assert.All(band.FindAll(".roadmap-days-off__kind"), kind => Assert.Equal("Blocked", kind.TextContent.Trim()));
        Assert.Equal("true", Day(band, Sunday18October).GetAttribute("aria-pressed"));
    }

    /// <summary>Scenario "A range over an unblocked Saturday": a worked Saturday inside
    /// a Shift run loses its override, so it reads its pattern — a day off — again.</summary>
    [Fact]
    public async Task A_shift_run_removes_an_unblocked_saturday_inside_it()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a");
        Assert.Null(PaceFile.ToggleWorkedDay(Saturday17October));
        using var context = GatheringContext(14);
        var band = Drawn(context);
        Open(band, "roadmap-band-days-off", "roadmap-days-off");
        Assert.Equal(["2026-10-17"], ListedDates(band));

        Press(band, Monday12October);
        Press(band, Sunday18October, shift: true);

        Assert.Null(PaceFile.WorkingWeek.OverrideOn(Saturday17October));
        Assert.False(PaceFile.WorkingWeek.IsWorked(Saturday17October));
        band.WaitForAssertion(() => Assert.DoesNotContain("2026-10-17", ListedDates(band)));
    }

    /// <summary>ADR 0019 Verification 36, through the calendar: pressing a Saturday the
    /// pattern leaves off unblocks it — one override, listed with its weekday, and the
    /// date no longer pressed.</summary>
    [Fact]
    public async Task Pressing_a_saturday_unblocks_it()
    {
        await WithCulture("en-US", async () =>
        {
            Configure("JSdotNet/Backlog");
            await ImportedAsync("plan-a");
            using var context = GatheringContext(14);
            var band = Drawn(context);
            Open(band, "roadmap-band-days-off", "roadmap-days-off");
            Assert.Equal("true", Day(band, Saturday17October).GetAttribute("aria-pressed"));

            Press(band, Saturday17October);

            Assert.Equal([new DayOverride(Saturday17October, true)], PaceFile.WorkingWeek.Overrides);
            band.WaitForAssertion(() =>
            {
                var entry = band.Find("[data-testid='roadmap-days-off-entry-2026-10-17']");
                Assert.Equal("Sat 17 Oct", entry.QuerySelector(".roadmap-days-off__date")!.TextContent.Trim());
                Assert.Equal("Unblocked", entry.QuerySelector(".roadmap-days-off__kind")!.TextContent.Trim());
                Assert.Equal("false", Day(band, Saturday17October).GetAttribute("aria-pressed"));
                Assert.Contains("day-calendar__day--marked", Day(band, Saturday17October).ClassName);
            });
        });
    }

    /// <summary>A press selects a worked date as a day off, and a second press deselects
    /// it, returning it to its pattern with nothing stored.</summary>
    [Fact]
    public async Task A_second_press_deselects_the_day_off()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a");
        using var context = GatheringContext(14);
        var band = Drawn(context);
        Open(band, "roadmap-band-days-off", "roadmap-days-off");

        Press(band, Friday9October);
        Assert.Equal([new DayOverride(Friday9October, false)], PaceFile.WorkingWeek.Overrides);
        band.WaitForAssertion(() => Assert.Equal("true", Day(band, Friday9October).GetAttribute("aria-pressed")));

        Press(band, Friday9October);

        Assert.Empty(PaceFile.WorkingWeek.Overrides);
        band.WaitForAssertion(() =>
        {
            Assert.Equal("false", Day(band, Friday9October).GetAttribute("aria-pressed"));
            Assert.Empty(ListedDates(band));
        });
    }

    /// <summary>ADR 0019 Verification 37: removing the blocked Friday 9 October empties
    /// the set, and its head looks like an ordinary day head again.</summary>
    [Fact]
    public async Task Removing_an_entry_returns_the_date_to_its_pattern()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a");
        Assert.Null(PaceFile.ToggleWorkedDay(Friday9October));
        using var context = GatheringContext(14);
        var band = Drawn(context, new DateOnly(2026, 10, 5));
        Assert.Contains("roadmap-timeline__quarter--weekend", DayHead(band, Friday9October).ClassName);
        Open(band, "roadmap-band-days-off", "roadmap-days-off");

        band.Find("[data-testid='roadmap-days-off-remove-2026-10-09']").Click();

        Assert.Empty(PaceFile.WorkingWeek.Overrides);
        band.WaitForAssertion(() =>
        {
            Assert.Empty(band.FindAll("[data-testid='roadmap-days-off-entry-2026-10-09']"));
            var head = DayHead(band, Friday9October);
            Assert.DoesNotContain("roadmap-timeline__quarter--weekend", head.ClassName);
            Assert.DoesNotContain("roadmap-timeline__quarter--override", head.ClassName);
        });
        Assert.NotNull(band.Find("[data-testid='roadmap-days-off']"));
    }

    /// <summary>
    /// ADR 0019 Verification 38, scenarios "Future overrides first, past ones hidden" and
    /// "Showing the past": with today Saturday 3 October, the dates still to come are
    /// listed soonest first and the past one only after "Show past", after them.
    /// </summary>
    [Fact]
    public async Task The_list_puts_the_future_first_and_the_past_behind_show_past()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a");
        Assert.Null(PaceFile.ToggleWorkedDay(Saturday17October));
        Assert.Null(PaceFile.ToggleWorkedDay(new DateOnly(2026, 9, 25)));
        Assert.Null(PaceFile.ToggleWorkedDay(new DateOnly(2026, 9, 18)));
        Assert.Null(PaceFile.ToggleWorkedDay(Friday9October));
        using var context = GatheringContext(14);
        var band = Drawn(context, new DateOnly(2026, 10, 3));
        Open(band, "roadmap-band-days-off", "roadmap-days-off");

        Assert.Equal(["2026-10-09", "2026-10-17"], ListedDates(band));

        var showPast = band.Find("[data-testid='roadmap-days-off-show-past']");
        Assert.Equal("Show past (2)", showPast.TextContent.Trim());
        Assert.Equal("false", showPast.GetAttribute("aria-expanded"));
        showPast.Click();

        band.WaitForAssertion(() => Assert.Equal(["2026-10-09", "2026-10-17", "2026-09-25", "2026-09-18"], ListedDates(band)));
        Assert.Equal("true", band.Find("[data-testid='roadmap-days-off-show-past']").GetAttribute("aria-expanded"));
    }

    /// <summary>ADR 0019 Verification 39, scenario "A plan moves at once": a day off added
    /// inside an effort-placed window lengthens the bar without a reload, and the dialog
    /// stays open over it.</summary>
    [Fact]
    public async Task A_day_off_added_in_the_dialog_moves_the_bars_and_the_dialog_stays_open()
    {
        Configure("JSdotNet/Backlog");
        var plan = await ImportedAsync("plan-a");
        using var context = GatheringContext(14);
        var band = Drawn(context);

        // 14 points at Mine's 7 a week: two working weeks, Friday 25 September to
        // Thursday 8 October.
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 14), DrawnWindow(band, plan.Id)));
        Open(band, "roadmap-band-days-off", "roadmap-days-off");

        var monday28 = new DateOnly(2026, 9, 28);
        Press(band, monday28);

        band.WaitForAssertion(() => Assert.Equal((plan.Start, 15), DrawnWindow(band, plan.Id)));
        Assert.NotNull(band.Find("[data-testid='roadmap-days-off']"));
        band.WaitForAssertion(() => Assert.Equal(["2026-09-28"], ListedDates(band)));
        Assert.Contains("roadmap-timeline__quarter--override", DayHead(band, monday28).ClassName);
    }

    /// <summary>A Shift run longer than a year is refused with the cap in its message,
    /// and nothing but the first press is written.</summary>
    [Fact]
    public async Task A_shift_run_longer_than_a_year_is_refused()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a");
        using var context = GatheringContext(14);
        var band = Drawn(context);
        Open(band, "roadmap-band-days-off", "roadmap-days-off");

        Press(band, Monday12October);
        Press(band, Monday12October.AddDays(400), shift: true);

        band.WaitForAssertion(() => Assert.Equal(
            "Take at most 366 days off at a time.",
            band.Find("[data-testid='roadmap-days-off-calendar-error']").TextContent.Trim()));
        Assert.Equal([new DayOverride(Monday12October, false)], PaceFile.WorkingWeek.Overrides);
    }

    /// <summary>Presses <paramref name="date"/> on the calendar, paging to its month first.</summary>
    private static void Press(IRenderedComponent<RoadmapBand> band, DateOnly date, bool shift = false) =>
        Day(band, date).Click(new MouseEventArgs { ShiftKey = shift });

    /// <summary>The calendar's button for <paramref name="date"/>, paging to its month.</summary>
    private static AngleSharp.Dom.IElement Day(IRenderedComponent<RoadmapBand> band, DateOnly date)
    {
        const string prefix = "roadmap-days-off-calendar-";

        for (var page = 0; page < 36; page++)
        {
            if (Find(band, $"[data-testid='{prefix}{date:yyyy-MM-dd}']") is { } day) return day;

            var shown = DateOnly.ParseExact(
                band.Find($"[data-testid^='{prefix}2']").GetAttribute("data-testid")![prefix.Length..],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);
            band.Find($"[data-testid='{prefix}{(date < shown ? "previous" : "next")}']").Click();
        }

        throw new InvalidOperationException($"The calendar never showed {date:yyyy-MM-dd}.");
    }

    /// <summary>The ISO dates the dialog lists, top to bottom.</summary>
    private static List<string> ListedDates(IRenderedComponent<RoadmapBand> band) =>
        [.. band.FindAll(".roadmap-days-off__entry time").Select(time => time.GetAttribute("datetime") ?? string.Empty)];

    private static AngleSharp.Dom.IElement? Find(IRenderedComponent<RoadmapBand> band, string selector) =>
        band.FindAll(selector).FirstOrDefault();

    private static IRenderedComponent<RoadmapBand> Drawn(BunitContext context, DateOnly? today = null)
    {
        var band = context.Render<RoadmapBand>(parameters => parameters.Add(view => view.Today, today ?? PaceToday));
        band.WaitForElement("[data-testid='roadmap-timeline']");
        return band;
    }

    private static AngleSharp.Dom.IElement DayHead(IRenderedComponent<RoadmapBand> band, DateOnly date) =>
        band.Find($"[data-testid='roadmap-timeline-day-{date:yyyy-MM-dd}']");

    /// <summary>Where the chart draws an item: the first day of its first bar and how
    /// many days its bars cover.</summary>
    private static (DateOnly Start, int Days) DrawnWindow(IRenderedComponent<RoadmapBand> band, Guid itemId)
    {
        var bars = band.FindComponent<RoadmapTimeline>().Instance.Bars
            .Where(bar => RoadmapPlanView.NodeIdOf(bar.Id) == itemId)
            .ToList();
        Assert.NotEmpty(bars);

        var start = bars.Min(bar => bar.Start);
        return (start, bars.Max(bar => bar.End).DayNumber - start.DayNumber + 1);
    }

    private BunitContext GatheringContext(int totalEffort)
    {
        var context = Context();
        context.Services.AddSingleton<IRoadmapItemRollup>(new EveryItemGathers(totalEffort));
        return context;
    }

    private async Task<RoadmapItemDto> ImportedAsync(string tag)
    {
        var imported = await Planning.ImportPlanItemsAsync(
            [new PlanImportEntryDto("Plan", tag, RepositoryAliases: ["backlog"])],
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(imported.IsSuccess);

        return Assert.Single(
            (await Planning.GetPlanAsync(TestContext.Current.CancellationToken)).Items,
            item => item.Tag == tag);
    }

    private static async Task WithCulture(string name, Func<Task> test)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(name);
        try
        {
            await test();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private sealed class EveryItemGathers(int totalEffort) : IRoadmapItemRollup
    {
        private RoadmapItemRollupDto Rollup => new(
            [new RoadmapGatheredLink("task-1", "Gathered task", totalEffort, RollupOrigin.Tag)],
            []);

        public Task<RoadmapItemRollupDto> GatherAsync(RoadmapItemDto item, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rollup);

        public Task<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>> GatherPlanAsync(
            RoadmapPlanDto plan,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>>(
                plan.Items.ToDictionary(item => item.Id, _ => Rollup));
    }
}
