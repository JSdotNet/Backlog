using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.UI;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The reader's pace in the roadmap's heading: the one they type, three measured
/// from what they finished over the last two, four and eight weeks, and which of
/// them places an imported plan (ADR 0013, ruling 4) — and a change to it redrawing
/// every bar still sized by its effort (ruling 5 as amended).
/// </summary>
public sealed class RoadmapBandPaceTests : RoadmapBandHarness
{
    [Fact]
    public void The_pace_opens_at_seven_points_a_week_typed_with_nothing_measured()
    {
        using var context = Context();
        var band = Paced(context);

        Assert.Equal("Points a week", band.Find(".roadmap-pace__label").TextContent);
        Assert.Equal("7", Manual(band).GetAttribute("value"));
        Assert.Equal("number", Manual(band).GetAttribute("type"));
        Assert.Equal("true", Option(band, "manual-option").GetAttribute("aria-pressed"));

        foreach (var stretch in new[] { "two-weeks", "four-weeks", "eight-weeks" })
        {
            var option = Option(band, stretch);
            Assert.True(option.HasAttribute("disabled"));
            Assert.Equal("—", Measured(band, stretch));
        }
    }

    [Fact]
    public void Each_stretch_shows_what_was_finished_in_it_per_week()
    {
        Finished.Add(new CompletedEffortDto(PaceToday, 14));

        using var context = Context();
        var band = Paced(context);

        Assert.Equal("7", Measured(band, "two-weeks"));
        Assert.Equal("3.5", Measured(band, "four-weeks"));
        Assert.Equal("1.75", Measured(band, "eight-weeks"));
        Assert.False(Option(band, "two-weeks").HasAttribute("disabled"));
    }

    [Fact]
    public void A_typed_pace_is_stored_straight_away()
    {
        using var context = Context();
        var band = Paced(context);

        Manual(band).Change("2.5");

        Assert.Equal(2.5m, PaceFile.StoryPointsPerWeek);
        band.WaitForAssertion(() => Assert.Equal("2.5", Manual(band).GetAttribute("value")));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("quickly")]
    public void A_pace_the_roadmap_could_not_divide_by_says_so_and_changes_nothing(string refused)
    {
        _ = PaceFile.Set(4m);

        using var context = Context();
        var band = Paced(context);

        Manual(band).Change(refused);

        Assert.Equal(4m, PaceFile.StoryPointsPerWeek);
        band.WaitForAssertion(() => Assert.Contains(
            "field__error",
            band.Find("[data-testid='roadmap-pace']").InnerHtml,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Choosing_a_measured_pace_is_kept_and_shown_pressed()
    {
        Finished.Add(new CompletedEffortDto(PaceToday, 14));

        using var context = Context();
        var band = Paced(context);

        Option(band, "four-weeks").Click();

        Assert.Equal(PaceSource.LastFourWeeks, PaceFile.Source);
        band.WaitForAssertion(() =>
        {
            Assert.Equal("true", Option(band, "four-weeks").GetAttribute("aria-pressed"));
            Assert.Equal("false", Option(band, "manual-option").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void A_chosen_pace_that_measured_nothing_says_the_typed_one_is_used()
    {
        _ = PaceFile.Choose(PaceSource.LastTwoWeeks);

        using var context = Context();
        var band = Paced(context);

        Assert.Contains(
            "your own pace is used",
            band.Find("[data-testid='roadmap-pace-fell-back']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_pace_changed_elsewhere_is_shown_without_a_reload()
    {
        using var context = Context();
        var band = Paced(context);

        _ = PaceFile.Set(3m);

        band.WaitForAssertion(() => Assert.Equal("3", Manual(band).GetAttribute("value")));
    }

    [Fact]
    public void Finished_work_updates_the_measured_paces()
    {
        using var context = Context();
        var band = Paced(context);

        Finished.Add(new CompletedEffortDto(PaceToday, 28));
        WorkChanges.Raise();

        band.WaitForAssertion(() =>
            Assert.Equal("14", Measured(band, "two-weeks")));
    }

    // --- A pace change redraws the bars sized by effort -------------------------

    [Fact]
    public async Task Typing_a_new_pace_relengthens_an_effort_placed_plan()
    {
        var plan = await ImportedAsync("plan-a");
        Assert.Equal(5, Days(plan)); // nothing gathered at import: the default span

        using var context = GatheringContext(14);
        var band = Paced(context);

        Manual(band).Change("14");

        // 14 points at 14 a week is a week.
        await WaitForDaysAsync("plan-a", 7);
        Assert.Equal(plan.Start, (await StoredAsync("plan-a")).Start);
    }

    [Fact]
    public async Task Choosing_a_measured_pace_relengthens_an_effort_placed_plan()
    {
        Finished.Add(new CompletedEffortDto(PaceToday, 56)); // 28 a week over two weeks
        await ImportedAsync("plan-a");

        using var context = GatheringContext(14);
        var band = Paced(context);

        Option(band, "two-weeks").Click();

        // 14 points at 28 a week is three and a half days, rounded up.
        await WaitForDaysAsync("plan-a", 4);
    }

    [Fact]
    public async Task A_hand_moved_plan_keeps_its_window_when_the_pace_changes()
    {
        var plan = await ImportedAsync("plan-a");
        var moved = await Planning.RescheduleItemAsync(
            plan.Id, plan.Start, plan.End.AddDays(2), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(moved.IsSuccess);

        using var context = GatheringContext(14);
        var band = Paced(context);

        Manual(band).Change("14");

        band.WaitForAssertion(() => Assert.Equal(14m, PaceFile.StoryPointsPerWeek));
        Assert.Equal(7, Days(await StoredAsync("plan-a")));
    }

    [Fact]
    public async Task A_typed_pace_while_a_measured_one_is_in_use_moves_no_plan()
    {
        Finished.Add(new CompletedEffortDto(PaceToday, 14));
        _ = PaceFile.Choose(PaceSource.LastTwoWeeks);
        await ImportedAsync("plan-a");

        using var context = GatheringContext(14);
        var band = Paced(context);

        Manual(band).Change("100");

        band.WaitForAssertion(() => Assert.Equal(100m, PaceFile.StoryPointsPerWeek));
        Assert.Equal(5, Days(await StoredAsync("plan-a")));
    }

    /// <summary>A band whose every item gathers <paramref name="totalEffort"/> points,
    /// so a test can say what the tasks add up to without writing a backlog.</summary>
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
        return await StoredAsync(tag);
    }

    private async Task<RoadmapItemDto> StoredAsync(string tag) =>
        Assert.Single(
            (await Planning.GetPlanAsync(TestContext.Current.CancellationToken)).Items,
            item => item.Tag == tag);

    /// <summary>The store is written after the band gathers and re-lengthens, which
    /// lands a few renders after the change; polled rather than read once.</summary>
    private async Task WaitForDaysAsync(string tag, int days)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (Days(await StoredAsync(tag)) == days) return;
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Equal(days, Days(await StoredAsync(tag)));
    }

    private static int Days(RoadmapItemDto item) => item.End.DayNumber - item.Start.DayNumber + 1;

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

    private static IRenderedComponent<RoadmapBand> Paced(BunitContext context)
    {
        var band = context.Render<RoadmapBand>();
        band.WaitForElement("[data-testid='roadmap-pace-manual'] input");
        return band;
    }

    private static AngleSharp.Dom.IElement Manual(IRenderedComponent<RoadmapBand> band) =>
        band.Find("[data-testid='roadmap-pace-manual'] input");

    private static AngleSharp.Dom.IElement Option(IRenderedComponent<RoadmapBand> band, string key) =>
        band.Find($"[data-testid='roadmap-pace-{key}']");

    private static string Measured(IRenderedComponent<RoadmapBand> band, string key) =>
        band.Find($"[data-testid='roadmap-pace-{key}'] .roadmap-pace__option-value").TextContent.Trim();
}
