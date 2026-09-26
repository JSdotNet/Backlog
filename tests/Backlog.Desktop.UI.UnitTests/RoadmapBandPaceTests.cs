using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.UI;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The reader's paces, each under its band's name in the chart's sidebar: the one
/// they type, those measured from what they finished over the last two, four and
/// eight weeks — each shown with its figure, and only when it measured something —
/// and which of them places an imported plan
/// (ADR 0013, ruling 4, as amended on 2026-09-26) — and a change to one redrawing
/// every bar still sized by its effort, each at its own pace (ruling 5 as amended).
/// <para>
/// A repository's band carries that repository's pace. The unfiled band carries the
/// default pace, the one its plans are placed at and every repository without a pace
/// of its own reads. The heading carries none.
/// </para>
/// </summary>
public sealed class RoadmapBandPaceTests : RoadmapBandHarness
{
    private const string Default = "default";

    // --- Where the paces are ------------------------------------------------------

    [Fact]
    public async Task There_is_no_pace_in_the_heading_only_in_the_bands()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a", "backlog");
        await UnfiledAsync("plan-b");

        using var context = Context();
        var band = Banded(context);

        var paces = band.FindAll(".roadmap-pace");
        Assert.Equal(2, paces.Count);
        Assert.All(paces, pace => Assert.NotNull(pace.Closest(".roadmap-timeline__group-content")));
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace']"));
        Assert.Empty(band.Find(".roadmap-timeline__heading").QuerySelectorAll(".roadmap-pace"));
    }

    [Fact]
    public void An_empty_plan_shows_no_pace_at_all()
    {
        using var context = Context();
        var band = context.Render<RoadmapBand>();
        band.WaitForElement("[data-testid='roadmap-band-empty-state']");

        Assert.Empty(band.FindAll(".roadmap-pace"));
    }

    [Fact]
    public async Task Each_repository_band_and_the_unfiled_band_carry_a_pace_and_the_dates_band_none()
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Site");
        await ImportedAsync("plan-a", "backlog");
        await ImportedAsync("plan-b", "site");
        await UnfiledAsync("plan-c");
        _ = await Planning.AddMilestoneAsync("1.0", PaceToday.AddDays(30), cancellationToken: TestContext.Current.CancellationToken);

        using var context = Context();
        var band = Banded(context);

        Assert.Equal("Points a week for backlog", Manual(band, "backlog").GetAttribute("aria-label"));
        Assert.Equal("Points a week for site", Manual(band, "site").GetAttribute("aria-label"));
        Assert.Contains("Default points a week", Manual(band, Default).GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.NotNull(band.Find("[data-testid='roadmap-timeline-group-unfiled-content'] [data-testid='roadmap-pace-default']"));
        Assert.Empty(band.FindAll("[data-testid='roadmap-timeline-group-milestones-content']"));
        Assert.Equal("pt/wk", band.Find("[data-testid='roadmap-pace-backlog'] .roadmap-pace__unit").TextContent);
    }

    [Fact]
    public async Task A_band_is_as_tall_as_its_pace_needs_before_its_first_named_lane()
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Site");
        Finished.Add(new CompletedEffortDto(PaceToday, 14) { RepositoryAliases = ["site"] });
        await ImportedAsync("plan-a", "backlog");
        await ImportedAsync("plan-b", "site");

        using var context = Context();
        var band = Banded(context);

        // backlog measured nothing, so its pace is the field alone; site offers choices.
        Assert.Equal(
            RoadmapPlanView.PaceRowsFieldOnly,
            band.FindAll("[data-testid='roadmap-timeline-group-backlog'] .roadmap-timeline__row-name").Count);
        Assert.Equal(
            RoadmapPlanView.PaceRowsWithChoices,
            band.FindAll("[data-testid='roadmap-timeline-group-site'] .roadmap-timeline__row-name").Count);
        Assert.Equal(2, RoadmapPlanView.PaceRowsFieldOnly);
        Assert.Equal(3, RoadmapPlanView.PaceRowsWithChoices);
    }

    // --- What each pace shows ----------------------------------------------------

    [Fact]
    public async Task A_pace_that_measured_nothing_is_the_field_alone()
    {
        await UnfiledAsync("plan-a");

        using var context = Context();
        var band = Banded(context);

        Assert.Equal("7", Manual(band, Default).GetAttribute("value"));
        Assert.Equal("number", Manual(band, Default).GetAttribute("type"));

        // No choices, not even Mine — a choice between one thing is no choice — and
        // no line saying so: the missing buttons say it.
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace-options-default']"));
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace-default'] button"));
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace-default'] .roadmap-pace__note"));
    }

    [Fact]
    public async Task Every_measured_figure_is_shown_on_its_button()
    {
        Configure("JSdotNet/Backlog");
        Finished.Add(new CompletedEffortDto(PaceToday, 14) { RepositoryAliases = ["backlog"] });
        await ImportedAsync("plan-a", "backlog");

        using var context = Context();
        var band = Banded(context);

        band.WaitForAssertion(() =>
        {
            Assert.Equal("7", Measured(band, "two-weeks", "backlog"));
            Assert.Equal("3.5", Measured(band, "four-weeks", "backlog"));
            Assert.Equal("1.75", Measured(band, "eight-weeks", "backlog"));
        });

        // Drawn, not only read out: the figure is not hidden, and it is inside the
        // button beside the stretch's name.
        var value = Option(band, "two-weeks", "backlog").QuerySelector(".roadmap-pace__option-value")!;
        Assert.DoesNotContain("sr-only", value.ClassName!, StringComparison.Ordinal);
        Assert.Equal("2 wk", Option(band, "two-weeks", "backlog").QuerySelector(".roadmap-pace__option-name")!.TextContent);
        Assert.Equal("true", Option(band, "manual-option", "backlog").GetAttribute("aria-pressed"));
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace-backlog'] button[disabled]"));
    }

    [Fact]
    public async Task A_stretch_that_measured_nothing_is_not_offered()
    {
        Configure("JSdotNet/Backlog");
        // Twenty days ago: outside the last two weeks, inside the last four and eight.
        Finished.Add(new CompletedEffortDto(PaceToday.AddDays(-20), 28) { RepositoryAliases = ["backlog"] });
        await ImportedAsync("plan-a", "backlog");

        using var context = Context();
        var band = Banded(context);

        band.WaitForAssertion(() =>
        {
            Assert.Empty(band.FindAll("[data-testid='roadmap-pace-two-weeks-backlog']"));
            Assert.Equal("7", Measured(band, "four-weeks", "backlog"));
            Assert.Equal("3.5", Measured(band, "eight-weeks", "backlog"));
            Assert.NotNull(Option(band, "manual-option", "backlog"));
        });
    }

    [Fact]
    public async Task The_default_pace_counts_all_finished_work_and_a_repository_only_its_own()
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Site");
        Finished.Add(new CompletedEffortDto(PaceToday, 14) { RepositoryAliases = ["backlog"] });
        Finished.Add(new CompletedEffortDto(PaceToday, 28));
        _ = PaceFile.Set(12m, "site");
        await ImportedAsync("plan-a", "backlog");
        await ImportedAsync("plan-b", "site");
        await UnfiledAsync("plan-c");

        using var context = Context();
        var band = Banded(context);

        band.WaitForAssertion(() =>
        {
            Assert.Equal("21", Measured(band, "two-weeks", Default)); // 42 over 2 weeks
            Assert.Equal("7", Measured(band, "two-weeks", "backlog"));
            Assert.Empty(band.FindAll("[data-testid='roadmap-pace-options-site']"));

            // Site has a pace of its own; backlog still reads the default one.
            Assert.Equal("12", Manual(band, "site").GetAttribute("value"));
            Assert.Equal("7", Manual(band, "backlog").GetAttribute("value"));
        });
    }

    [Fact]
    public async Task Finished_work_updates_the_measured_paces()
    {
        await UnfiledAsync("plan-a");

        using var context = Context();
        var band = Banded(context);
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace-options-default']"));

        Finished.Add(new CompletedEffortDto(PaceToday, 28));
        WorkChanges.Raise();

        band.WaitForAssertion(() => Assert.Equal("14", Measured(band, "two-weeks", Default)));
    }

    [Fact]
    public async Task A_pace_changed_elsewhere_is_shown_without_a_reload()
    {
        await UnfiledAsync("plan-a");

        using var context = Context();
        var band = Banded(context);

        _ = PaceFile.Set(3m);

        band.WaitForAssertion(() => Assert.Equal("3", Manual(band, Default).GetAttribute("value")));
    }

    [Fact]
    public async Task A_chosen_pace_that_measured_nothing_says_the_typed_one_is_used()
    {
        Configure("JSdotNet/Backlog");
        Finished.Add(new CompletedEffortDto(PaceToday, 14)); // unfiled: the default pace's alone
        _ = PaceFile.Choose(PaceSource.LastTwoWeeks);
        await ImportedAsync("plan-a", "backlog");
        await UnfiledAsync("plan-b");

        using var context = Context();
        var band = Banded(context);

        band.WaitForAssertion(() =>
        {
            var note = band.Find("[data-testid='roadmap-pace-fell-back-backlog']");
            Assert.Equal("status", note.GetAttribute("role"));
            Assert.Contains("Empty stretch: using Mine", note.TextContent, StringComparison.Ordinal);
            Assert.Contains("Nothing estimated was finished in backlog", note.GetAttribute("title"), StringComparison.Ordinal);
        });

        // The default pace measured something, so its band says nothing.
        Assert.Empty(band.FindAll("[data-testid^='roadmap-pace-fell-back-default']"));

        // backlog's chosen stretch has no button any more, so the line is what says it.
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace-options-backlog']"));
    }

    // --- Setting a pace ----------------------------------------------------------

    [Fact]
    public async Task A_default_pace_typed_in_the_unfiled_band_is_stored_as_the_default()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a", "backlog");
        await UnfiledAsync("plan-b");

        using var context = Context();
        var band = Banded(context);

        Manual(band, Default).Change("2.5");

        Assert.Equal(2.5m, PaceFile.StoryPointsPerWeek);
        Assert.False(PaceFile.KeepsOwnPace("backlog"));

        // backlog has no pace of its own, so it shows the new default.
        band.WaitForAssertion(() =>
        {
            Assert.Equal("2.5", Manual(band, Default).GetAttribute("value"));
            Assert.Equal("2.5", Manual(band, "backlog").GetAttribute("value"));
        });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("quickly")]
    public async Task A_pace_the_roadmap_could_not_divide_by_says_so_and_changes_nothing(string refused)
    {
        _ = PaceFile.Set(4m);
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a", "backlog");

        using var context = Context();
        var band = Banded(context);

        Manual(band, "backlog").Change(refused);

        Assert.False(PaceFile.KeepsOwnPace("backlog"));
        Assert.Equal(4m, PaceFile.StoryPointsPerWeek);
        band.WaitForAssertion(() =>
        {
            var note = band.Find("[data-testid='roadmap-pace-refused-backlog']");
            Assert.Equal("status", note.GetAttribute("role"));
            Assert.False(string.IsNullOrWhiteSpace(note.GetAttribute("title")));
        });
    }

    [Fact]
    public async Task Choosing_a_pace_in_a_band_is_kept_for_that_repository_and_shown_pressed_there()
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Site");
        Finished.Add(new CompletedEffortDto(PaceToday, 14) { RepositoryAliases = ["site"] });
        await ImportedAsync("plan-a", "backlog");
        await ImportedAsync("plan-b", "site");

        using var context = Context();
        var band = Banded(context);

        Option(band, "two-weeks", "site").Click();

        Assert.Equal(PaceSource.LastTwoWeeks, PaceFile.SourceFor("site"));
        Assert.Equal(PaceSource.Manual, PaceFile.Source);
        band.WaitForAssertion(() =>
        {
            Assert.Equal("true", Option(band, "two-weeks", "site").GetAttribute("aria-pressed"));
            Assert.Equal("false", Option(band, "manual-option", "site").GetAttribute("aria-pressed"));

            // backlog measured nothing, so it offers no choice to press.
            Assert.Empty(band.FindAll("[data-testid='roadmap-pace-options-backlog']"));
        });
    }

    // --- A pace change redraws the bars sized by effort -------------------------

    [Fact]
    public async Task Typing_a_new_default_pace_relengthens_an_unfiled_plan()
    {
        var plan = await UnfiledAsync("plan-a");
        Assert.Equal(5, Days(plan)); // nothing gathered at import: the default span

        using var context = GatheringContext(14);
        var band = Banded(context);

        Manual(band, Default).Change("14");

        // 14 points at 14 a week is a week.
        await WaitForDaysAsync("plan-a", 7);
        Assert.Equal(plan.Start, (await StoredAsync("plan-a")).Start);
    }

    [Fact]
    public async Task Choosing_a_measured_default_pace_relengthens_an_unfiled_plan()
    {
        Finished.Add(new CompletedEffortDto(PaceToday, 56)); // 28 a week over two weeks
        await UnfiledAsync("plan-a");

        using var context = GatheringContext(14);
        var band = Banded(context);

        Option(band, "two-weeks", Default).Click();

        // 14 points at 28 a week is three and a half days, rounded up.
        await WaitForDaysAsync("plan-a", 4);
    }

    [Fact]
    public async Task Typing_a_pace_in_a_band_relengthens_each_plan_at_its_own_pace()
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Site");
        await ImportedAsync("plan-a", "backlog");
        await ImportedAsync("plan-b", "site");

        using var context = GatheringContext(14);
        var band = Banded(context);

        Manual(band, "site").Change("14");

        // Site's 14 points at its new 14 a week is a week; backlog's still read the
        // default 7 a week, which makes two.
        await WaitForDaysAsync("plan-b", 7);
        await WaitForDaysAsync("plan-a", 14);
        Assert.Equal(14m, PaceFile.StoryPointsPerWeekFor("site"));
        Assert.Equal(7m, PaceFile.StoryPointsPerWeek);
        Assert.False(PaceFile.KeepsOwnPace("backlog"));
    }

    [Fact]
    public async Task A_hand_moved_plan_keeps_its_window_when_the_pace_changes()
    {
        var plan = await UnfiledAsync("plan-a");
        var moved = await Planning.RescheduleItemAsync(
            plan.Id, plan.Start, plan.End.AddDays(2), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(moved.IsSuccess);

        using var context = GatheringContext(14);
        var band = Banded(context);

        Manual(band, Default).Change("14");

        band.WaitForAssertion(() => Assert.Equal(14m, PaceFile.StoryPointsPerWeek));
        Assert.Equal(7, Days(await StoredAsync("plan-a")));
    }

    [Fact]
    public async Task A_typed_pace_while_a_measured_one_is_in_use_moves_no_plan()
    {
        Finished.Add(new CompletedEffortDto(PaceToday, 14));
        _ = PaceFile.Choose(PaceSource.LastTwoWeeks);
        await UnfiledAsync("plan-a");

        using var context = GatheringContext(14);
        var band = Banded(context);

        Manual(band, Default).Change("100");

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

    private async Task<RoadmapItemDto> ImportedAsync(string tag, string repository)
    {
        var imported = await Planning.ImportPlanItemsAsync(
            [new PlanImportEntryDto("Plan", tag, RepositoryAliases: [repository])],
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(imported.IsSuccess);
        return await StoredAsync(tag);
    }

    private async Task<RoadmapItemDto> UnfiledAsync(string tag)
    {
        var imported = await Planning.ImportPlanItemsAsync(
            [new PlanImportEntryDto("Plan", tag, RepositoryAliases: [])],
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

    /// <summary>A band drawn with its chart, and with the paces in its sidebar read.</summary>
    private static IRenderedComponent<RoadmapBand> Banded(BunitContext context)
    {
        var band = context.Render<RoadmapBand>();
        band.WaitForElement("[data-testid='roadmap-timeline']");
        band.WaitForElement(".roadmap-pace input");
        return band;
    }

    private static AngleSharp.Dom.IElement Manual(IRenderedComponent<RoadmapBand> band, string scope) =>
        band.Find($"[data-testid='roadmap-pace-manual-{scope}'] input");

    private static AngleSharp.Dom.IElement Option(IRenderedComponent<RoadmapBand> band, string key, string scope) =>
        band.Find($"[data-testid='roadmap-pace-{key}-{scope}']");

    private static string Measured(IRenderedComponent<RoadmapBand> band, string key, string scope) =>
        Option(band, key, scope).QuerySelector(".roadmap-pace__option-value")!.TextContent.Trim();
}
