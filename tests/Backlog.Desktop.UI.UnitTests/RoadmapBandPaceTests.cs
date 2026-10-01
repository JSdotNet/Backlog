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
/// fallback every band shares; and per band, under its name in the chart's sidebar, a
/// slider between what the lane measured over the last two weeks and what was typed,
/// with the managed effort per week over the last two, four and eight weeks in an
/// info mark, and a "Measured" reset (ADR 0013, ruling 4, as amended) — and a change
/// to one redrawing every bar still sized by its effort, each at its own pace
/// (ruling 5 as amended).
/// <para>
/// A repository's band carries that repository's paces. The unfiled band carries the
/// default ones, measured over all finished work.
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

        // A band carries a slider and nothing typed: the one field is the heading's.
        Assert.All(paces, pace =>
        {
            Assert.Empty(pace.QuerySelectorAll("input[type='number']"));
            Assert.Single(pace.QuerySelectorAll("input[type='range']"));
        });
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

        // backlog measured nothing, so its pace says Mine is used; site offers "Measured".
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
    public async Task A_band_that_measured_nothing_says_Mine_is_used_with_its_figure_and_still_has_its_slider()
    {
        await UnfiledAsync("plan-a");

        using var context = Context();
        var band = Banded(context);

        Assert.Equal("7", Manual(band).GetAttribute("value"));
        Assert.Equal("number", Manual(band).GetAttribute("type"));

        // Nothing measured to go back to, so no "Measured" - but the slider is there,
        // on a line from half the typed pace to double it, at the typed pace.
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace-measured-default']"));
        var slider = Slider(band, Default);
        Assert.Equal("3", slider.GetAttribute("min"));
        Assert.Equal("14", slider.GetAttribute("max"));
        Assert.Equal("7", slider.GetAttribute("value"));
        Assert.Equal("1", slider.GetAttribute("step"));
        Assert.Equal("7 pt/wk", ValueText(band, Default));

        var note = band.Find("[data-testid='roadmap-pace-fell-back-default']");
        Assert.Equal("status", note.GetAttribute("role"));
        Assert.Contains("Using Mine: 7 pt/wk", note.TextContent, StringComparison.Ordinal);
        Assert.Contains("nothing finished", Hint(band, Default), StringComparison.Ordinal);
        Assert.Contains("your typed pace", Hint(band, Default), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_slider_runs_from_the_lanes_two_week_pace_to_the_typed_pace_and_Measured_is_pressed()
    {
        Configure("JSdotNet/Backlog");
        Finished.Add(new CompletedEffortDto(PaceToday, 6) { RepositoryAliases = ["backlog"] }); // 3 a week over two weeks
        await ImportedAsync("plan-a", "backlog");

        using var context = Context();
        var band = Banded(context);

        band.WaitForAssertion(() =>
        {
            var slider = Slider(band, "backlog");
            Assert.Equal("3", slider.GetAttribute("min"));
            Assert.Equal("7", slider.GetAttribute("max")); // the typed pace
            Assert.Equal("3", slider.GetAttribute("value"));
            Assert.Equal("true", Measured(band, "backlog").GetAttribute("aria-pressed"));
            Assert.Empty(band.FindAll("[data-testid='roadmap-pace-fell-back-backlog']"));
        });
    }

    [Fact]
    public async Task A_lane_measuring_more_than_was_typed_runs_the_other_way_round()
    {
        Configure("JSdotNet/Backlog");
        Finished.Add(new CompletedEffortDto(PaceToday, 28) { RepositoryAliases = ["backlog"] }); // 14 a week
        await ImportedAsync("plan-a", "backlog");

        using var context = Context();
        var band = Banded(context);

        band.WaitForAssertion(() =>
        {
            Assert.Equal("7", Slider(band, "backlog").GetAttribute("min"));
            Assert.Equal("14", Slider(band, "backlog").GetAttribute("max"));
        });
    }

    [Fact]
    public async Task The_info_mark_lists_what_was_managed_over_two_four_and_eight_weeks_and_what_is_placing_the_lane()
    {
        Configure("JSdotNet/Backlog");
        Finished.Add(new CompletedEffortDto(PaceToday, 14) { RepositoryAliases = ["backlog"] });
        await ImportedAsync("plan-a", "backlog");

        using var context = Context();
        var band = Banded(context);

        band.WaitForAssertion(() =>
        {
            var hint = Hint(band, "backlog");
            Assert.Contains("Managed in backlog", hint, StringComparison.Ordinal);
            Assert.Contains("last 2 weeks 7 pt/wk", hint, StringComparison.Ordinal);
            Assert.Contains("4 weeks 4 pt/wk", hint, StringComparison.Ordinal);
            Assert.Contains("8 weeks 2 pt/wk", hint, StringComparison.Ordinal);
            Assert.Contains("Placing at 7 pt/wk \u2014 measured over 2 weeks", hint, StringComparison.Ordinal);
            Assert.DoesNotContain("nothing finished", hint, StringComparison.Ordinal);
        });

        // A real button with a name, not a native tooltip.
        var trigger = band.Find("[data-testid='roadmap-pace-hint-trigger-backlog']");
        Assert.Equal("About the pace in backlog", trigger.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task A_stretch_that_measured_nothing_says_so_and_the_next_one_is_placing_the_lane()
    {
        Configure("JSdotNet/Backlog");
        // Twenty days ago: outside the last two weeks, inside the last four and eight.
        Finished.Add(new CompletedEffortDto(PaceToday.AddDays(-20), 28) { RepositoryAliases = ["backlog"] });
        await ImportedAsync("plan-a", "backlog");

        using var context = Context();
        var band = Banded(context);

        band.WaitForAssertion(() =>
        {
            var hint = Hint(band, "backlog");
            Assert.Contains("last 2 weeks nothing finished", hint, StringComparison.Ordinal);
            Assert.Contains("4 weeks 7 pt/wk", hint, StringComparison.Ordinal);
            Assert.Contains("8 weeks 4 pt/wk", hint, StringComparison.Ordinal);
            Assert.Contains("measured over 4 weeks", hint, StringComparison.Ordinal);
            Assert.Equal("true", Measured(band, "backlog").GetAttribute("aria-pressed"));

            // No two-week pace to run to: half to double the typed one, holding the pace in use.
            Assert.Equal("3", Slider(band, "backlog").GetAttribute("min"));
            Assert.Equal("14", Slider(band, "backlog").GetAttribute("max"));
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
            Assert.Contains("last 2 weeks 21 pt/wk", Hint(band, Default), StringComparison.Ordinal); // 42 over 2 weeks
            Assert.Contains("last 2 weeks 7 pt/wk", Hint(band, "backlog"), StringComparison.Ordinal);

            // Site finished nothing of its own, so it places by Mine.
            Assert.Empty(band.FindAll("[data-testid='roadmap-pace-measured-site']"));
            Assert.NotNull(band.Find("[data-testid='roadmap-pace-fell-back-site']"));
            Assert.Contains("Managed in site", Hint(band, "site"), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Finished_work_updates_the_measured_paces()
    {
        await UnfiledAsync("plan-a");

        using var context = Context();
        var band = Banded(context);
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace-measured-default']"));

        Finished.Add(new CompletedEffortDto(PaceToday, 28));
        WorkChanges.Raise();

        band.WaitForAssertion(() =>
        {
            Assert.Contains("last 2 weeks 14 pt/wk", Hint(band, Default), StringComparison.Ordinal);
            Assert.Equal("true", Measured(band, Default).GetAttribute("aria-pressed"));
        });
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
            Assert.Equal("3 pt/wk", ValueText(band, Default));
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
        Assert.Empty(band.FindAll("[data-testid='roadmap-pace-measured-backlog']"));
    }

    [Fact]
    public async Task A_lane_that_measured_nothing_and_was_set_by_hand_offers_a_reset_back_to_Mine()
    {
        Configure("JSdotNet/Backlog");
        await ImportedAsync("plan-a", "backlog");

        using var context = Context();
        var band = Banded(context);

        Slider(band, "backlog").Change("9");

        band.WaitForAssertion(() =>
        {
            var reset = Measured(band, "backlog");
            Assert.Equal("Reset", reset.TextContent.Trim());
            Assert.Equal("false", reset.GetAttribute("aria-pressed"));
            Assert.Empty(band.FindAll("[data-testid='roadmap-pace-fell-back-backlog']"));
        });

        Measured(band, "backlog").Click();

        Assert.NotEqual(PaceSource.Set, PaceFile.SourceFor("backlog"));
        band.WaitForAssertion(() =>
        {
            Assert.Equal("7 pt/wk", ValueText(band, "backlog"));
            Assert.Contains(
                "Using Mine: 7 pt/wk",
                band.Find("[data-testid='roadmap-pace-fell-back-backlog']").TextContent,
                StringComparison.Ordinal);
            Assert.Empty(band.FindAll("[data-testid='roadmap-pace-measured-backlog']"));
        });
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

        Manual(band).Change("3");

        Assert.Equal(3m, PaceFile.StoryPointsPerWeek);
        Assert.False(PaceFile.KeepsOwnPace("backlog"));

        band.WaitForAssertion(() =>
        {
            Assert.Equal("3", Manual(band).GetAttribute("value"));
            Assert.Contains(
                "Using Mine: 3 pt/wk",
                band.Find("[data-testid='roadmap-pace-fell-back-backlog']").TextContent,
                StringComparison.Ordinal);
            Assert.Contains(
                "Using Mine: 3 pt/wk",
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
    public async Task Releasing_the_slider_sets_that_lanes_own_pace_and_leaves_the_others()
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Site");
        Finished.Add(new CompletedEffortDto(PaceToday, 14) { RepositoryAliases = ["site"] }); // 7 a week
        Finished.Add(new CompletedEffortDto(PaceToday, 14) { RepositoryAliases = ["backlog"] });
        await ImportedAsync("plan-a", "backlog");
        await ImportedAsync("plan-b", "site");

        using var context = Context();
        var band = Banded(context);

        Slider(band, "site").Change("5");

        Assert.Equal(PaceSource.Set, PaceFile.SourceFor("site"));
        Assert.Equal(5m, PaceFile.StoryPointsPerWeekFor("site"));
        Assert.Equal(PaceSource.Manual, PaceFile.Source);
        Assert.Equal(7m, PaceFile.StoryPointsPerWeek); // the heading's typed pace is untouched
        Assert.Equal(PaceSource.Manual, PaceFile.SourceFor("backlog"));

        band.WaitForAssertion(() =>
        {
            Assert.Equal("5 pt/wk", ValueText(band, "site"));
            Assert.Equal("5", Slider(band, "site").GetAttribute("value"));
            Assert.Equal("false", Measured(band, "site").GetAttribute("aria-pressed"));
            Assert.Contains("Placing at 5 pt/wk \u2014 set by hand", Hint(band, "site"), StringComparison.Ordinal);

            // backlog is where it was.
            Assert.Equal("7 pt/wk", ValueText(band, "backlog"));
            Assert.Equal("true", Measured(band, "backlog").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public async Task Measured_returns_the_lane_to_its_two_week_pace()
    {
        Configure("JSdotNet/Backlog");
        Finished.Add(new CompletedEffortDto(PaceToday, 14) { RepositoryAliases = ["backlog"] }); // 7 a week
        await ImportedAsync("plan-a", "backlog");

        using var context = Context();
        var band = Banded(context);

        Slider(band, "backlog").Change("4");
        band.WaitForAssertion(() => Assert.Equal("false", Measured(band, "backlog").GetAttribute("aria-pressed")));

        Measured(band, "backlog").Click();

        Assert.Equal(PaceSource.LastTwoWeeks, PaceFile.SourceFor("backlog"));
        band.WaitForAssertion(() =>
        {
            Assert.Equal("true", Measured(band, "backlog").GetAttribute("aria-pressed"));
            Assert.Equal("7 pt/wk", ValueText(band, "backlog"));
            Assert.Equal("7", Slider(band, "backlog").GetAttribute("value"));
        });
    }

    [Fact]
    public async Task Moving_the_thumb_shows_the_figure_as_it_goes_and_saves_nothing_until_it_is_let_go()
    {
        Configure("JSdotNet/Backlog");
        Finished.Add(new CompletedEffortDto(PaceToday, 14) { RepositoryAliases = ["backlog"] });
        await ImportedAsync("plan-a", "backlog");

        using var context = Context();
        var band = Banded(context);

        Slider(band, "backlog").Input("5");

        band.WaitForAssertion(() => Assert.Equal("5 pt/wk", ValueText(band, "backlog")));
        Assert.Equal(PaceSource.Manual, PaceFile.SourceFor("backlog"));
        Assert.False(PaceFile.KeepsOwnPace("backlog"));
    }

    [Fact]
    public async Task Letting_go_of_the_thumb_keeps_it_there_while_the_pace_is_read_back()
    {
        Configure("JSdotNet/Backlog");
        Finished.Add(new CompletedEffortDto(PaceToday, 14) { RepositoryAliases = ["backlog"] }); // 7 a week
        await ImportedAsync("plan-a", "backlog");

        using var context = Context();
        var band = Banded(context);

        HoldReadsOfFinished();
        Slider(band, "backlog").Change("5");

        // Saved, and still being read back: neither the thumb nor the figure goes back
        // to the pace it was let go from.
        band.WaitForAssertion(() =>
        {
            Assert.Equal("5", Slider(band, "backlog").GetAttribute("value"));
            Assert.Equal("5 pt/wk", ValueText(band, "backlog"));
        });

        ReleaseReadsOfFinished();

        band.WaitForAssertion(() =>
        {
            Assert.Equal("5", Slider(band, "backlog").GetAttribute("value"));
            Assert.Contains("Placing at 5 pt/wk — set by hand", Hint(band, "backlog"), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Letting_go_of_the_thumb_reads_the_finished_work_once_per_control_and_once_for_the_band()
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Site", "JSdotNet/Docs");
        Finished.Add(new CompletedEffortDto(PaceToday, 14) { RepositoryAliases = ["backlog"] });
        await ImportedAsync("plan-a", "backlog");
        await ImportedAsync("plan-b", "site");
        await ImportedAsync("plan-c", "docs");

        using var context = Context();
        var band = Banded(context);
        band.WaitForAssertion(() => Assert.Equal("7 pt/wk", ValueText(band, "backlog")));
        var controls = band.FindAll("[data-testid^='roadmap-pace-slider-']").Count;
        var before = ReadsOfFinished;

        Slider(band, "backlog").Change("5");
        band.WaitForAssertion(() => Assert.Contains(
            "Placing at 5 pt/wk — set by hand", Hint(band, "backlog"), StringComparison.Ordinal));

        // One pace saved is one change heard: each band's control and the heading's
        // typed pace read once, the control let go of once more for its own answer,
        // and the band redraws from one read.
        Assert.Equal(controls + 1 + 1 + 1, ReadsOfFinished - before);
    }

    [Fact]
    public async Task A_pace_set_by_hand_below_the_line_makes_the_line_grow_to_hold_it()
    {
        Configure("JSdotNet/Backlog");
        Finished.Add(new CompletedEffortDto(PaceToday, 6) { RepositoryAliases = ["backlog"] }); // 3 a week
        await ImportedAsync("plan-a", "backlog");
        _ = PaceFile.Set(2m, "backlog");
        _ = PaceFile.Choose(PaceSource.Set, "backlog");

        using var context = Context();
        var band = Banded(context);

        // In use is 2, below the line that runs to the typed 7, so the line reaches down to it.
        band.WaitForAssertion(() =>
        {
            Assert.Equal("2", Slider(band, "backlog").GetAttribute("min"));
            Assert.Equal("2 pt/wk", ValueText(band, "backlog"));
        });
    }

    [Fact]
    public async Task The_default_bands_slider_writes_the_headings_typed_pace()
    {
        await UnfiledAsync("plan-a");

        using var context = Context();
        var band = Banded(context);

        Slider(band, Default).Change("10");

        Assert.Equal(10m, PaceFile.StoryPointsPerWeek);
        Assert.Equal(PaceSource.Set, PaceFile.Source);
        band.WaitForAssertion(() => Assert.Equal("10", Manual(band).GetAttribute("value")));
    }

    [Fact]
    public async Task Moving_the_default_bands_slider_leaves_a_measuring_lane_as_it_was()
    {
        Configure("JSdotNet/Backlog");
        Finished.Add(new CompletedEffortDto(PaceToday, 6) { RepositoryAliases = ["backlog"] }); // 3 a week
        await ImportedAsync("plan-a", "backlog");
        await UnfiledAsync("plan-b");

        using var context = Context();
        var band = Banded(context);

        // Within the default line, which runs from what all the work measured (3) to Mine (7).
        Slider(band, Default).Change("5");
        band.WaitForAssertion(() => Assert.Equal("5", Manual(band).GetAttribute("value")));

        // The default band's figure is the heading's; it is nobody else's pace to place by.
        Assert.Equal(PaceSource.Manual, PaceFile.SourceFor("backlog"));
        band.WaitForAssertion(() =>
        {
            Assert.Equal("3 pt/wk", ValueText(band, "backlog"));
            Assert.Equal("3", Slider(band, "backlog").GetAttribute("value"));
            Assert.Equal("true", Measured(band, "backlog").GetAttribute("aria-pressed"));
            Assert.Contains("measured over 2 weeks", Hint(band, "backlog"), StringComparison.Ordinal);
            Assert.DoesNotContain("set by hand", Hint(band, "backlog"), StringComparison.Ordinal);
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
    public async Task Moving_the_default_slider_redraws_an_unfiled_plan()
    {
        Finished.Add(new CompletedEffortDto(PaceToday, 56)); // 28 a week over two weeks, 7 over eight
        var plan = await UnfiledAsync("plan-a");

        using var context = GatheringContext(14);
        var band = Banded(context);

        // Two weeks is in use until a pace is set: 14 points at 28 a week.
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 4), DrawnWindow(band, plan.Id)));

        Slider(band, Default).Change("7");

        // 14 points at 7 a week is two weeks.
        band.WaitForAssertion(() => Assert.Equal((plan.Start, 14), DrawnWindow(band, plan.Id)));
    }

    [Fact]
    public async Task Moving_a_lanes_slider_redraws_that_lanes_plans_and_no_others()
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Site");
        Finished.Add(new CompletedEffortDto(PaceToday, 28) { RepositoryAliases = ["backlog"] }); // 14 a week
        Finished.Add(new CompletedEffortDto(PaceToday, 28) { RepositoryAliases = ["site"] });    // 14 a week
        var backlog = await ImportedAsync("plan-a", "backlog");
        var site = await ImportedAsync("plan-b", "site");

        using var context = GatheringContext(14);
        var band = Banded(context);
        band.WaitForAssertion(() =>
        {
            Assert.Equal((backlog.Start, 7), DrawnWindow(band, backlog.Id));
            Assert.Equal((site.Start, 7), DrawnWindow(band, site.Id));
        });

        Slider(band, "backlog").Change("7");

        // 14 points at 7 a week: two weeks. Site stays at its measured 14 a week.
        band.WaitForAssertion(() =>
        {
            Assert.Equal((backlog.Start, 14), DrawnWindow(band, backlog.Id));
            Assert.Equal((site.Start, 7), DrawnWindow(band, site.Id));
        });
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

    private static AngleSharp.Dom.IElement Slider(IRenderedComponent<RoadmapBand> band, string scope) =>
        band.Find($"input[data-testid='roadmap-pace-slider-{scope}']");

    /// <summary>The figure beside the slider: what is in use, or where the thumb is.</summary>
    private static string ValueText(IRenderedComponent<RoadmapBand> band, string scope) =>
        band.Find($"[data-testid='roadmap-pace-value-{scope}']").TextContent.Trim();

    private static AngleSharp.Dom.IElement Measured(IRenderedComponent<RoadmapBand> band, string scope) =>
        band.Find($"[data-testid='roadmap-pace-measured-{scope}']");

    /// <summary>The info mark's explanation: the managed effort and what places the lane.</summary>
    private static string Hint(IRenderedComponent<RoadmapBand> band, string scope) =>
        band.Find($"[data-testid='roadmap-pace-hint-text-{scope}']").TextContent;
}
