using AngleSharp.Dom;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The steps inside a bar: the arithmetic that sizes them, the figures the collapsed
/// bar carries, and the disclosure that opens them. Asserted on what is rendered —
/// the rows, the widths, the words — because an exception inside a click handler
/// never reaches a bUnit test, and only the state it would have prevented does.
/// </summary>
public sealed class RoadmapTimelineStepsTests
{
    private static readonly RoadmapWindow Q1 = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

    private static readonly IReadOnlyList<RoadmapGroup> Plan =
    [
        new("delivery", "Delivery", [new RoadmapRow("build", "Build"), new RoadmapRow("ship", "Ship")])
    ];

    /// <summary>A chain of three, one step with no estimate, and one that is done
    /// without one: 5 of 13 estimated points done, and 2 unestimated.</summary>
    private static readonly IReadOnlyList<RoadmapStep> Steps =
    [
        new("parse", "Parse", 3, RoadmapStepTone.Done, "Done"),
        new("layout", "Layout", 2, RoadmapStepTone.Done, "Done", "After Parse"),
        new("draw", "Draw", 8, RoadmapStepTone.InProgress, "In progress", "After Layout"),
        new("docs", "Docs", null, RoadmapStepTone.Ready, "Ready"),
        new("tidy", "Tidy", null, RoadmapStepTone.Done, "Done")
    ];

    // Twelve days wide at a rem a day: 5 January to 16 January.
    private static readonly IReadOnlyList<RoadmapBar> Work =
    [
        new("alpha", "build", "Alpha", On(1, 5), On(1, 16), Steps: Steps),
        new("beta", "ship", "Beta", On(1, 19), On(1, 30))
    ];

    private static DateOnly On(int month, int day) => new(2026, month, day);

    private static IRenderedComponent<RoadmapTimeline> Chart(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<RoadmapTimeline>>? extra = null)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<RoadmapTimeline>(parameters =>
        {
            parameters
                .Add(timeline => timeline.Groups, Plan)
                .Add(timeline => timeline.Bars, Work)
                .Add(timeline => timeline.Window, Q1)
                // A quarter at its nominal length, so a day is exactly one rem.
                .Add(timeline => timeline.QuarterWidth, RoadmapWindow.NominalQuarterDays)
                .Add(timeline => timeline.TestId, "rm");

            extra?.Invoke(parameters);
        });
    }

    private static string Announcement(IRenderedComponent<RoadmapTimeline> view) =>
        view.Find("[data-testid='rm-announcement']").TextContent;

    private static IElement Toggle(IRenderedComponent<RoadmapTimeline> view) =>
        view.Find("[data-testid='rm-bar-alpha-toggle']");

    private static void Expand(IRenderedComponent<RoadmapTimeline> view)
    {
        Toggle(view).Click();
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".roadmap-step")));
    }

    // --- Widths ----------------------------------------------------------------

    [Fact]
    public void Estimated_steps_share_what_the_unestimated_minimums_leave_by_their_effort()
    {
        // 12rem, two unsized steps at 1rem each, so 10rem shared across 13 points.
        var spans = RoadmapStepLayout.Lay(Steps, 12);

        Assert.Equal([3 / 13d, 2 / 13d, 8 / 13d, 0, 0], spans.Select(span => span.Share));
        Assert.Equal(10 * 3 / 13d, spans[0].Width, 6);
        Assert.Equal(10 * 2 / 13d, spans[1].Width, 6);
        Assert.Equal(10 * 8 / 13d, spans[2].Width, 6);
        Assert.Equal(RoadmapStepLayout.MinStepWidthRem, spans[3].Width);
        Assert.Equal(RoadmapStepLayout.MinStepWidthRem, spans[4].Width);
    }

    [Fact]
    public void Steps_are_laid_end_to_end_in_the_order_given_and_fill_the_window()
    {
        var spans = RoadmapStepLayout.Lay(Steps, 12);

        Assert.Equal(0, spans[0].Offset);

        for (var index = 1; index < spans.Count; index++)
        {
            Assert.Equal(spans[index - 1].Offset + spans[index - 1].Width, spans[index].Offset, 6);
        }

        Assert.Equal(12, spans[^1].Offset + spans[^1].Width, 6);
    }

    [Fact]
    public void Unsized_steps_keep_their_minimum_even_when_it_is_wider_than_the_bar()
    {
        var spans = RoadmapStepLayout.Lay(
            [new RoadmapStep("a", "A", null), new RoadmapStep("b", "B", null), new RoadmapStep("c", "C", 5)],
            1.5);

        Assert.Equal(RoadmapStepLayout.MinStepWidthRem, spans[0].Width);
        Assert.Equal(RoadmapStepLayout.MinStepWidthRem, spans[1].Width);

        // Nothing left to share, so the estimated step is a sliver, not a negative.
        Assert.Equal(RoadmapStepLayout.HairlineRem, spans[2].Width);
    }

    // --- Progress figures -------------------------------------------------------

    [Fact]
    public void A_bar_counts_done_effort_over_total_effort_and_keeps_unsized_work_out_of_both()
    {
        var bar = Work[0];

        Assert.Equal(13, bar.TotalEffort);
        Assert.Equal(5, bar.DoneEffort);

        // "Tidy" is done with no estimate: counted here, not in the fill.
        Assert.Equal(2, bar.UnestimatedCount);
        Assert.Equal(5 / 13d, bar.DoneShare, 6);
    }

    [Fact]
    public void A_bar_with_nothing_estimated_draws_no_fill_rather_than_dividing_by_zero()
    {
        var bar = new RoadmapBar("x", "build", "X", On(1, 5), On(1, 9),
            Steps: [new RoadmapStep("a", "A", null, RoadmapStepTone.Done)]);

        Assert.Equal(0, bar.DoneShare);
        Assert.Equal(1, bar.UnestimatedCount);
    }

    [Fact]
    public void The_collapsed_bar_carries_its_fill_and_its_unestimated_count()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        var fill = view.Find("[data-testid='rm-bar-alpha-fill']");
        Assert.Contains("width: 38.462%", fill.GetAttribute("style"));
        Assert.Equal("true", fill.GetAttribute("aria-hidden"));

        Assert.Equal("2 unestimated", view.Find("[data-testid='rm-bar-alpha-unestimated']").TextContent);

        // Said in words too, since a width cannot be read out.
        Assert.Contains("5 of 13 points done, 2 unestimated",
            view.Find("[data-roadmap-bar='alpha'] .roadmap-bar__body .sr-only").TextContent);
    }

    [Fact]
    public void A_bar_with_no_steps_offers_nothing_to_expand_and_draws_no_fill()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        Assert.Empty(view.FindAll("[data-testid='rm-bar-beta-toggle']"));
        Assert.Empty(view.FindAll("[data-testid='rm-bar-beta-fill']"));
        Assert.Empty(view.FindAll("[data-testid='rm-bar-beta-unestimated']"));
    }

    // --- Expanding ---------------------------------------------------------------

    [Fact]
    public void The_disclosure_is_a_real_button_that_starts_collapsed()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        var toggle = Toggle(view);
        Assert.Equal("BUTTON", toggle.TagName);
        Assert.Equal("button", toggle.GetAttribute("type"));
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("Show the 5 steps of Alpha", toggle.GetAttribute("aria-label"));
        Assert.Empty(view.FindAll(".roadmap-step"));
    }

    [Fact]
    public void The_disclosure_comes_before_the_bar_in_tab_order_as_it_does_on_screen()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        var buttons = view.FindAll("[data-roadmap-bar='alpha'] button").ToList();

        Assert.Equal(["roadmap-bar__toggle", "roadmap-bar__body"], buttons.Select(button => button.ClassName));
    }

    [Fact]
    public void Expanded_a_bar_opens_one_row_per_step_in_the_order_it_was_handed()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        Expand(view);

        Assert.Equal(
            ["parse", "layout", "draw", "docs", "tidy"],
            view.FindAll(".roadmap-step").Select(step => step.GetAttribute("data-testid")!["rm-step-alpha-".Length..]));

        // One row each, directly under the bar's row and above the next lane.
        var rows = view.FindAll(".roadmap-timeline__row").Select(row => row.GetAttribute("data-testid")).ToList();
        Assert.Equal(
            ["rm-row-build", "rm-step-row-alpha-0", "rm-step-row-alpha-1", "rm-step-row-alpha-2",
             "rm-step-row-alpha-3", "rm-step-row-alpha-4", "rm-row-ship"],
            rows);

        // The sidebar keeps pace, so the two columns stay the same height.
        Assert.Equal(rows.Count, view.FindAll(".roadmap-timeline__row-name").Count);
        Assert.Equal("true", Toggle(view).GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Each_step_is_drawn_inside_the_bar_window_at_its_share_of_the_effort()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        Expand(view);

        // The bar starts four days into the window, at 4rem, and is 12rem wide.
        var spans = RoadmapStepLayout.Lay(Steps, 12);
        var drawn = view.FindAll(".roadmap-step").ToList();

        for (var index = 0; index < drawn.Count; index++)
        {
            var style = drawn[index].GetAttribute("style");
            Assert.Contains($"left: {RoadmapGeometry.N(4 + spans[index].Offset)}rem", style);
            Assert.Contains($"width: {RoadmapGeometry.N(spans[index].Width)}rem", style);
        }

        Assert.Contains("23% of the estimated effort", drawn[0].GetAttribute("aria-label"));
    }

    [Fact]
    public void An_unestimated_step_is_marked_and_its_name_says_so()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        Expand(view);

        var docs = view.Find("[data-testid='rm-step-alpha-docs']");
        Assert.Contains("roadmap-step--unestimated", docs.ClassList);
        Assert.Equal("?", docs.QuerySelector(".roadmap-step__marker")!.TextContent);
        Assert.Equal("Docs. Ready. Not estimated, drawn at the minimum width.", docs.GetAttribute("aria-label"));

        var parse = view.Find("[data-testid='rm-step-alpha-parse']");
        Assert.DoesNotContain("roadmap-step--unestimated", parse.ClassList);
        Assert.Null(parse.QuerySelector(".roadmap-step__marker"));
    }

    [Fact]
    public void A_step_wears_its_status_colour_and_says_its_status_in_words()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        Expand(view);

        var draw = view.Find("[data-testid='rm-step-alpha-draw']");
        Assert.Contains("roadmap-step--inprogress", draw.ClassList);
        Assert.Contains("In progress", draw.GetAttribute("aria-label"));
        Assert.Contains("After Layout", draw.GetAttribute("aria-label"));
        Assert.Contains("roadmap-step--done", view.Find("[data-testid='rm-step-alpha-parse']").ClassList);
    }

    [Fact]
    public void Expanding_and_collapsing_are_both_announced()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        Expand(view);
        Assert.Equal(
            "Showing 5 steps of Alpha, one row each, sized by effort rather than by date.",
            Announcement(view));

        Toggle(view).Click();
        view.WaitForAssertion(() => Assert.Empty(view.FindAll(".roadmap-step")));
        Assert.Equal("Steps of Alpha hidden.", Announcement(view));
        Assert.Equal("false", Toggle(view).GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Moving_a_grabbed_bar_down_skips_open_step_rows_and_lands_on_the_next_lane()
    {
        using var context = new BunitContext();
        RoadmapChange? change = null;
        var view = Chart(context, parameters => parameters
            .Add(timeline => timeline.OnBarChanged, (RoadmapChange moved) => change = moved));

        Expand(view);

        var body = view.Find("[data-roadmap-bar='alpha'] .roadmap-bar__body");
        body.KeyDown(new KeyboardEventArgs { Key = " " });
        view.Find("[data-roadmap-bar='alpha'] .roadmap-bar__body").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        view.Find("[data-roadmap-bar='alpha'] .roadmap-bar__body").KeyDown(new KeyboardEventArgs { Key = " " });

        view.WaitForAssertion(() => Assert.Equal("ship", change?.RowId));
    }
}
