using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Roadmap;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the band draws from a stored plan, and what a gesture on the chart does to
/// it. Editing through the dialog is <see cref="RoadmapBandEditingTests"/>; the
/// fixture both share is <see cref="RoadmapBandHarness"/>.
/// </summary>
public class RoadmapBandTests : RoadmapBandHarness
{
    [Fact]
    public void WithNothingPlanned_TheBandShowsItsEmptyStateRatherThanAnEmptyChart()
    {
        using var context = Context();

        var band = context.Render<RoadmapBand>();

        Assert.NotNull(band.Find("[data-testid=\"roadmap-band-empty-state\"]"));
        Assert.Empty(band.FindAll("[data-testid=\"roadmap-timeline\"]"));
    }

    [Fact]
    public async Task StoredWorkAppearsOnTheTimeline_UnderTheRepositoryItNames()
    {
        Configure("JSdotNet/Backlog");
        await Planning.AddItemAsync(
            "Extract the sync service",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 2, 13),
            PlanningPriority.High,
            ["backlog"],
            "platform");

        using var context = Context();
        var band = Drawn(context);

        Assert.Empty(band.FindAll("[data-testid=\"roadmap-band-empty-state\"]"));

        var markup = band.Markup;
        Assert.Contains("Extract the sync service", markup);
        Assert.Contains("platform", markup);

        // The band is labelled with the repository's alias, because that label is
        // written down the side of the band and its length is a floor on the band's
        // height. The full name is what the Repository filter offers, so it is still
        // there to be found — in the filter's options rather than on the band.
        var timeline = band.FindComponent<RoadmapTimeline>().Instance;

        Assert.Equal("backlog", timeline.Groups[0].Title);
        Assert.Contains(
            new RoadmapFacet("Repository", "JSdotNet/Backlog"),
            timeline.Bars.Single().FacetList);
    }

    [Fact]
    public async Task TheHeadingAndTheFiltersShareOneRow_AndThereIsNoSecondTitle()
    {
        Configure("JSdotNet/Backlog");
        await Planning.AddItemAsync(
            "Work",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 2, 13),
            repositoryAliases: ["backlog"]);

        using var context = Context();
        var band = Drawn(context);

        // Exactly one heading, and it is inside the chart's header row rather than
        // above the chart — which is what puts it on the same line as the filters.
        var heading = Assert.Single(band.FindAll("h2#roadmap-band-title"));
        Assert.Equal("Roadmap", heading.TextContent.Trim());

        var header = band.Find(".roadmap-timeline__header");
        Assert.Contains(heading, header.QuerySelectorAll("h2"));
        Assert.NotNull(header.QuerySelector(".roadmap-timeline__filters"));

        // No chart title of its own, and no year: the axis carries the years.
        Assert.Empty(band.FindAll(".roadmap-timeline__title"));
        Assert.Empty(band.FindAll(".roadmap-band__header"));
    }

    [Fact]
    public async Task TheFiltersCarryNoLabelHeadings_ButAreStillNamed()
    {
        Configure("JSdotNet/Backlog");
        await Planning.AddItemAsync(
            "Work",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 2, 13),
            repositoryAliases: ["backlog"]);

        using var context = Context();
        var band = Drawn(context);

        Assert.Empty(band.FindAll(".roadmap-timeline__filters .field__label"));
        Assert.Equal(
            ["Repository", "Priority", "Lane"],
            band.FindAll(".roadmap-timeline__filters input").Select(input => input.GetAttribute("aria-label")));
    }

    [Fact]
    public void WithNothingPlanned_TheBandKeepsItsOwnHeaderAboveTheEmptyState()
    {
        using var context = Context();

        var band = context.Render<RoadmapBand>();

        // No chart to head, so the heading has nowhere else to live.
        Assert.NotNull(band.Find(".roadmap-band__header"));
        Assert.Single(band.FindAll("h2#roadmap-band-title"));
    }

    [Fact]
    public async Task AMilestoneIsDrawnOnItsOwnRow()
    {
        Configure("JSdotNet/Backlog");
        await Planning.AddMilestoneAsync("1.0", new DateOnly(2026, 3, 31), MilestoneKind.Release, ["backlog"]);

        using var context = Context();
        var band = Drawn(context);

        Assert.Contains("Milestones", band.Markup);
        Assert.Contains("1.0", band.Markup);
    }

    [Fact]
    public async Task RescheduleFromTheTimeline_IsStored()
    {
        var added = await Planning.AddItemAsync("Work", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));
        var itemId = added.Value.Id;

        using var context = Context();
        var band = Drawn(context);
        var timeline = band.FindComponent<RoadmapTimeline>();
        var bar = timeline.Instance.Bars.Single();

        await band.InvokeAsync(() => timeline.Instance.OnBarChanged.InvokeAsync(
            new RoadmapChange(bar.Id, bar.RowId, new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 6), RoadmapDrag.Move)));

        var plan = await Planning.GetPlanAsync();
        var stored = plan.Items.Single(item => item.Id == itemId);

        Assert.Equal(new DateOnly(2026, 2, 2), stored.Start);
        Assert.Equal(new DateOnly(2026, 2, 6), stored.End);
    }

    [Fact]
    public async Task DroppingSomethingOnAnotherLane_RefilesIt()
    {
        Configure("JSdotNet/Backlog");
        var added = await Planning.AddItemAsync(
            "Work",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 9),
            repositoryAliases: ["backlog"],
            lane: "platform");
        await Planning.AddItemAsync(
            "Other work",
            new DateOnly(2026, 1, 12),
            new DateOnly(2026, 1, 16),
            repositoryAliases: ["backlog"],
            lane: "migration");

        using var context = Context();
        var band = Drawn(context);
        var timeline = band.FindComponent<RoadmapTimeline>();
        var bar = timeline.Instance.Bars.Single(candidate => candidate.Id == added.Value.Id.ToString());
        var migrationRow = timeline.Instance.Groups
            .SelectMany(group => group.RowList)
            .Single(row => row.Title == "migration");

        await band.InvokeAsync(() => timeline.Instance.OnBarChanged.InvokeAsync(
            new RoadmapChange(bar.Id, migrationRow.Id, bar.Start, bar.End, RoadmapDrag.Move)));

        var plan = await Planning.GetPlanAsync();
        Assert.Equal("migration", plan.Items.Single(item => item.Id == added.Value.Id).Lane);
    }

    [Fact]
    public async Task ARefusedRescheduleIsExplained_AndTheChartGoesBackToWhatWasStored()
    {
        var added = await Planning.AddItemAsync("Work", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));

        using var context = Context();
        var band = Drawn(context);
        var timeline = band.FindComponent<RoadmapTimeline>();
        var bar = timeline.Instance.Bars.Single();

        // A window that ends before it starts. The chart cannot produce one by
        // dragging, but the band must not assume that: the plan is what decides.
        await band.InvokeAsync(() => timeline.Instance.OnBarChanged.InvokeAsync(
            new RoadmapChange(bar.Id, bar.RowId, new DateOnly(2026, 2, 6), new DateOnly(2026, 2, 2), RoadmapDrag.Move)));

        Assert.NotNull(band.Find("[data-testid=\"roadmap-band-error\"]"));

        var plan = await Planning.GetPlanAsync();
        var stored = plan.Items.Single(item => item.Id == added.Value.Id);
        Assert.Equal(new DateOnly(2026, 1, 5), stored.Start);
        Assert.Equal(new DateOnly(2026, 1, 9), stored.End);
    }

    [Fact]
    public async Task WorkNamingARepositoryNobodyConfigured_StillShows()
    {
        await Planning.AddItemAsync(
            "Old work",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 9),
            repositoryAliases: ["retired"]);

        using var context = Context();
        var band = Drawn(context);

        Assert.Contains("Old work", band.Markup);
        Assert.Contains(RoadmapPlanView.UnfiledGroupTitle, band.Markup);
    }

    [Fact]
    public async Task ADependencyIsDrawnAsAnArrow()
    {
        var design = await Planning.AddItemAsync("Design", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));
        var build = await Planning.AddItemAsync("Build", new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 16));
        Assert.True((await Planning.AddDependencyAsync(build.Value.Id, design.Value.Id)).IsSuccess);

        using var context = Context();
        var band = Drawn(context);
        var timeline = band.FindComponent<RoadmapTimeline>();

        var link = Assert.Single(timeline.Instance.Links);
        Assert.Equal(design.Value.Id.ToString(), link.FromId);
        Assert.Equal(build.Value.Id.ToString(), link.ToId);
    }

}
