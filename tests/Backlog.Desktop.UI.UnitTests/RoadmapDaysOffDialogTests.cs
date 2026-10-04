using System.Globalization;

using Backlog.Modules.Roadmap.UI;
using Backlog.SharedKernel;
using Backlog.UI.Components.Roadmap;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Days off dialog through the band (local ADR 0019, §4; requirements "Setting days
/// off in a list"): a Days off button on the toolbar opens it; it lists every date set off
/// the pattern, the ones to come first; it adds a range of days off and a single worked
/// day, and removes an entry; every change is written through the same port as a head
/// press, at once, and the bars move while the dialog stays open.
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

    /// <summary>ADR 0019 Verification 35: on the default week, Mon 12 to Sun 18 October
    /// adds five blocked dates, Monday to Friday, and none for the weekend — one write,
    /// one change heard by the band — and the dialog lists them.</summary>
    [Fact]
    public async Task A_range_blocks_only_the_worked_dates_in_one_change()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a");
        using var context = GatheringContext(14);
        var band = Drawn(context);
        var heard = 0;
        PaceFile.Changed += () => heard++;
        Open(band, "roadmap-band-days-off", "roadmap-days-off");

        AddDaysOff(band, Monday12October, Sunday18October);

        Assert.Equal(1, heard);
        Assert.Equal(
            [.. Enumerable.Range(0, 5).Select(offset => new DayOverride(Monday12October.AddDays(offset), false))],
            PaceFile.WorkingWeek.Overrides);
        band.WaitForAssertion(() => Assert.Equal(
            ["2026-10-12", "2026-10-13", "2026-10-14", "2026-10-15", "2026-10-16"],
            ListedDates(band)));
        Assert.All(band.FindAll(".roadmap-days-off__kind"), kind => Assert.Equal("Blocked", kind.TextContent.Trim()));
        Assert.Null(Find(band, "[data-testid='roadmap-days-off-from'] .field__error"));
    }

    /// <summary>Scenario "A range over an unblocked Saturday": a worked Saturday inside
    /// the range loses its override, so it reads its pattern — a day off — again.</summary>
    [Fact]
    public async Task A_range_removes_an_unblocked_saturday_inside_it()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a");
        Assert.Null(PaceFile.ToggleWorkedDay(Saturday17October));
        using var context = GatheringContext(14);
        var band = Drawn(context);
        Open(band, "roadmap-band-days-off", "roadmap-days-off");
        Assert.Equal(["2026-10-17"], ListedDates(band));

        AddDaysOff(band, Monday12October, Sunday18October);

        Assert.Null(PaceFile.WorkingWeek.OverrideOn(Saturday17October));
        Assert.False(PaceFile.WorkingWeek.IsWorked(Saturday17October));
        band.WaitForAssertion(() => Assert.DoesNotContain("2026-10-17", ListedDates(band)));
    }

    /// <summary>ADR 0019 Verification 36: a worked Saturday is one unblocked override,
    /// listed with its weekday.</summary>
    [Fact]
    public async Task Adding_a_worked_saturday_unblocks_it()
    {
        await WithCulture("en-US", async () =>
        {
            Configure("JSdotNet/Backlog");
            await ImportedAsync("plan-a");
            using var context = GatheringContext(14);
            var band = Drawn(context);
            Open(band, "roadmap-band-days-off", "roadmap-days-off");

            Input(band, "roadmap-days-off-worked-date", Saturday17October);
            band.Find("[data-testid='roadmap-days-off-add-worked']").Click();

            Assert.Equal([new DayOverride(Saturday17October, true)], PaceFile.WorkingWeek.Overrides);
            band.WaitForAssertion(() =>
            {
                var entry = band.Find("[data-testid='roadmap-days-off-entry-2026-10-17']");
                Assert.Equal("Sat 17 Oct", entry.QuerySelector(".roadmap-days-off__date")!.TextContent.Trim());
                Assert.Equal("Unblocked", entry.QuerySelector(".roadmap-days-off__kind")!.TextContent.Trim());
            });
        });
    }

    /// <summary>Scenario "A date the pattern already works": adding a weekday as worked
    /// adds no override, and the dialog says why.</summary>
    [Fact]
    public async Task Adding_a_weekday_as_worked_adds_nothing_and_says_why()
    {
        await WithCulture("en-US", async () =>
        {
            Configure("JSdotNet/Backlog");
            await ImportedAsync("plan-a");
            using var context = GatheringContext(14);
            var band = Drawn(context);
            Open(band, "roadmap-band-days-off", "roadmap-days-off");

            Input(band, "roadmap-days-off-worked-date", new DateOnly(2026, 10, 8));
            band.Find("[data-testid='roadmap-days-off-add-worked']").Click();

            Assert.Empty(PaceFile.WorkingWeek.Overrides);
            band.WaitForAssertion(() => Assert.Equal(
                "Thu 8 Oct is already a working day in your week, so nothing was added.",
                band.Find("[data-testid='roadmap-days-off-note']").TextContent.Trim()));
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
        AddDaysOff(band, monday28, monday28);

        band.WaitForAssertion(() => Assert.Equal((plan.Start, 15), DrawnWindow(band, plan.Id)));
        Assert.NotNull(band.Find("[data-testid='roadmap-days-off']"));
        band.WaitForAssertion(() => Assert.Equal(["2026-09-28"], ListedDates(band)));
        Assert.Contains("roadmap-timeline__quarter--override", DayHead(band, monday28).ClassName);
    }

    /// <summary>A range that ends before it starts is refused beside the field, and
    /// nothing is written.</summary>
    [Fact]
    public async Task A_range_ending_before_it_starts_is_refused()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a");
        using var context = GatheringContext(14);
        var band = Drawn(context);
        var heard = 0;
        PaceFile.Changed += () => heard++;
        Open(band, "roadmap-band-days-off", "roadmap-days-off");

        AddDaysOff(band, Sunday18October, Monday12October);

        band.WaitForAssertion(() => Assert.Equal(
            "End the days off on or after the day they start.",
            band.Find("[data-testid='roadmap-days-off-from'] .field__error").TextContent.Trim()));
        Assert.Equal(0, heard);
        Assert.Empty(PaceFile.WorkingWeek.Overrides);
    }

    /// <summary>A range longer than a year is refused with the cap in its message.</summary>
    [Fact]
    public async Task A_range_longer_than_a_year_is_refused()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a");
        using var context = GatheringContext(14);
        var band = Drawn(context);
        Open(band, "roadmap-band-days-off", "roadmap-days-off");

        AddDaysOff(band, Monday12October, Monday12October.AddDays(400));

        band.WaitForAssertion(() => Assert.Equal(
            "Add at most 366 days off at a time.",
            band.Find("[data-testid='roadmap-days-off-from'] .field__error").TextContent.Trim()));
        Assert.Empty(PaceFile.WorkingWeek.Overrides);
    }

    private static void AddDaysOff(IRenderedComponent<RoadmapBand> band, DateOnly from, DateOnly through)
    {
        Input(band, "roadmap-days-off-from", from);
        Input(band, "roadmap-days-off-to", through);
        band.Find("[data-testid='roadmap-days-off-add']").Click();
    }

    private static void Input(IRenderedComponent<RoadmapBand> band, string testId, DateOnly date) =>
        band.Find($"[data-testid='{testId}'] input").Input(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

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
