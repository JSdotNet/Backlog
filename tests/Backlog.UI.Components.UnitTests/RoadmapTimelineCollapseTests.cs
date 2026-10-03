using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A band folded to one lane — every bar of its rows drawn on a single row — and
/// opened again. Pinned here: only a chart that opts in offers it, and then only on a
/// band with bars; folded, the band is one row with no room for its content, its bars
/// and their arrows are still drawn, and a bar dropped or a place picked on the folded
/// row lands on one of the band's real lanes rather than on a row the host never made.
/// </summary>
public sealed class RoadmapTimelineCollapseTests
{
    private static readonly RoadmapWindow Q1 = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

    private static readonly IReadOnlyList<RoadmapGroup> Plan =
    [
        new("dates", "Dates", [new RoadmapRow("moments", "Moments", RoadmapRowKind.Milestones)]),
        new("backlog", "backlog",
            [
                new RoadmapRow("backlog-lane", string.Empty),
                new RoadmapRow("backlog-lane-2", string.Empty),
                new RoadmapRow("platform", "platform")
            ],
            ContentRows: 2),
        new("docs", "docs", [new RoadmapRow("api", "api")])
    ];

    private static readonly IReadOnlyList<RoadmapBar> Work =
    [
        new("alpha", "backlog-lane", "Alpha", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 30)),
        new("beta", "backlog-lane-2", "Beta", new DateOnly(2026, 1, 12), new DateOnly(2026, 2, 6)),
        new("gamma", "platform", "Gamma", new DateOnly(2026, 2, 9), new DateOnly(2026, 2, 20)),
        new("delta", "api", "Delta", new DateOnly(2026, 2, 23), new DateOnly(2026, 3, 6))
    ];

    private static readonly RenderFragment<RoadmapGroup> Content = group => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "data-testid", $"content-{group.Id}");
        builder.AddContent(2, "Pace");
        builder.CloseElement();
    };

    private static IRenderedComponent<RoadmapTimeline> Chart(
        BunitContext context,
        bool collapsible = true,
        Action<ComponentParameterCollectionBuilder<RoadmapTimeline>>? extra = null)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<RoadmapTimeline>(parameters =>
        {
            parameters
                .Add(timeline => timeline.Groups, Plan)
                .Add(timeline => timeline.Bars, Work)
                .Add(timeline => timeline.Links, [new RoadmapLink("beta", "delta")])
                .Add(timeline => timeline.Window, Q1)
                .Add(timeline => timeline.GroupContent, Content)
                .Add(timeline => timeline.Collapsible, collapsible)
                .Add(timeline => timeline.TestId, "rm");

            extra?.Invoke(parameters);
        });
    }

    private static List<string> TrackRows(IRenderedComponent<RoadmapTimeline> view) =>
        [.. view.FindAll(".roadmap-timeline__body > .roadmap-timeline__row").Select(row => row.GetAttribute("data-testid")!)];

    private static void Fold(IRenderedComponent<RoadmapTimeline> view, string group) =>
        view.Find($"[data-testid='rm-group-{group}-collapse']").Click();

    [Fact]
    public void Only_a_chart_that_opts_in_offers_the_toggle_and_only_on_a_band_with_bars()
    {
        using var context = new BunitContext();

        Assert.Empty(Chart(context, collapsible: false).FindAll(".roadmap-timeline__group-toggle"));

        var view = Chart(context);

        Assert.Empty(view.FindAll("[data-testid='rm-group-dates-collapse']"));

        var toggle = view.Find("[data-testid='rm-group-backlog-collapse']");
        Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("Collapse backlog to one lane", toggle.GetAttribute("aria-label"));

        // Outside anything hidden from a screen reader: it is a control.
        Assert.Null(toggle.Closest("[aria-hidden='true']"));
    }

    [Fact]
    public void A_folded_band_is_one_row_holding_every_bar_of_its_lanes_and_no_content()
    {
        using var context = new BunitContext();

        var view = Chart(context);
        Fold(view, "backlog");

        Assert.Equal(
            ["rm-row-moments", "rm-row-backlog---collapsed-", "rm-row-api"],
            TrackRows(view));

        var folded = view.Find("[data-testid='rm-row-backlog---collapsed-']");
        Assert.Equal(3, folded.QuerySelectorAll(".roadmap-bar").Length);
        Assert.Empty(view.FindAll("[data-testid='content-backlog']"));
        Assert.Empty(view.FindAll(".roadmap-timeline__row--padding"));

        // One name in the sidebar for the one row, and the toggle now offers to open it.
        Assert.Single(view.FindAll("[data-testid='rm-group-backlog'] .roadmap-timeline__row-name"));
        var toggle = view.Find("[data-testid='rm-group-backlog-collapse']");
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("Expand backlog into its lanes", toggle.GetAttribute("aria-label"));

        // The arrow out of a folded bar is still drawn.
        Assert.Single(view.FindAll("[data-testid='rm-links'] path.roadmap-timeline__link"));
    }

    [Fact]
    public void Expanding_puts_every_lane_and_the_content_back()
    {
        using var context = new BunitContext();

        var view = Chart(context);
        var before = TrackRows(view);

        Fold(view, "backlog");
        Fold(view, "backlog");

        Assert.Equal(before, TrackRows(view));
        Assert.NotNull(view.Find("[data-testid='content-backlog']"));
    }

    [Fact]
    public void A_bar_dropped_on_a_folded_band_lands_on_its_first_lane()
    {
        using var context = new BunitContext();
        RoadmapChange? changed = null;

        var view = Chart(context, extra: parameters =>
            parameters.Add(timeline => timeline.OnBarChanged, (RoadmapChange change) => changed = change));
        Fold(view, "backlog");

        // Delta is on docs, the row under the folded band: one row up is the fold.
        view.InvokeAsync(() => view.Instance.DragBegin("delta", "move"));
        view.InvokeAsync(() => view.Instance.DragPreview(0, -1));
        view.InvokeAsync(() => view.Instance.DragCommit());

        Assert.NotNull(changed);
        Assert.Equal("backlog-lane", changed!.RowId);
    }

    [Fact]
    public void A_folded_bar_moved_in_time_keeps_its_own_lane()
    {
        using var context = new BunitContext();
        RoadmapChange? changed = null;

        var view = Chart(context, extra: parameters =>
            parameters.Add(timeline => timeline.OnBarChanged, (RoadmapChange change) => changed = change));
        Fold(view, "backlog");

        view.InvokeAsync(() => view.Instance.DragBegin("gamma", "move"));
        view.InvokeAsync(() => view.Instance.DragPreview(1, 0));
        view.InvokeAsync(() => view.Instance.DragCommit());

        Assert.NotNull(changed);
        Assert.Equal("platform", changed!.RowId);
    }

    [Fact]
    public void A_place_picked_on_a_folded_band_names_its_first_lane()
    {
        using var context = new BunitContext();
        RoadmapSlot? slot = null;

        var view = Chart(context, extra: parameters =>
            parameters.Add(timeline => timeline.OnSlotActivated, (RoadmapSlot picked) => slot = picked));
        Fold(view, "backlog");

        var folded = view.Find("[data-testid='rm-row-backlog---collapsed-']").GetAttribute("data-roadmap-slot")!;
        view.InvokeAsync(() => view.Instance.SlotActivated(folded, 2));

        Assert.NotNull(slot);
        Assert.Equal("backlog-lane", slot!.RowId);
    }
}
