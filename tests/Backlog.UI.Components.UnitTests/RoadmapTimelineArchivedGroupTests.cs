namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A group marked <see cref="RoadmapGroup.Archived"/>: drawn greyed — neutral, whatever
/// colour it was handed — and tagged Archived in words beside its name, so the mark is
/// never a shade alone, and every row in it says so to a screen reader.
/// </summary>
public sealed class RoadmapTimelineArchivedGroupTests
{
    private static readonly RoadmapWindow Q3 = new(new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30));

    private static readonly IReadOnlyList<RoadmapGroup> Plan =
    [
        new("backlog", "backlog", [new RoadmapRow("backlog-lane", string.Empty)], "var(--color-band-1)"),
        new("budget", "budget", [new RoadmapRow("budget-lane", string.Empty)], "var(--color-band-2)", Archived: true)
    ];

    private static readonly IReadOnlyList<RoadmapBar> Work =
    [
        new("ship", "backlog-lane", "Ship", new DateOnly(2026, 7, 6), new DateOnly(2026, 7, 31)),
        new("books", "budget-lane", "Close the books", new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 28), Locked: true)
    ];

    private static IRenderedComponent<RoadmapTimeline> Chart(BunitContext context)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<RoadmapTimeline>(parameters => parameters
            .Add(timeline => timeline.Groups, Plan)
            .Add(timeline => timeline.Bars, Work)
            .Add(timeline => timeline.Window, Q3)
            .Add(timeline => timeline.TestId, "rm"));
    }

    [Fact]
    public void An_archived_group_is_tagged_archived_beside_its_name()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        var archived = view.Find("[data-testid='rm-group-budget']");
        Assert.Contains("roadmap-timeline__group--archived", archived.ClassList);
        var tag = archived.QuerySelector(".roadmap-timeline__group-tag");
        Assert.Equal("Archived", tag?.TextContent.Trim());

        // The library's archived status badge, placed for the sidebar, not a second pill.
        Assert.Contains("badge--status-archived", tag!.ClassList);

        var active = view.Find("[data-testid='rm-group-backlog']");
        Assert.DoesNotContain("roadmap-timeline__group--archived", active.ClassList);
        Assert.Null(active.QuerySelector(".roadmap-timeline__group-tag"));
    }

    [Fact]
    public void An_archived_group_is_drawn_neutral_whatever_colour_it_was_handed()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        Assert.Null(view.Find("[data-testid='rm-group-budget']").GetAttribute("style"));
        Assert.DoesNotContain("--roadmap-group-color", view.Find("[data-testid='rm-row-budget-lane']").GetAttribute("style") ?? string.Empty);

        // The active band keeps its hue.
        Assert.Contains("var(--color-band-1)", view.Find("[data-testid='rm-group-backlog']").GetAttribute("style"));
    }

    [Fact]
    public void Every_row_of_an_archived_group_says_it_is_archived()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        Assert.Equal("budget (archived)", view.Find("[data-testid='rm-row-budget-lane']").GetAttribute("aria-label"));
        Assert.Equal("backlog", view.Find("[data-testid='rm-row-backlog-lane']").GetAttribute("aria-label"));
    }
}
