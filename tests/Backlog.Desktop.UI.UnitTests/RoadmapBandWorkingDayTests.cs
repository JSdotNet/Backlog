using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Roadmap;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The roadmap's day heads through the band (local ADR 0019, §§4 to 6): pressing one
/// blocks or unblocks its date in the working week the pace carries, which re-places
/// every bar sized by its effort at once; and a head that has begun shows the hours
/// agents were actually active, or its planned hours alone when those cannot be read.
/// </summary>
public sealed class RoadmapBandWorkingDayTests : RoadmapBandHarness
{
    /// <summary>Monday 28 September 2026, a worked weekday inside the plan's window, and
    /// on a day column: today is Friday 25 September, so 14 September to 4 October is
    /// ruled a column a day.</summary>
    private static readonly DateOnly Monday28September = new(2026, 9, 28);

    /// <summary>Thursday 24 September 2026, the day before today.</summary>
    private static readonly DateOnly Thursday24September = new(2026, 9, 24);

    /// <summary>
    /// ADR 0019 Verification 23, and requirement "A day head is a toggle": pressing a
    /// worked day's head inside an effort-placed window blocks the date, and the bar
    /// lengthens by one worked date without a reload — the pace changed, and the band
    /// re-reads on that as it does for a typed pace.
    /// </summary>
    [Fact]
    public async Task Pressing_a_day_head_blocks_the_date_and_the_bar_lengthens_at_once()
    {
        Configure("JSdotNet/Backlog");
        var plan = await ImportedAsync("plan-a");

        using var context = GatheringContext(14);
        var band = Drawn(context, PaceToday);

        // 14 points at Mine's 7 a week: two working weeks, Friday 25 September to
        // Thursday 8 October.
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 14), DrawnWindow(band, plan.Id)));
        Assert.Equal(Named("Block", Monday28September), DayHead(band, Monday28September).GetAttribute("aria-label"));

        DayHead(band, Monday28September).Click();

        // One worked date fewer inside it, so it ends one worked date later: Friday 9 October.
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 15), DrawnWindow(band, plan.Id)));
        band.WaitForAssertion(() =>
        {
            var head = DayHead(band, Monday28September);
            Assert.Equal(Named("Unblock", Monday28September), head.GetAttribute("aria-label"));
            Assert.Contains("roadmap-timeline__quarter--weekend", head.ClassName);
            Assert.Contains("roadmap-timeline__quarter--override", head.ClassName);
        });
        Assert.False(PaceFile.WorkingWeek.IsWorked(Monday28September));
    }

    /// <summary>Pressing it again brings the date back to its pattern, and the bar back
    /// to where it was.</summary>
    [Fact]
    public async Task Pressing_a_blocked_head_again_unblocks_it_and_the_bar_returns()
    {
        Configure("JSdotNet/Backlog");
        var plan = await ImportedAsync("plan-a");

        using var context = GatheringContext(14);
        var band = Drawn(context, PaceToday);

        DayHead(band, Monday28September).Click();
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 15), DrawnWindow(band, plan.Id)));

        DayHead(band, Monday28September).Click();
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 14), DrawnWindow(band, plan.Id)));
        band.WaitForAssertion(() =>
            Assert.DoesNotContain("roadmap-timeline__quarter--override", DayHead(band, Monday28September).ClassName));
    }

    /// <summary>A pace that keeps no working week refuses the press, and the band says
    /// why where it says its other refusals; no bar moves.</summary>
    [Fact]
    public async Task A_refused_press_is_shown_and_moves_nothing()
    {
        Configure("JSdotNet/Backlog");
        var plan = await ImportedAsync("plan-a");

        using var context = GatheringContext(14);
        context.Services.AddSingleton(TasksTestHost.UntouchedPace());
        var band = Drawn(context, PaceToday);
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 14), DrawnWindow(band, plan.Id)));

        DayHead(band, Monday28September).Click();

        band.WaitForAssertion(() => Assert.Equal(
            "This device keeps no working week to block or unblock a day in.",
            band.Find("[data-testid='roadmap-band-error']").TextContent.Trim()));
        Assert.Equal((plan.Start, 14), DrawnWindow(band, plan.Id));
        Assert.Equal(Named("Block", Monday28September), DayHead(band, Monday28September).GetAttribute("aria-label"));
    }

    /// <summary>
    /// ADR 0019 Verification 18, through the band: once the plan is drawn, the band asks
    /// for the hours agents were active from the first date the axis rules in weeks to
    /// today, and a past worked day reads them over its planned hours.
    /// </summary>
    [Fact]
    public async Task A_past_day_head_reads_the_actual_hours_over_the_planned()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a");
        ActualHours.Answer = new Dictionary<DateOnly, TimeSpan> { [Thursday24September] = TimeSpan.FromHours(6.2) };

        using var context = GatheringContext(14);
        var band = Drawn(context, PaceToday);

        band.WaitForAssertion(() =>
            Assert.EndsWith(" · 6.2 / 8.5h", DayHead(band, Thursday24September).GetAttribute("title"), StringComparison.Ordinal));

        Assert.Equal((RoadmapWindow.GraduatedWeeksFrom(PaceToday, DayOfWeek.Monday), PaceToday), ActualHours.LastRange);

        // Today has begun too, and nobody worked it yet; tomorrow has not.
        Assert.EndsWith(" · 0.0 / 8.5h", DayHead(band, PaceToday).GetAttribute("title"), StringComparison.Ordinal);
        Assert.EndsWith(" · 8.5h", DayHead(band, Monday28September).GetAttribute("title"), StringComparison.Ordinal);
        Assert.DoesNotContain("/", DayHead(band, Monday28September).GetAttribute("title"), StringComparison.Ordinal);
    }

    /// <summary>The actual hours are read once the plan is drawn and again when the pace
    /// changes — not on every render.</summary>
    [Fact]
    public async Task The_actual_hours_are_read_once_and_again_on_a_pace_change()
    {
        Configure("JSdotNet/Backlog");
        var plan = await ImportedAsync("plan-a");
        ActualHours.Answer = new Dictionary<DateOnly, TimeSpan>();

        using var context = GatheringContext(14);
        var band = Drawn(context, PaceToday);
        band.WaitForAssertion(() => Assert.Equal(1, ActualHours.Reads));

        // A render that changes nothing the read depends on asks nothing more.
        band.Render();
        Assert.Equal(1, ActualHours.Reads);

        DayHead(band, Monday28September).Click();
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 15), DrawnWindow(band, plan.Id)));
        band.WaitForAssertion(() => Assert.Equal(2, ActualHours.Reads));
    }

    /// <summary>
    /// Requirement "Heads fall back to planned hours", scenario "Session activity is
    /// unreadable": a read that fails leaves every head on its planned hours, and the
    /// roadmap still opens and still toggles.
    /// </summary>
    [Fact]
    public async Task A_failed_read_of_the_actual_hours_falls_back_to_the_planned_hours()
    {
        Configure("JSdotNet/Backlog");
        var plan = await ImportedAsync("plan-a");
        ActualHours.Fails = true;

        using var context = GatheringContext(14);
        var band = Drawn(context, PaceToday);

        band.WaitForAssertion(() => Assert.True(ActualHours.Reads >= 1));
        band.WaitForAssertion(() =>
            Assert.EndsWith(" · 8.5h", DayHead(band, Thursday24September).GetAttribute("title"), StringComparison.Ordinal));
        Assert.DoesNotContain("/", DayHead(band, PaceToday).GetAttribute("title"), StringComparison.Ordinal);
        Assert.Empty(band.FindAll("[data-testid='roadmap-band-error']"));

        DayHead(band, Monday28September).Click();
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 15), DrawnWindow(band, plan.Id)));
    }

    /// <summary>A host that cannot state the actual hours at all — no Sessions activity
    /// composed — answers null, and the heads read their planned hours alone.</summary>
    [Fact]
    public async Task No_actual_hours_to_state_leaves_the_planned_hours()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a");

        using var context = GatheringContext(14);
        var band = Drawn(context, PaceToday);

        band.WaitForAssertion(() => Assert.Equal(1, ActualHours.Reads));
        Assert.EndsWith(" · 8.5h", DayHead(band, Thursday24September).GetAttribute("title"), StringComparison.Ordinal);
    }

    private static IRenderedComponent<RoadmapBand> Drawn(BunitContext context, DateOnly today)
    {
        var band = context.Render<RoadmapBand>(parameters => parameters.Add(view => view.Today, today));
        band.WaitForElement("[data-testid='roadmap-timeline']");
        return band;
    }

    /// <summary>A day head's accessible name in the culture the test runs under:
    /// "Block Mon 28 Sep" in en-US, "Block Mon 28 Sept" in en-GB.</summary>
    private static string Named(string verb, DateOnly date) =>
        $"{verb} {date.ToString("ddd d MMM", System.Globalization.CultureInfo.CurrentCulture)}";

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

    /// <summary>A band whose every item gathers <paramref name="totalEffort"/> points.</summary>
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
