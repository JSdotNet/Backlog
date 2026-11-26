using Microsoft.JSInterop;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Tracing a dependency chain: what a hover or focus draws out, how the badge and T
/// pin it so it stays while the reader follows it, how Escape lets it go without
/// taking a grab with it, and how the other parts of split work are found.
/// </summary>
public sealed class RoadmapTimelineTraceTests
{
    private static readonly RoadmapWindow Q1 = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

    private static readonly IReadOnlyList<RoadmapGroup> Plan =
    [
        new("delivery", "Delivery", [new RoadmapRow("build", "Build"), new RoadmapRow("ship", "Ship")], "#3366ff"),
        new("dates", "Dates", [new RoadmapRow("moments", "Moments", RoadmapRowKind.Milestones)])
    ];

    private static DateOnly On(int month, int day) => new(2026, month, day);

    /// <summary>A chain of four — alpha, then beta, then gamma, then delta — and
    /// epsilon and the launch, which nothing joins to it.</summary>
    private static readonly IReadOnlyList<RoadmapBar> Work =
    [
        new("alpha", "build", "Alpha", On(1, 5), On(1, 9)),
        new("beta", "ship", "Beta", On(1, 12), On(1, 16)),
        new("gamma", "build", "Gamma", On(1, 19), On(1, 23)),
        new("delta", "ship", "Delta", On(1, 26), On(1, 30)),
        new("epsilon", "build", "Epsilon", On(2, 16), On(2, 27))
    ];

    private static readonly RoadmapLink[] Chain = [new("alpha", "beta"), new("beta", "gamma"), new("gamma", "delta")];

    private static readonly IReadOnlyList<RoadmapMilestone> Moments = [new("launch", "moments", "Launch", On(3, 2))];

    /// <summary>Zoomed in to a little over a rem a day, so a working week is wide
    /// enough to hold its badge (<see cref="RoadmapTimelineBar.BadgeRoomRem"/>).</summary>
    private const double Wide = 96;

    private static IRenderedComponent<RoadmapTimeline> Chart(
        BunitContext context,
        List<string>? opened = null,
        IReadOnlyList<RoadmapBar>? bars = null,
        IReadOnlyList<RoadmapLink>? links = null,
        double quarterWidth = Wide)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<RoadmapTimeline>(parameters => parameters
            .Add(timeline => timeline.QuarterWidth, quarterWidth)
            .Add(timeline => timeline.Groups, Plan)
            .Add(timeline => timeline.Bars, bars ?? Work)
            .Add(timeline => timeline.Milestones, Moments)
            .Add(timeline => timeline.Links, links ?? Chain)
            .Add(timeline => timeline.Window, Q1)
            .Add(timeline => timeline.OnBarSelected, id => opened?.Add(id))
            .Add(timeline => timeline.TestId, "rm"));
    }

    private static string Classes(IRenderedComponent<RoadmapTimeline> view, string bar) =>
        view.Find($"[data-testid='rm-bar-{bar}']").ClassName!;

    private static bool Has(IRenderedComponent<RoadmapTimeline> view, string bar, string state) =>
        Classes(view, bar).Split(' ').Contains($"roadmap-bar--{state}");

    private static AngleSharp.Dom.IElement Body(IRenderedComponent<RoadmapTimeline> view, string bar) =>
        view.Find($"[data-testid='rm-bar-{bar}'] .roadmap-bar__body");

    private static AngleSharp.Dom.IElement Badge(IRenderedComponent<RoadmapTimeline> view, string bar) =>
        view.Find($"[data-testid='rm-bar-{bar}-badge']");

    private static string Announcement(IRenderedComponent<RoadmapTimeline> view) =>
        view.Find("[data-testid='rm-announcement']").TextContent;

    private static readonly KeyboardEventArgs T = new() { Key = "t" };
    private static readonly KeyboardEventArgs Escape = new() { Key = "Escape" };
    private static readonly KeyboardEventArgs Space = new() { Key = " " };

    [Fact]
    public void Pointing_at_one_link_of_a_chain_traces_the_whole_chain_and_dims_the_rest()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        view.Find("[data-testid='rm-bar-alpha']").PointerEnter();

        // Every arrow on the chain, three links from alpha to delta, not only alpha's own.
        Assert.Equal(3, view.FindAll(".roadmap-timeline__link--traced").Count);
        Assert.True(Has(view, "beta", "related"));
        Assert.True(Has(view, "delta", "related"));

        // What is off the chain recedes; what is on it, and the traced bar, does not.
        Assert.True(Has(view, "epsilon", "dimmed"));
        Assert.Contains("roadmap-milestone--dimmed", view.Find("[data-roadmap-node='launch']").ClassName!, StringComparison.Ordinal);
        Assert.False(Has(view, "alpha", "dimmed"));
        Assert.False(Has(view, "delta", "dimmed"));

        view.Find("[data-testid='rm-bar-alpha']").PointerLeave();

        Assert.Empty(view.FindAll(".roadmap-bar--dimmed"));
        Assert.Empty(view.FindAll(".roadmap-timeline__link--traced"));
    }

    [Fact]
    public void Pointing_at_something_with_no_arrows_dims_nothing()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        view.Find("[data-testid='rm-bar-epsilon']").PointerEnter();

        Assert.Empty(view.FindAll(".roadmap-bar--dimmed"));
        Assert.Empty(view.FindAll(".roadmap-milestone--dimmed"));
        Assert.Empty(view.FindAll(".roadmap-timeline__links--tracing"));
    }

    [Fact]
    public void The_badge_pins_the_trace_without_opening_the_bar_and_a_second_press_lets_it_go()
    {
        using var context = new BunitContext();
        var opened = new List<string>();
        var view = Chart(context, opened);

        var badge = Badge(view, "gamma");
        Assert.Equal("false", badge.GetAttribute("aria-pressed"));
        Assert.Equal("Trace 2 dependencies of Gamma", badge.GetAttribute("aria-label"));
        Assert.Equal("-1", badge.GetAttribute("tabindex"));

        badge.Click();

        Assert.Empty(opened);
        Assert.Equal("true", Badge(view, "gamma").GetAttribute("aria-pressed"));

        // The name is the act and stays put; aria-pressed alone says it is pinned.
        Assert.Equal("Trace 2 dependencies of Gamma", Badge(view, "gamma").GetAttribute("aria-label"));
        Assert.True(Has(view, "gamma", "pinned"));
        Assert.Equal(3, view.FindAll(".roadmap-timeline__link--traced").Count);
        Assert.StartsWith("Tracing Gamma: 3 connected items.", Announcement(view), StringComparison.Ordinal);

        // The pointer leaving is not the reader letting go.
        view.Find("[data-testid='rm-bar-gamma']").PointerLeave();
        Assert.Equal(3, view.FindAll(".roadmap-timeline__link--traced").Count);
        Assert.True(Has(view, "epsilon", "dimmed"));

        Badge(view, "gamma").Click();

        Assert.Empty(opened);
        Assert.False(Has(view, "gamma", "pinned"));
        Assert.Empty(view.FindAll(".roadmap-timeline__link--traced"));
        Assert.Equal("Stopped tracing Gamma.", Announcement(view));
    }

    [Fact]
    public void A_badge_carries_no_grip_so_a_press_on_it_never_starts_a_drag()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        var badge = Badge(view, "beta");

        // components.js starts a drag only from a [data-roadmap-grip]; the badge is
        // neither one nor inside the body that is.
        Assert.Null(badge.GetAttribute("data-roadmap-grip"));
        Assert.Null(badge.Closest("[data-roadmap-grip]"));
    }

    [Fact]
    public void Pinning_another_bar_moves_the_pin_and_the_pin_wins_over_a_hover()
    {
        using var context = new BunitContext();
        var view = Chart(context, links: [new("alpha", "beta"), new("gamma", "delta")]);

        Badge(view, "beta").Click();
        Assert.True(Has(view, "beta", "pinned"));

        // Pointing elsewhere while pinned leaves the pinned trace drawn.
        view.Find("[data-testid='rm-bar-delta']").PointerEnter();
        Assert.True(Has(view, "alpha", "related"));
        Assert.False(Has(view, "gamma", "related"));
        Assert.True(Has(view, "delta", "dimmed"));
        view.Find("[data-testid='rm-bar-delta']").PointerLeave();

        Badge(view, "delta").Click();

        Assert.False(Has(view, "beta", "pinned"));
        Assert.True(Has(view, "delta", "pinned"));
        Assert.True(Has(view, "gamma", "related"));
        Assert.True(Has(view, "alpha", "dimmed"));
    }

    [Fact]
    public void T_on_a_focused_bar_pins_its_trace_and_T_again_clears_it()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        Body(view, "beta").KeyDown(T);

        Assert.True(Has(view, "beta", "pinned"));
        Assert.Equal("true", Badge(view, "beta").GetAttribute("aria-pressed"));
        Assert.Contains("Dependencies traced", Body(view, "beta").TextContent, StringComparison.Ordinal);

        Body(view, "beta").KeyDown(T);

        Assert.False(Has(view, "beta", "pinned"));
        Assert.Equal("Stopped tracing Beta.", Announcement(view));
    }

    [Fact]
    public void T_on_a_focused_milestone_pins_its_trace()
    {
        using var context = new BunitContext();
        var view = Chart(context, links: [.. Chain, new("delta", "launch")]);

        view.Find("[data-testid='rm-milestone-launch']").KeyDown(T);

        Assert.Contains("roadmap-milestone--pinned", view.Find("[data-roadmap-node='launch']").ClassName!, StringComparison.Ordinal);
        Assert.Equal(4, view.FindAll(".roadmap-timeline__link--traced").Count);
        Assert.True(Has(view, "epsilon", "dimmed"));
    }

    [Fact]
    public void Escape_with_a_pin_and_nothing_grabbed_clears_the_pin()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        Badge(view, "gamma").Click();
        Body(view, "epsilon").KeyDown(Escape);

        Assert.Empty(view.FindAll(".roadmap-bar--pinned"));
        Assert.Empty(view.FindAll(".roadmap-timeline__link--traced"));
        Assert.Equal("Stopped tracing Gamma.", Announcement(view));
    }

    [Fact]
    public void Escape_while_a_bar_is_grabbed_cancels_the_grab_and_keeps_the_pin()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        Badge(view, "gamma").Click();
        Body(view, "epsilon").KeyDown(Space);
        Assert.True(Has(view, "epsilon", "grabbed"));

        Body(view, "epsilon").KeyDown(Escape);

        Assert.False(Has(view, "epsilon", "grabbed"));
        Assert.True(Has(view, "gamma", "pinned"));

        // The next Escape, with nothing grabbed, is the one that lets the pin go.
        Body(view, "epsilon").KeyDown(Escape);
        Assert.False(Has(view, "gamma", "pinned"));
    }

    [Fact]
    public void A_click_on_a_bar_still_opens_it_while_a_trace_is_pinned()
    {
        using var context = new BunitContext();
        var opened = new List<string>();
        var view = Chart(context, opened);

        Badge(view, "gamma").Click();
        Body(view, "epsilon").Click();

        Assert.Equal(["epsilon"], opened);
        Assert.True(Has(view, "gamma", "pinned"));
    }

    [Fact]
    public void The_first_thing_in_a_chain_has_a_badge_too_and_a_bar_with_no_arrows_has_none()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        // Nothing points at alpha, but its one arrow out is a chain to pin.
        Assert.Equal("1", Badge(view, "alpha").TextContent);
        Assert.Equal("Trace 1 dependency of Alpha", Badge(view, "alpha").GetAttribute("aria-label"));

        Badge(view, "alpha").Click();
        Assert.True(Has(view, "alpha", "pinned"));
        Assert.Equal(3, view.FindAll(".roadmap-timeline__link--traced").Count);

        Assert.Empty(view.FindAll("[data-testid='rm-bar-epsilon-badge']"));
    }

    [Fact]
    public void A_bar_too_short_to_hold_its_badge_leaves_it_off_and_T_still_pins_it()
    {
        using var context = new BunitContext();

        // At the default zoom a working week is under a rem: a badge laid over it would
        // take every press meant to open the bar.
        var opened = new List<string>();
        var view = Chart(context, opened, quarterWidth: 16);

        Assert.Empty(view.FindAll(".roadmap-bar__badge"));
        Assert.False(Has(view, "gamma", "has-badge"));

        // The count is still said, and the whole sliver still opens the bar.
        Assert.Contains("2 dependencies", Body(view, "gamma").TextContent, StringComparison.Ordinal);
        Body(view, "gamma").Click();
        Assert.Equal(["gamma"], opened);

        Body(view, "gamma").KeyDown(T);
        Assert.True(Has(view, "gamma", "pinned"));
    }

    [Fact]
    public void A_wide_enough_bar_holds_its_badge_and_reserves_room_for_it()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        Assert.True(Has(view, "gamma", "has-badge"));
        Assert.NotNull(Badge(view, "gamma"));
    }

    [Fact]
    public void A_pin_on_a_bar_that_leaves_the_chart_is_let_go_and_hovering_traces_again()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        Badge(view, "gamma").Click();
        Assert.True(Has(view, "gamma", "pinned"));

        // The host redraws the plan without gamma.
        view.Render(parameters => parameters.Add(timeline => timeline.Bars, [.. Work.Where(bar => bar.Id != "gamma")]));

        Assert.Empty(view.FindAll(".roadmap-bar--pinned"));
        Assert.Equal("Stopped tracing Gamma: it is no longer on the chart.", Announcement(view));

        // Hover is no longer held hostage by a pin on nothing.
        view.Find("[data-testid='rm-bar-alpha']").PointerEnter();
        Assert.Single(view.FindAll(".roadmap-timeline__link--traced"));
        Assert.True(Has(view, "beta", "related"));
        Assert.True(Has(view, "epsilon", "dimmed"));
    }

    [Fact]
    public void A_part_at_an_arrows_far_end_reads_as_both_related_and_a_sibling()
    {
        using var context = new BunitContext();

        // One plan drawn in two rows, its first part waiting into its second — the way a
        // plan handed over between repositories is drawn.
        IReadOnlyList<RoadmapBar> parts =
        [
            new("plan@a", "build", "Plan", On(1, 5), On(1, 9), PartOf: "plan", PartIndex: 1, PartCount: 2),
            new("plan@b", "ship", "Plan", On(1, 12), On(1, 16), PartOf: "plan", PartIndex: 2, PartCount: 2),
            new("other", "build", "Other", On(2, 2), On(2, 6))
        ];

        var view = Chart(context, bars: parts, links: [new("plan@a", "plan@b")]);

        view.Find("[data-testid='rm-bar-plan-a']").PointerEnter();

        Assert.True(Has(view, "plan-b", "related"));
        Assert.True(Has(view, "plan-b", "sibling"));
        Assert.False(Has(view, "plan-b", "dimmed"));
        Assert.True(Has(view, "other", "dimmed"));
    }

    [Fact]
    public void A_sibling_off_the_chain_is_marked_and_not_dimmed()
    {
        using var context = new BunitContext();

        IReadOnlyList<RoadmapBar> parts =
        [
            new("plan@a", "build", "Plan", On(1, 5), On(1, 9), PartOf: "plan", PartIndex: 1, PartCount: 2),
            new("plan@b", "ship", "Plan", On(1, 12), On(1, 16), PartOf: "plan", PartIndex: 2, PartCount: 2),
            new("before", "ship", "Before", On(1, 5), On(1, 7)),
            new("other", "build", "Other", On(2, 2), On(2, 6))
        ];

        var view = Chart(context, bars: parts, links: [new("before", "plan@a")]);

        Badge(view, "plan-a").Click();

        Assert.True(Has(view, "plan-b", "sibling"));
        Assert.False(Has(view, "plan-b", "dimmed"));
        Assert.True(Has(view, "other", "dimmed"));
    }
}
