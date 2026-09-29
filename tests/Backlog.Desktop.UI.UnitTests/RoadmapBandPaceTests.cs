using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Sqlite.Roadmap;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Roadmap;

using Bunit;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The reader's paces. Their own — "Mine" — typed once in the roadmap's heading, the
/// fallback every band shares; and per band, under its name in the chart's sidebar,
/// those measured from what was finished over the last two, four and eight weeks —
/// each shown with its figure, only when it measured something, and used whenever
/// one did (ADR 0013, ruling 4, as amended) — and a change to one redrawing every
/// bar still sized by its effort, each at its own pace (ruling 5 as amended).
/// <para>
/// A repository's band carries that repository's measured paces. The unfiled band
/// carries the default ones, measured over all finished work.
/// </para>
/// </summary>
public sealed class RoadmapBandPaceTests : RoadmapBandHarness
{
    private const string Default = "default";

    // --- Where the paces are ------------------------------------------------------

    [Fact]
    public async Task Mine_is_typed_once_in_the_heading_and_each_band_carries_its_measured_pace()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a", "backlog");
        await UnfiledAsync("plan-b");

        using var context = Context();
        var band = Banded(context);

        var heading = band.Find(".roadmap-timeline__heading");
        Assert.Single(heading.QuerySelectorAll("input"));
        Assert.Equal("Mine", heading.QuerySelector(".roadmap-pace__label")!.TextContent);
        Assert.Equal("pt/wk", heading.QuerySelector(".roadmap-pace__unit")!.TextContent);

        var paces = band.FindAll(".roadmap-timeline__group-content .roadmap-pace");
        Assert.Equal(2, paces.Count);
        Assert.All(paces, pace => Assert.Empty(pace.QuerySelectorAll("input")));
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

        Assert.NotNull(band.Find("[data-testid='roadmap-timeline-group-backlog-content'] [data-testid='roadmap-pace-backlog']"));
        Assert.NotNull(band.Find("[data-testid='roadmap-timeline-group-site-content'] [data-testid='roadmap-pace-site']"));
        Assert.NotNull(band.Find("[data-testid='roadmap-timeline-group-unfiled-content'] [data-testid='roadmap-pace-default']"));
        Assert.Empty(band.FindAll("[data-testid='roadmap-timeline-group-milestones-content']"));
    }

    [Fact]
    public async Task A_band_keeps_the_same_rows_for_its_pace_whether_it_measured_anything_or_not()
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Site");
        Finished.Add(new CompletedEffortDto(PaceToday, 14) { RepositoryAliases = ["site"] });
        await ImportedAsync("plan-a", "backlog");
        await ImportedAsync("plan-b", "site");

        using var context = Context();
        var band = Banded(context);

        // backlog measured nothing, so its pace is a line saying Mine is used; site offers choices.
        Assert.Equal(
            RoadmapPlanView.PaceRows,
            band.FindAll("[data-testid='roadmap-timeline-group-backlog'] .roadmap-timeline__row-name").Count);
        Assert.Equal(
            RoadmapPlanView.PaceRows,
            band.FindAll("[data-testid='roadmap-timeline-group-site'] .roadmap-timeline__row-name").Count);
        Assert.Equal(2, RoadmapPlanView.PaceRows);
    }

    // --- What each pace shows ----------------------------------------------------

    [Fact]
    public async Task A_band_that_measured_nothing_says_Mine_is_used_with_its_figure()
    {
        await UnfiledAsync("plan-a");

        using var context = Context();
        var band = Banded(context);

        Assert.Equal("7", Manual(band).GetAttribute("value"));
        Assert.Equal("number", Manual(band).GetAttribute("type"));

        // No choices — there is nothing measured to choose between.
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace-options-default']"));
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace-default'] button"));

        var note = band.Find("[data-testid='roadmap-pace-fell-back-default']");
        Assert.Equal("status", note.GetAttribute("role"));
        Assert.Contains("Using Mine: 7 pt/wk", note.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_measured_figure_is_shown_on_its_button_and_two_weeks_is_used_until_chosen()
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
        Assert.Equal("true", Option(band, "two-weeks", "backlog").GetAttribute("aria-pressed"));
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace-manual-option-backlog']"));
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace-fell-back-backlog']"));
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace-backlog'] button[disabled]"));
    }

    [Fact]
    public async Task A_stretch_that_measured_nothing_is_not_offered_and_the_next_one_is_used()
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
            Assert.Equal("true", Option(band, "four-weeks", "backlog").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public async Task The_default_pace_counts_all_finished_work_and_a_repository_only_its_own()
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Site");
        Finished.Add(new CompletedEffortDto(PaceToday, 14) { RepositoryAliases = ["backlog"] });
        Finished.Add(new CompletedEffortDto(PaceToday, 28));
        await ImportedAsync("plan-a", "backlog");
        await ImportedAsync("plan-b", "site");
        await UnfiledAsync("plan-c");

        using var context = Context();
        var band = Banded(context);

        band.WaitForAssertion(() =>
        {
            Assert.Equal("21", Measured(band, "two-weeks", Default)); // 42 over 2 weeks
            Assert.Equal("7", Measured(band, "two-weeks", "backlog"));

            // Site finished nothing of its own, so it places by Mine.
            Assert.Empty(band.FindAll("[data-testid='roadmap-pace-options-site']"));
            Assert.NotNull(band.Find("[data-testid='roadmap-pace-fell-back-site']"));
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

        band.WaitForAssertion(() =>
        {
            Assert.Equal("3", Manual(band).GetAttribute("value"));
            Assert.Contains(
                "Using Mine: 3 pt/wk",
                band.Find("[data-testid='roadmap-pace-fell-back-default']").TextContent,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_band_whose_every_stretch_measured_nothing_says_Mine_is_used_there()
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
            Assert.Contains("Using Mine: 7 pt/wk", note.TextContent, StringComparison.Ordinal);
            Assert.Contains("Nothing estimated was finished in backlog", note.GetAttribute("title"), StringComparison.Ordinal);
        });

        // The default pace measured something, so its band says nothing.
        Assert.Empty(band.FindAll("[data-testid^='roadmap-pace-fell-back-default']"));
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace-options-backlog']"));
    }

    // --- Setting a pace ----------------------------------------------------------

    [Fact]
    public async Task Mine_typed_in_the_heading_is_stored_once_for_every_band()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a", "backlog");
        await UnfiledAsync("plan-b");

        using var context = Context();
        var band = Banded(context);

        Manual(band).Change("2.5");

        Assert.Equal(2.5m, PaceFile.StoryPointsPerWeek);
        Assert.False(PaceFile.KeepsOwnPace("backlog"));

        band.WaitForAssertion(() =>
        {
            Assert.Equal("2.5", Manual(band).GetAttribute("value"));
            Assert.Contains(
                "Using Mine: 2.5 pt/wk",
                band.Find("[data-testid='roadmap-pace-fell-back-backlog']").TextContent,
                StringComparison.Ordinal);
            Assert.Contains(
                "Using Mine: 2.5 pt/wk",
                band.Find("[data-testid='roadmap-pace-fell-back-default']").TextContent,
                StringComparison.Ordinal);
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

        Manual(band).Change(refused);

        Assert.Equal(4m, PaceFile.StoryPointsPerWeek);
        band.WaitForAssertion(() =>
        {
            var note = band.Find("[data-testid='roadmap-pace-refused-mine']");
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

        Option(band, "eight-weeks", "site").Click();

        Assert.Equal(PaceSource.LastEightWeeks, PaceFile.SourceFor("site"));
        Assert.Equal(PaceSource.Manual, PaceFile.Source);
        band.WaitForAssertion(() =>
        {
            Assert.Equal("true", Option(band, "eight-weeks", "site").GetAttribute("aria-pressed"));
            Assert.Equal("false", Option(band, "two-weeks", "site").GetAttribute("aria-pressed"));

            // backlog measured nothing, so it offers no choice to press.
            Assert.Empty(band.FindAll("[data-testid='roadmap-pace-options-backlog']"));
        });
    }

    // --- A pace change redraws the bars sized by effort -------------------------

    [Fact]
    public async Task Typing_Mine_redraws_an_unfiled_plan_that_measured_nothing()
    {
        var plan = await UnfiledAsync("plan-a");
        Assert.Equal(5, Days(plan)); // nothing gathered at import: the default span

        using var context = GatheringContext(14);
        var band = Banded(context);

        // 14 points at Mine's 7 a week is two weeks.
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 14), DrawnWindow(band, plan.Id)));

        Manual(band).Change("14");

        // 14 points at 14 a week is a week, from the same start.
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 7), DrawnWindow(band, plan.Id)));
    }

    [Fact]
    public async Task Choosing_a_measured_default_pace_redraws_an_unfiled_plan()
    {
        Finished.Add(new CompletedEffortDto(PaceToday, 56)); // 28 a week over two weeks, 7 over eight
        var plan = await UnfiledAsync("plan-a");

        using var context = GatheringContext(14);
        var band = Banded(context);

        // Two weeks is in use until another is chosen: 14 points at 28 a week.
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 4), DrawnWindow(band, plan.Id)));

        Option(band, "eight-weeks", Default).Click();

        // 14 points at 7 a week is two weeks.
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 14), DrawnWindow(band, plan.Id)));
    }

    [Fact]
    public async Task Typing_Mine_redraws_each_plan_at_its_own_pace()
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Site");
        Finished.Add(new CompletedEffortDto(PaceToday, 28) { RepositoryAliases = ["backlog"] }); // 14 a week
        var backlog = await ImportedAsync("plan-a", "backlog");
        var site = await ImportedAsync("plan-b", "site");

        using var context = GatheringContext(14);
        var band = Banded(context);

        Manual(band).Change("28");

        // Site measured nothing, so its 14 points go at Mine's 28 a week: three and a
        // half days, rounded up. Backlog's go at its own measured 14 a week: a week.
        band.WaitForAssertion(() =>
        {
            Assert.Equal((site.Start, 4), DrawnWindow(band, site.Id));
            Assert.Equal((backlog.Start, 7), DrawnWindow(band, backlog.Id));
        });
        Assert.Equal(28m, PaceFile.StoryPointsPerWeek);
        Assert.False(PaceFile.KeepsOwnPace("site"));
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

        Manual(band).Change("14");

        band.WaitForAssertion(() => Assert.Equal(14m, PaceFile.StoryPointsPerWeek));
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 7), DrawnWindow(band, plan.Id)));
        Assert.Equal(7, Days(await StoredAsync("plan-a")));
    }

    [Fact]
    public async Task Typing_Mine_while_every_band_measured_something_moves_no_bar()
    {
        Finished.Add(new CompletedEffortDto(PaceToday, 14)); // 7 a week measured
        var plan = await UnfiledAsync("plan-a");

        using var context = GatheringContext(14);
        var band = Banded(context);
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 14), DrawnWindow(band, plan.Id)));

        Manual(band).Change("100");

        band.WaitForAssertion(() => Assert.Equal(100m, PaceFile.StoryPointsPerWeek));
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 14), DrawnWindow(band, plan.Id)));
    }

    /// <summary>
    /// A pace change is the pace's document and nothing else (local ADR 0018): the
    /// plan's stored document and its <c>updated_at</c> are what they were, and no plan
    /// change is announced — so a plan edit another PC made in the same interval is
    /// never overwritten by a newer plan stamp from this one.
    /// </summary>
    [Fact]
    public async Task A_pace_change_writes_nothing_to_the_plan()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a", "backlog");
        await UnfiledAsync("plan-b");
        var before = await StoredPlanRowAsync();

        using var context = GatheringContext(14);
        var band = Banded(context);

        var planChanges = 0;
        Planning.Changed += () => Interlocked.Increment(ref planChanges);

        Manual(band).Change("28");
        band.WaitForAssertion(() => Assert.Equal(28m, PaceFile.StoryPointsPerWeek));

        // The bar has been redrawn at the new pace — whatever the change set off has
        // run — before the store is read. 14 points at 28 a week: four days.
        var plan = await StoredAsync("plan-b");
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 4), DrawnWindow(band, plan.Id)));

        Assert.Equal(before, await StoredPlanRowAsync());
        Assert.Equal(0, Volatile.Read(ref planChanges));
    }

    /// <summary>
    /// A pace that arrives from another PC — written into the pace file by the sync,
    /// not typed here — redraws the bars as a typed one does.
    /// </summary>
    [Fact]
    public async Task A_pace_that_arrives_from_elsewhere_redraws_the_bars()
    {
        var plan = await UnfiledAsync("plan-a");

        using var context = GatheringContext(14);
        var band = Banded(context);
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 14), DrawnWindow(band, plan.Id)));

        _ = PaceFile.Set(14m);

        band.WaitForAssertion(() => Assert.Equal((plan.Start, 7), DrawnWindow(band, plan.Id)));
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

    /// <summary>Where the chart draws an item: the first day of its first bar and how
    /// many days its bars cover, whether it is drawn whole or in segments.</summary>
    private static (DateOnly Start, int Days) DrawnWindow(IRenderedComponent<RoadmapBand> band, Guid itemId)
    {
        var bars = band.FindComponent<RoadmapTimeline>().Instance.Bars
            .Where(bar => RoadmapPlanView.NodeIdOf(bar.Id) == itemId)
            .ToList();
        Assert.NotEmpty(bars);

        var start = bars.Min(bar => bar.Start);
        return (start, bars.Max(bar => bar.End).DayNumber - start.DayNumber + 1);
    }

    /// <summary>The plan's row as the store holds it — the document and its stamp —
    /// read beneath the module, so nothing the module caches can answer instead.</summary>
    private async Task<(string Document, string UpdatedAt)> StoredPlanRowAsync()
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = SqliteTaskRepository.DatabasePathFor(Settings.RootDirectory),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT document, updated_at FROM roadmap_plan WHERE id = $id;";
        read.Parameters.AddWithValue("$id", SqliteRoadmapPlanRepository.PlanRowId);

        await using var row = await read.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await row.ReadAsync(TestContext.Current.CancellationToken));
        return (row.GetString(0), row.GetString(1));
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

    /// <summary>A band drawn with its chart, and with the paces read.</summary>
    private static IRenderedComponent<RoadmapBand> Banded(BunitContext context)
    {
        var band = context.Render<RoadmapBand>();
        band.WaitForElement("[data-testid='roadmap-timeline']");
        band.WaitForElement(".roadmap-pace input");
        return band;
    }

    /// <summary>Mine: the one typed pace, in the heading.</summary>
    private static AngleSharp.Dom.IElement Manual(IRenderedComponent<RoadmapBand> band) =>
        band.Find("[data-testid='roadmap-pace-manual'] input");

    private static AngleSharp.Dom.IElement Option(IRenderedComponent<RoadmapBand> band, string key, string scope) =>
        band.Find($"[data-testid='roadmap-pace-{key}-{scope}']");

    private static string Measured(IRenderedComponent<RoadmapBand> band, string key, string scope) =>
        Option(band, key, scope).QuerySelector(".roadmap-pace__option-value")!.TextContent.Trim();
}
