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
    //
    // Nothing here chooses a colour any more. A repository's hue is a fact about the
    // repository and is chosen once in Settings, so what these assert is that the band
    // reads that one answer — and that a plan written before the move does not lose the
    // choice somebody had already made.

    [Fact]
    public async Task ABandTakesTheColourTheRepositoryWasGivenInSettings()
    {
        Configure("JSdotNet/Backlog");
        Assert.Null(RepositorySettings.SetRepositoryColour("backlog", 4));

        using var context = Context();
        var band = await PlannedAsync(context);

        Assert.Equal(
            "var(--color-band-4)",
            band.FindComponent<RoadmapTimeline>().Instance.Groups.Single(group => group.Id == "backlog").Color);
    }

    [Fact]
    public async Task ARepositoryNobodyHasColouredStillGetsOne()
    {
        Configure("JSdotNet/Backlog");

        using var context = Context();
        var band = await PlannedAsync(context);

        // "Nobody chose" is not "no colour": the band is still one project's row and
        // still has to be told from the next one. What it must not be is neutral, which
        // is reserved for the unfiled band.
        Assert.Equal(
            "var(--color-band-1)",
            band.FindComponent<RoadmapTimeline>().Instance.Groups.Single(group => group.Id == "backlog").Color);
    }

    [Fact]
    public void TheBandOffersNoColourControlOfItsOwn()
    {
        Configure("JSdotNet/Backlog");

        using var context = Context();
        var band = context.Render<RoadmapBand>();

        // One choice, one place. A second editor here would be a second answer to which
        // colour this repository is, and the filter and the entry list would disagree
        // with whichever one was used last.
        Assert.Empty(band.FindAll("[data-testid=\"roadmap-band-colours\"]"));
    }

    [Fact]
    public async Task AColourChosenBeforeTheMoveIsCarriedOntoTheRepository()
    {
        Configure("JSdotNet/Backlog");
        await Planning.AddItemAsync(
            "Work",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 9),
            repositoryAliases: ["backlog"]);
        await StoreLegacyBandColourAsync("backlog", 5);

        using var context = Context();
        var band = Drawn(context);

        // Losing it would be the upgrade quietly recolouring somebody's chart.
        band.WaitForAssertion(() => Assert.Equal(
            5,
            RepositorySettings.Current.Find("backlog")!.Colour));
    }

    [Fact]
    public async Task ASettingsChoiceIsNotOverwrittenByTheOldPlanFile()
    {
        Configure("JSdotNet/Backlog");
        await Planning.AddItemAsync(
            "Work",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 9),
            repositoryAliases: ["backlog"]);
        await StoreLegacyBandColourAsync("backlog", 5);
        Assert.Null(RepositorySettings.SetRepositoryColour("backlog", 2));

        using var context = Context();
        var band = Drawn(context);

        band.WaitForAssertion(() => Assert.NotNull(
            band.FindComponent<RoadmapTimeline>().Instance.Groups.SingleOrDefault(group => group.Id == "backlog")));

        // The newer answer wins. A file carried forward from before the move is a
        // starting point, never a correction to a choice made since.
        Assert.Equal(2, RepositorySettings.Current.Find("backlog")!.Colour);
    }
}
