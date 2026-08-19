using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Roadmap;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Adding and editing dates, and choosing the colour a repository's band takes. Both
/// are stored with the plan, so both are asserted against the plan on disk rather than
/// against what the dialog happens to be showing.
/// </summary>
public class RoadmapBandDatesAndColoursTests : RoadmapBandHarness
{
    private static RoadmapMilestoneEditor DateEditor(IRenderedComponent<RoadmapBand> band) =>
        band.FindComponent<RoadmapMilestoneEditor>().Instance;

    private static async Task Activate(IRenderedComponent<RoadmapBand> band, Guid nodeId)
    {
        var timeline = band.FindComponent<RoadmapTimeline>();
        await band.InvokeAsync(() => timeline.Instance.OnBarSelected.InvokeAsync(nodeId.ToString()));
    }

    // --- Dates ----------------------------------------------------------------

    [Fact]
    public void AddADateOpensAnEmptyDateEditor()
    {
        using var context = Context();
        var band = context.Render<RoadmapBand>();

        Open(band, "roadmap-band-add-date", "roadmap-milestone-editor");

        Assert.NotNull(band.Find("[data-testid=\"roadmap-milestone-editor\"]"));
        Assert.Null(DateEditor(band).Milestone);

        // And not the item editor, which asks for a window and a priority a date has no
        // use for.
        Assert.Empty(band.FindAll("[data-testid=\"roadmap-editor\"]"));
    }

    [Fact]
    public async Task AddingADateStoresIt()
    {
        Configure("JSdotNet/Backlog");

        using var context = Context();
        var band = context.Render<RoadmapBand>();
        Open(band, "roadmap-band-add-date", "roadmap-milestone-editor");

        await band.InvokeAsync(() => DateEditor(band).OnSave.InvokeAsync(new RoadmapMilestoneSubmission(
            null,
            "Feature freeze",
            new DateOnly(2026, 10, 16),
            MilestoneKind.Freeze,
            ["backlog"],
            IsPlanWide: true)));

        var milestone = Assert.Single((await Planning.GetPlanAsync()).Milestones);

        Assert.Equal("Feature freeze", milestone.Title);
        Assert.Equal(new DateOnly(2026, 10, 16), milestone.On);
        Assert.Equal(MilestoneKind.Freeze, milestone.Kind);
        Assert.Equal(["backlog"], milestone.RepositoryAliases);
        Assert.True(milestone.IsPlanWide);

        Assert.Empty(band.FindAll("[data-testid=\"roadmap-milestone-editor\"]"));
        Assert.Contains("Feature freeze", band.Markup);
    }

    [Fact]
    public async Task OpeningADateEditsThatDate_NotAnItem()
    {
        await Planning.AddItemAsync("Work", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));
        var release = await Planning.AddMilestoneAsync("1.0", new DateOnly(2026, 3, 31));

        using var context = Context();
        var band = Drawn(context);
        await Activate(band, release.Value.Id);

        Assert.Equal(release.Value.Id, DateEditor(band).Milestone?.Id);
        Assert.Empty(band.FindAll("[data-testid=\"roadmap-editor\"]"));
        Assert.Equal(
            "1.0",
            band.WaitForElement("[data-testid=\"roadmap-milestone-editor-title\"] input").GetAttribute("value"));
    }

    [Fact]
    public async Task EditingADateStoresIt_AndKeepsTheSameDate()
    {
        var release = await Planning.AddMilestoneAsync("1.0", new DateOnly(2026, 3, 31));

        using var context = Context();
        var band = Drawn(context);
        await Activate(band, release.Value.Id);

        await band.InvokeAsync(() => DateEditor(band).OnSave.InvokeAsync(new RoadmapMilestoneSubmission(
            release.Value.Id,
            "1.1",
            new DateOnly(2026, 4, 30),
            MilestoneKind.Commitment,
            [],
            IsPlanWide: false)));

        var milestone = Assert.Single((await Planning.GetPlanAsync()).Milestones);

        Assert.Equal(release.Value.Id, milestone.Id);
        Assert.Equal("1.1", milestone.Title);
        Assert.Equal(new DateOnly(2026, 4, 30), milestone.On);
        Assert.Equal(MilestoneKind.Commitment, milestone.Kind);
    }

    [Fact]
    public async Task ARefusedDateKeepsTheEditorOpenAndSaysWhy()
    {
        using var context = Context();
        var band = context.Render<RoadmapBand>();
        Open(band, "roadmap-band-add-date", "roadmap-milestone-editor");

        await band.InvokeAsync(() => DateEditor(band).OnSave.InvokeAsync(new RoadmapMilestoneSubmission(
            null, "   ", new DateOnly(2026, 3, 31), MilestoneKind.Release, [], IsPlanWide: false)));

        Assert.NotNull(band.Find("[data-testid=\"roadmap-milestone-editor\"]"));
        Assert.NotNull(band.Find("[data-testid=\"roadmap-milestone-editor-error\"]"));
        Assert.Empty((await Planning.GetPlanAsync()).Milestones);
    }

    [Fact]
    public async Task DeletingADateRemovesIt()
    {
        var release = await Planning.AddMilestoneAsync("1.0", new DateOnly(2026, 3, 31));

        using var context = Context();
        var band = Drawn(context);
        await Activate(band, release.Value.Id);

        await band.InvokeAsync(() => DateEditor(band).OnDelete.InvokeAsync(release.Value.Id));

        Assert.Empty((await Planning.GetPlanAsync()).Milestones);
        Assert.Empty(band.FindAll("[data-testid=\"roadmap-milestone-editor\"]"));
    }

    [Fact]
    public async Task APlanWideDateDrawsARuleThroughEveryRow()
    {
        Configure("JSdotNet/Backlog");
        await Planning.AddItemAsync(
            "Work",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 9),
            repositoryAliases: ["backlog"]);
        var freeze = await Planning.AddMilestoneAsync(
            "Freeze",
            new DateOnly(2026, 2, 2),
            MilestoneKind.Freeze,
            isPlanWide: true);

        using var context = Context();
        var band = Drawn(context);

        Assert.NotNull(band.Find($"[data-testid=\"roadmap-timeline-milestone-rule-{freeze.Value.Id}\"]"));
    }

    [Fact]
    public async Task AnOrdinaryDateDrawsNoRule()
    {
        Configure("JSdotNet/Backlog");
        await Planning.AddItemAsync(
            "Work",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 9),
            repositoryAliases: ["backlog"]);
        await Planning.AddMilestoneAsync("1.0", new DateOnly(2026, 2, 2));

        using var context = Context();
        var band = Drawn(context);

        Assert.Empty(band.FindAll("[class*=\"roadmap-timeline__milestone-rule\"]"));
    }

    [Fact]
    public async Task ADateWaitsForThingsJustAsWorkDoes()
    {
        var work = await Planning.AddItemAsync("Work", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));
        var release = await Planning.AddMilestoneAsync("1.0", new DateOnly(2026, 3, 31));

        using var context = Context();
        var band = Drawn(context);
        await Activate(band, release.Value.Id);

        await band.InvokeAsync(() => DateEditor(band).OnDependencyChanged.InvokeAsync(
            new RoadmapDependencyChange(release.Value.Id, work.Value.Id, Added: true)));

        var plan = await Planning.GetPlanAsync();

        Assert.Equal([work.Value.Id], plan.Milestones.Single().DependsOn);
    }

    // --- Band colours ---------------------------------------------------------

    [Fact]
    public async Task ChoosingAColourStoresItAndRedrawsTheBand()
    {
        Configure("JSdotNet/Backlog");
        await Planning.AddItemAsync(
            "Work",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 9),
            repositoryAliases: ["backlog"]);

        using var context = Context();
        var band = Drawn(context);
        Open(band, "roadmap-band-colours", "roadmap-colours");

        band.Find("[data-testid=\"roadmap-colours-backlog-4\"]").Click();

        // Waited for on the chart, because that is the observable end of the round trip:
        // the choice goes to the module, through the plan file, and back into the view.
        // It is also the point of applying a choice immediately — the chart behind the
        // dialog is the only way to judge whether it was the right one.
        band.WaitForAssertion(() => Assert.Equal(
            "var(--color-band-4)",
            band.FindComponent<RoadmapTimeline>().Instance.Groups.Single(group => group.Id == "backlog").Color));

        var plan = await Planning.GetPlanAsync();
        Assert.Equal(4, plan.Bands["backlog"]);
    }

    [Fact]
    public async Task AColourChoiceCanBeGivenBack()
    {
        Configure("JSdotNet/Backlog");
        await Planning.AddItemAsync(
            "Work",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 9),
            repositoryAliases: ["backlog"]);
        Assert.True((await Planning.ColourBandAsync("backlog", 5)).IsSuccess);

        using var context = Context();
        var band = Drawn(context);
        Open(band, "roadmap-band-colours", "roadmap-colours");

        band.Find("[data-testid=\"roadmap-colours-backlog-auto\"]").Click();

        band.WaitForAssertion(() => Assert.Equal(
            "false",
            band.Find("[data-testid=\"roadmap-colours-backlog-5\"]").GetAttribute("aria-pressed")));

        Assert.Empty((await Planning.GetPlanAsync()).Bands);
    }

    [Fact]
    public void TheColourDialogOffersExactlyTheSanctionedFive()
    {
        Configure("JSdotNet/Backlog");

        using var context = Context();
        var band = context.Render<RoadmapBand>();
        Open(band, "roadmap-band-colours", "roadmap-colours");

        for (var colour = 1; colour <= RoadmapPlanView.BandHues; colour++)
        {
            Assert.NotNull(band.Find($"[data-testid=\"roadmap-colours-backlog-{colour}\"]"));
        }

        Assert.Empty(band.FindAll($"[data-testid=\"roadmap-colours-backlog-{RoadmapPlanView.BandHues + 1}\"]"));
    }

    [Fact]
    public void TheChosenColourIsSaidInStateNotOnlyInColour()
    {
        Configure("JSdotNet/Backlog");

        using var context = Context();
        var band = context.Render<RoadmapBand>();
        Open(band, "roadmap-band-colours", "roadmap-colours");
        band.Find("[data-testid=\"roadmap-colours-backlog-2\"]").Click();

        // Waited for, not assumed: the choice goes to the module and through the plan
        // file before the dialog is redrawn from what was stored.
        band.WaitForAssertion(() => Assert.Equal(
            "true",
            band.Find("[data-testid=\"roadmap-colours-backlog-2\"]").GetAttribute("aria-pressed")));

        Assert.Equal(
            "false",
            band.Find("[data-testid=\"roadmap-colours-backlog-3\"]").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void WithNoRepositoriesConfiguredTheDialogSaysSo()
    {
        using var context = Context();
        var band = context.Render<RoadmapBand>();

        Open(band, "roadmap-band-colours", "roadmap-colours");

        Assert.NotNull(band.Find("[data-testid=\"roadmap-colours-empty\"]"));
        Assert.Empty(band.FindAll("[data-testid=\"roadmap-colours-row\"]"));
    }
}
