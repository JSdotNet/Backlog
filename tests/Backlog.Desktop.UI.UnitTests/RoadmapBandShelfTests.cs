using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.UI;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The shelf of imported plans no item carries the tag of, and the one action on it.
/// The imported plans are a fixed stand-in, because what the backlog adapter reads
/// is pinned beside the adapter; the plan behind the band is the real one, so
/// planning from the shelf is proved by what the store holds afterwards.
/// </summary>
public sealed class RoadmapBandShelfTests : RoadmapBandHarness
{
    private sealed class FixedImportedPlans(params ImportedPlanDto[] plans) : IImportedPlanSource
    {
        /// <summary>How many times the band asked. It asks last thing in a load, so a
        /// test that waits for this has waited for the load: the only honest way to
        /// assert that something is <em>not</em> on screen.</summary>
        public int Reads { get; private set; }

        public Task<IReadOnlyList<ImportedPlanDto>> ListAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<ImportedPlanDto>>(plans);
        }
    }

    private FixedImportedPlans _imported = new();

    private static readonly ImportedPlanDto Shelf = new("shelf", ["backlog"], 3, 8, 1);

    private static readonly ImportedPlanDto Release = new("release-q4", [], 2, 5, 0);

    private BunitContext ContextWith(params ImportedPlanDto[] imported)
    {
        var context = Context();
        _imported = new FixedImportedPlans(imported);
        context.Services.AddSingleton<IImportedPlanSource>(_imported);
        return context;
    }

    private IRenderedComponent<RoadmapBand> Loaded(BunitContext context)
    {
        var band = context.Render<RoadmapBand>();
        band.WaitForAssertion(() => Assert.True(_imported.Reads > 0));
        return band;
    }

    [Fact]
    public async Task The_shelf_lists_every_imported_plan_no_item_carries_the_tag_of()
    {
        await Planning.AddItemAsync(
            "Release",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 9),
            repositoryAliases: ["backlog"],
            tag: "release-q4");

        using var context = ContextWith(Shelf, Release);
        var band = Loaded(context);

        var shelf = band.WaitForElement("[data-testid=\"roadmap-shelf\"]");
        var row = Assert.Single(shelf.QuerySelectorAll(".roadmap-shelf__row"));

        Assert.Equal("roadmap-shelf-row-shelf", row.GetAttribute("data-testid"));
        Assert.Contains("+shelf", row.TextContent);
        Assert.Equal("3 tasks · 8 points · 1 unestimated", row.QuerySelector(".roadmap-shelf__summary")!.TextContent);
        Assert.Equal("backlog", row.QuerySelector(".roadmap-shelf__repository")!.TextContent);
    }

    [Fact]
    public void A_plan_across_repositories_lists_each_repositorys_share()
    {
        var spanning = new ImportedPlanDto(
            "conventions", ["budgetbeheer", "spec-manager"], 23, 125, 0,
            [new("budgetbeheer", 14, 78, 0), new("spec-manager", 9, 47, 0)]);

        using var context = ContextWith(spanning);
        var band = Loaded(context);

        var row = band.WaitForElement("[data-testid=\"roadmap-shelf-row-conventions\"]");

        Assert.Equal("23 tasks · 125 points", row.QuerySelector(".roadmap-shelf__summary")!.TextContent);
        Assert.Equal(
            ["budgetbeheer", "spec-manager"],
            row.QuerySelectorAll(".roadmap-shelf__repository").Select(part => part.TextContent));
        Assert.Equal(
            ["14 tasks · 78 points", "9 tasks · 47 points"],
            row.QuerySelectorAll(".roadmap-shelf__part-figures").Select(part => part.TextContent));
    }

    [Fact]
    public void The_shelf_lists_plans_even_when_nothing_is_planned_yet()
    {
        // Tasks first is exactly the case with no roadmap at all.
        using var context = ContextWith(Shelf, Release);
        var band = Loaded(context);

        band.WaitForAssertion(() => Assert.Equal(2, band.FindAll(".roadmap-shelf__row").Count));
        Assert.NotEmpty(band.FindAll("[data-testid=\"roadmap-band-empty-state\"]"));
    }

    [Fact]
    public async Task The_shelf_is_not_rendered_when_every_imported_plan_is_already_planned()
    {
        await Planning.AddItemAsync(
            "Shelf",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 9),
            tag: "shelf");

        using var context = ContextWith(Shelf);
        var band = Loaded(context);
        band.WaitForElement("[data-testid=\"roadmap-timeline\"]");

        Assert.Empty(band.FindAll("[data-testid=\"roadmap-shelf\"]"));
    }

    [Fact]
    public void The_shelf_is_not_rendered_when_the_backlog_holds_no_imported_plan()
    {
        using var context = ContextWith();
        var band = Loaded(context);

        Assert.Empty(band.FindAll("[data-testid=\"roadmap-shelf\"]"));
    }

    [Fact]
    public void The_action_is_a_labelled_button_in_a_named_region()
    {
        using var context = ContextWith(Shelf);
        var band = Loaded(context);

        var shelf = band.WaitForElement("[data-testid=\"roadmap-shelf\"]");
        var heading = shelf.QuerySelector("h3")!;
        Assert.Equal(heading.Id, shelf.GetAttribute("aria-labelledby"));
        Assert.Equal("Unplanned work", heading.TextContent);

        var action = band.Find("[data-testid=\"roadmap-shelf-plan-shelf\"]");
        Assert.Equal("BUTTON", action.TagName);
        Assert.Equal("button", action.GetAttribute("type"));
        Assert.Equal("Plan it: +shelf", action.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task Plan_it_imports_one_item_placed_from_the_effort_its_tasks_registered()
    {
        using var context = ContextWith(Shelf, Release);
        var band = Loaded(context);

        band.WaitForElement("[data-testid=\"roadmap-shelf-plan-shelf\"]").Click();

        // The item is on the chart, the row it came from is gone, and the reader hears it.
        band.WaitForElement("[data-testid=\"roadmap-timeline\"]");
        band.WaitForAssertion(() =>
            Assert.Equal("Planned +shelf on the roadmap.", band.Find("[data-testid=\"roadmap-shelf-news\"]").TextContent));
        Assert.Equal("status", band.Find("[data-testid=\"roadmap-shelf-news\"]").GetAttribute("role"));
        Assert.Single(band.FindAll(".roadmap-shelf__row"));
        Assert.Empty(band.FindAll("[data-testid=\"roadmap-shelf-row-shelf\"]"));

        var item = Assert.Single((await Planning.GetPlanAsync()).Items);
        Assert.Equal("shelf", item.Tag);
        Assert.Equal("Shelf", item.Title);
        Assert.Equal(new[] { "backlog" }, item.RepositoryAliases);

        // Eight points at the test host's point a day: the window an import draws.
        Assert.Equal(ImportPlacement.Effort, item.PlacedByImport);
        Assert.Equal(8, item.Days);
    }

    [Fact]
    public void Planning_the_last_row_takes_the_shelf_away_and_keeps_the_announcement()
    {
        using var context = ContextWith(Shelf);
        var band = Loaded(context);

        band.WaitForElement("[data-testid=\"roadmap-shelf-plan-shelf\"]").Click();

        band.WaitForAssertion(() => Assert.Empty(band.FindAll("[data-testid=\"roadmap-shelf\"]")));
        Assert.Equal("Planned +shelf on the roadmap.", band.Find("[data-testid=\"roadmap-shelf-news\"]").TextContent);
    }

    [Theory]
    [InlineData("release-q4", "Release q4")]
    [InlineData("shelf", "Shelf")]
    public void An_items_title_is_read_off_its_tag(string tag, string title) =>
        Assert.Equal(title, RoadmapBand.TitleOf(tag));
}
