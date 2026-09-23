using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.UI;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The band asks for every item's gathered work once, as the plan loads, and the
/// bars carry it: the steps an item expands into and the fill it wears collapsed.
/// The rollup is a counting stand-in here, because what is pinned is the band's
/// side of the port — how often it asks and what it does with the answer. What
/// the real adapter gathers is pinned beside the adapter.
/// </summary>
public sealed class RoadmapBandStepsTests : RoadmapBandHarness
{
    private sealed class CountingRollup(RoadmapItemRollupDto answer) : IRoadmapItemRollup
    {
        public int PlanReads { get; private set; }

        public int ItemReads { get; private set; }

        public Task<RoadmapItemRollupDto> GatherAsync(RoadmapItemDto item, CancellationToken cancellationToken = default)
        {
            ItemReads++;
            return Task.FromResult(answer);
        }

        public Task<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>> GatherPlanAsync(
            RoadmapPlanDto plan,
            CancellationToken cancellationToken = default)
        {
            PlanReads++;
            return Task.FromResult<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>>(
                plan.Items.ToDictionary(item => item.Id, _ => answer));
        }
    }

    private static readonly RoadmapItemRollupDto Gathered = new(
        [
            new RoadmapGatheredLink("b", "Second", 3, RollupOrigin.Tag, RoadmapProgress.InProgress, ["a"]),
            new RoadmapGatheredLink("a", "First", 1, RollupOrigin.Tag, RoadmapProgress.Done),
            new RoadmapGatheredLink("c", "Unsized", null, RollupOrigin.Tag, RoadmapProgress.Ready)
        ],
        []);

    [Fact]
    public async Task The_band_gathers_the_whole_plan_once_on_load_and_no_item_on_its_own()
    {
        using var context = Context();
        var rollup = new CountingRollup(Gathered);
        context.Services.AddSingleton<IRoadmapItemRollup>(rollup);

        await Planning.AddItemAsync("One", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 30), repositoryAliases: ["backlog"]);
        await Planning.AddItemAsync("Two", new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 27), repositoryAliases: ["backlog"]);

        var band = Drawn(context);

        band.WaitForAssertion(() => Assert.Equal(2, band.FindAll(".roadmap-bar__toggle").Count));
        Assert.Equal(1, rollup.PlanReads);
        Assert.Equal(0, rollup.ItemReads);
    }

    [Fact]
    public async Task An_item_draws_its_gathered_tasks_as_ordered_steps_with_its_progress()
    {
        using var context = Context();
        context.Services.AddSingleton<IRoadmapItemRollup>(new CountingRollup(Gathered));

        await Planning.AddItemAsync("Plan", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 30), repositoryAliases: ["backlog"]);

        var band = Drawn(context);

        band.WaitForAssertion(() => Assert.Single(band.FindAll(".roadmap-bar__toggle")));

        // 1 of 4 estimated points done, one unsized.
        Assert.Contains("width: 25%", band.Find(".roadmap-bar__fill").GetAttribute("style"));
        Assert.Equal("1 unestimated", band.Find(".roadmap-bar__unestimated").TextContent);

        band.Find(".roadmap-bar__toggle").Click();

        band.WaitForAssertion(() => Assert.Equal(
            ["First", "Second", "Unsized"],
            band.FindAll(".roadmap-step__title").Select(title => title.TextContent)));
    }
}
