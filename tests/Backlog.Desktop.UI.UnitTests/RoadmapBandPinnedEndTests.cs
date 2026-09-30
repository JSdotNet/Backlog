using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Roadmap;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Started work in the band: its bar is locked but offers its end, dragging that end
/// pins it instead of rescheduling, and "Use forecast" in the editor hands it back.
/// The rollup is a stand-in saying the item's work is in flight, because what is pinned
/// here is the band's side: which call a change becomes, and when the offer shows.
/// </summary>
public sealed class RoadmapBandPinnedEndTests : RoadmapBandHarness
{
    private static readonly DateOnly Today = new(2026, 1, 12);

    private sealed class InFlight : IRoadmapItemRollup
    {
        private static readonly RoadmapItemRollupDto Work = new(
            [
                new RoadmapGatheredLink("a", "Done", 2, RollupOrigin.Tag, RoadmapProgress.Done),
                new RoadmapGatheredLink("b", "Open", 3, RollupOrigin.Tag, RoadmapProgress.InProgress)
            ],
            []);

        public Task<RoadmapItemRollupDto> GatherAsync(RoadmapItemDto item, CancellationToken cancellationToken = default) =>
            Task.FromResult(Work);

        public Task<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>> GatherPlanAsync(
            RoadmapPlanDto plan,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>>(
                plan.Items.ToDictionary(item => item.Id, _ => Work));
    }

    private async Task<Guid> StartedItemAsync()
    {
        var added = await Planning.AddItemAsync(
            "Move devbook",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 16),
            repositoryAliases: ["backlog"],
            cancellationToken: TestContext.Current.CancellationToken);
        return added.Value.Id;
    }

    private IRenderedComponent<RoadmapBand> Band(BunitContext context)
    {
        context.Services.AddSingleton<IRoadmapItemRollup>(new InFlight());

        var band = context.Render<RoadmapBand>(parameters => parameters.Add(component => component.Today, Today));
        band.WaitForElement("[data-testid=\"roadmap-timeline\"]");
        return band;
    }

    [Fact]
    public async Task AStartedItemsBar_IsLockedButOffersItsEnd()
    {
        await StartedItemAsync();

        using var context = Context();
        var band = Band(context);

        var bar = band.FindComponent<RoadmapTimeline>().Instance.Bars.Single();
        Assert.True(bar.Locked);
        Assert.True(bar.EndResizable);
    }

    [Fact]
    public async Task DraggingTheEndOfAStartedBar_PinsTheEnd_AndLeavesTheStartAlone()
    {
        var itemId = await StartedItemAsync();

        using var context = Context();
        var band = Band(context);
        var timeline = band.FindComponent<RoadmapTimeline>();
        var bar = timeline.Instance.Bars.Single();

        await band.InvokeAsync(() => timeline.Instance.OnBarChanged.InvokeAsync(
            new RoadmapChange(bar.Id, bar.RowId, bar.Start, new DateOnly(2026, 6, 28), RoadmapDrag.ResizeEnd)));

        var stored = Assert.Single((await Planning.GetPlanAsync(TestContext.Current.CancellationToken)).Items, item => item.Id == itemId);
        Assert.True(stored.EndPinned);
        Assert.Equal(new DateOnly(2026, 6, 28), stored.End);
        Assert.Equal(new DateOnly(2026, 1, 5), stored.Start);

        // Drawn to the pinned end, and the bar still offers it.
        band.WaitForAssertion(() =>
        {
            var redrawn = timeline.Instance.Bars.Single();
            Assert.Equal(new DateOnly(2026, 6, 28), redrawn.End);
            Assert.Contains("Forecast:", redrawn.Detail, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task UseForecast_IsOnlyOfferedForAPinnedEnd_AndHandsTheEndBack()
    {
        var itemId = await StartedItemAsync();

        using var context = Context();
        var band = Band(context);
        var timeline = band.FindComponent<RoadmapTimeline>();

        await band.InvokeAsync(() => timeline.Instance.OnBarSelected.InvokeAsync(itemId.ToString()));
        band.WaitForElement("[data-testid=\"roadmap-editor\"]");
        Assert.Empty(band.FindAll("[data-testid=\"roadmap-editor-use-forecast\"]"));

        // Close, pin through the plan, open again.
        await band.InvokeAsync(() => band.FindComponent<RoadmapItemEditor>().Instance.OpenChanged.InvokeAsync(false));
        await Planning.PinItemEndAsync(itemId, new DateOnly(2026, 6, 28), TestContext.Current.CancellationToken);

        await band.InvokeAsync(() => timeline.Instance.OnBarSelected.InvokeAsync(itemId.ToString()));
        var button = band.WaitForElement("[data-testid=\"roadmap-editor-use-forecast\"]");
        button.Click();

        band.WaitForAssertion(() => Assert.Empty(band.FindAll("[data-testid=\"roadmap-editor\"]")));

        var stored = Assert.Single((await Planning.GetPlanAsync(TestContext.Current.CancellationToken)).Items);
        Assert.False(stored.EndPinned);
        Assert.DoesNotContain("Forecast:", timeline.Instance.Bars.Single().Detail, StringComparison.Ordinal);
    }
}
