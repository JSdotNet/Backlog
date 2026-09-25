using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Roadmap;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A dependency drawn on the chart — the handle past one bar's end dropped on another —
/// against the real stored plan. The timeline's callback is raised directly: the
/// pointer gesture is the library's, and what is asserted here is what the band does
/// with the link it proposes.
/// </summary>
public class RoadmapBandLinkingTests : RoadmapBandHarness
{
    private async Task<(Guid First, Guid Then)> TwoItemsAsync()
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Fincent");

        var first = await Planning.AddItemAsync(
            "First", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), repositoryAliases: ["backlog"]);
        var then = await Planning.AddItemAsync(
            "Then", new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 16), repositoryAliases: ["fincent"]);

        return (first.Value.Id, then.Value.Id);
    }

    private static Task Link(IRenderedComponent<RoadmapBand> band, string fromId, string toId)
    {
        var timeline = band.FindComponent<RoadmapTimeline>();
        return band.InvokeAsync(() => timeline.Instance.OnLinkCreated.InvokeAsync(new RoadmapLink(fromId, toId)));
    }

    [Fact]
    public async Task DroppingTheHandleOnAnotherBar_MakesThatOneWaitForIt_AcrossRepositories()
    {
        var (first, then) = await TwoItemsAsync();

        using var context = Context();
        var band = Drawn(context);

        await Link(band, first.ToString(), then.ToString());

        var plan = await Planning.GetPlanAsync();
        Assert.Equal([first], plan.Items.Single(item => item.Id == then).DependsOn);

        // Drawn from the reloaded plan, from one band into the other.
        band.WaitForAssertion(() =>
            Assert.Contains(new RoadmapLink(first.ToString(), then.ToString()), band.FindComponent<RoadmapTimeline>().Instance.Links));
    }

    [Fact]
    public async Task ALinkThatWouldCloseACycle_IsRefusedAndSaidSo()
    {
        var (first, then) = await TwoItemsAsync();
        Assert.True((await Planning.AddDependencyAsync(then, first)).IsSuccess);

        using var context = Context();
        var band = Drawn(context);

        await Link(band, then.ToString(), first.ToString());

        var plan = await Planning.GetPlanAsync();
        Assert.Empty(plan.Items.Single(item => item.Id == first).DependsOn);
        band.WaitForAssertion(() => Assert.NotEmpty(band.Find("[data-testid=\"roadmap-band-error\"]").TextContent.Trim()));
    }

    [Fact]
    public async Task LinkingTwoPartsOfOneItem_ChangesNothingAndSaysWhy()
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Fincent");
        var item = (await Planning.AddItemAsync(
            "Both", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), repositoryAliases: ["backlog", "fincent"])).Value;

        using var context = Context();
        var band = Drawn(context);

        await Link(band, $"{item.Id}@backlog", $"{item.Id}@fincent");

        Assert.Empty((await Planning.GetPlanAsync()).Items.Single().DependsOn);
        band.WaitForAssertion(() => Assert.Contains(
            "part of itself", band.Find("[data-testid=\"roadmap-band-error\"]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MovingABar_MovesTheItemByAsMuchAsTheBarMoved()
    {
        var (first, _) = await TwoItemsAsync();

        using var context = Context();
        var band = Drawn(context);
        var timeline = band.FindComponent<RoadmapTimeline>();
        var bar = timeline.Instance.Bars.Single(candidate => candidate.Id == first.ToString());

        await band.InvokeAsync(() => timeline.Instance.OnBarChanged.InvokeAsync(
            new RoadmapChange(bar.Id, bar.RowId, bar.Start.AddDays(7), bar.End.AddDays(7), RoadmapDrag.Move)));

        var moved = (await Planning.GetPlanAsync()).Items.Single(item => item.Id == first);
        Assert.Equal((new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 16)), (moved.Start, moved.End));
    }
}
