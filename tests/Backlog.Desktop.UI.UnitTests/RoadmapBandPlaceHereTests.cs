using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Roadmap;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Adding in place: a double-click on an empty stretch of the chart opens the same
/// dialog the heading does, filled in with where it was — and work nothing carries out
/// yet is drawn apart from work somebody has picked up. The timeline's slot callback is
/// raised directly; the double-click that raises it is the script's.
/// </summary>
public class RoadmapBandPlaceHereTests : RoadmapBandHarness
{
    private static readonly DateOnly Week = new(2026, 2, 2);

    private static async Task PlaceAsync(IRenderedComponent<RoadmapBand> band, RoadmapSlot slot)
    {
        var timeline = band.FindComponent<RoadmapTimeline>();
        await band.InvokeAsync(() => timeline.Instance.OnSlotActivated.InvokeAsync(slot));
    }

    private static string? Value(IRenderedComponent<RoadmapBand> band, string testId) =>
        band.Find($"[data-testid=\"{testId}\"] input").GetAttribute("value");

    [Fact]
    public async Task ADoubleClickOnALaneOpensTheEditorOnThatWeekBandAndLane()
    {
        Configure("JSdotNet/Backlog");

        using var context = Context();
        var band = await PlannedAsync(context);

        await PlaceAsync(band, new RoadmapSlot("backlog::Planned", Week, RoadmapRowKind.Bars));
        band.WaitForElement("[data-testid=\"roadmap-editor\"]");

        var editor = band.FindComponent<RoadmapItemEditor>().Instance;
        Assert.Null(editor.Item);
        Assert.Equal(new RoadmapItemPlacement(Week, "backlog", "Planned"), editor.Placement);
        Assert.Equal("2026-02-02", Value(band, "roadmap-editor-start"));
        Assert.Equal("2026-02-15", Value(band, "roadmap-editor-end"));
        Assert.Equal("Planned", Value(band, "roadmap-editor-lane"));
    }

    [Fact]
    public async Task ADragAcrossALaneOpensTheEditorOnTheSpanItCovered()
    {
        Configure("JSdotNet/Backlog");

        using var context = Context();
        var band = await PlannedAsync(context);

        await PlaceAsync(band, new RoadmapSlot("backlog::Planned", Week, RoadmapRowKind.Bars, Week.AddDays(27)));
        band.WaitForElement("[data-testid=\"roadmap-editor\"]");

        Assert.Equal(new RoadmapItemPlacement(Week, "backlog", "Planned", Week.AddDays(27)), band.FindComponent<RoadmapItemEditor>().Instance.Placement);
        Assert.Equal("2026-02-02", Value(band, "roadmap-editor-start"));
        Assert.Equal("2026-03-01", Value(band, "roadmap-editor-end"));
    }

    [Fact]
    public async Task SavingWhatWasPlacedFilesItWhereItWasPut()
    {
        Configure("JSdotNet/Backlog");

        using var context = Context();
        var band = await PlannedAsync(context);

        await PlaceAsync(band, new RoadmapSlot("backlog::Planned", Week, RoadmapRowKind.Bars));
        band.WaitForElement("[data-testid=\"roadmap-editor\"]");

        band.Find("[data-testid=\"roadmap-editor-title\"] input").Input("Put here");
        band.Find("[data-testid=\"roadmap-editor-save\"]").Click();

        band.WaitForAssertion(() => Assert.Empty(band.FindAll("[data-testid=\"roadmap-editor\"]")));

        var placed = Assert.Single(
            (await Planning.GetPlanAsync(TestContext.Current.CancellationToken)).Items,
            item => item.Title == "Put here");
        Assert.Equal(Week, placed.Start);
        Assert.Equal(["backlog"], placed.RepositoryAliases);
    }

    [Fact]
    public async Task TheUnfiledBandFilesNewWorkAgainstNoRepository()
    {
        using var context = Context();
        var band = await PlannedAsync(context);

        await PlaceAsync(band, new RoadmapSlot("unfiled::Planned", Week, RoadmapRowKind.Bars));
        band.WaitForElement("[data-testid=\"roadmap-editor\"]");

        Assert.Null(band.FindComponent<RoadmapItemEditor>().Instance.Placement!.Repository);
    }

    [Fact]
    public async Task ADoubleClickOnTheDatesRowOpensTheDateEditorOnThatWeek()
    {
        Configure("JSdotNet/Backlog");

        using var context = Context();
        var band = await PlannedAsync(context);

        await PlaceAsync(band, new RoadmapSlot("milestones::all", Week, RoadmapRowKind.Milestones));
        band.WaitForElement("[data-testid=\"roadmap-milestone-editor\"]");

        Assert.Equal("2026-02-02", Value(band, "roadmap-milestone-editor-on"));
        Assert.Empty(band.FindAll("[data-testid=\"roadmap-editor\"]"));
    }

    [Fact]
    public async Task PlanWorkAfterAPlacedAddStartsFromTheDefaultsAgain()
    {
        Configure("JSdotNet/Backlog");

        using var context = Context();
        var band = await PlannedAsync(context);

        await PlaceAsync(band, new RoadmapSlot("backlog::Planned", Week, RoadmapRowKind.Bars));
        band.WaitForElement("[data-testid=\"roadmap-editor\"]");
        await band.InvokeAsync(() => band.FindComponent<RoadmapItemEditor>().Instance.OpenChanged.InvokeAsync(false));

        Open(band, "roadmap-band-add", "roadmap-editor");

        Assert.Null(band.FindComponent<RoadmapItemEditor>().Instance.Placement);
    }

    [Fact]
    public async Task WorkNoTaskCarriesOutIsDrawnAsIntent()
    {
        Configure("JSdotNet/Backlog");

        using var context = Context();
        var band = await PlannedAsync(context);

        var bar = Assert.Single(band.FindComponent<RoadmapTimeline>().Instance.Bars);
        Assert.True(bar.Tentative);
        Assert.Contains("no task yet", bar.Detail);
        Assert.Contains("roadmap-bar--tentative", band.Find(".roadmap-bar").ClassName);
    }
}
