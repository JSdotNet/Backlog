using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Roadmap;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Adding, editing and deleting planned work through the dialog, against the real
/// stored plan. The dialog's callbacks are raised directly rather than by typing into
/// every field: what these are about is what the band does with a submission, and the
/// fields themselves are asserted where they are populated.
/// </summary>
public class RoadmapBandEditingTests : RoadmapBandHarness
{
    private static RoadmapEditorSubmission Submission(
        Guid? itemId = null,
        string title = "Planned work",
        int startDay = 6,
        int endDay = 17,
        PlanningPriority priority = PlanningPriority.Medium,
        string[]? repositories = null,
        string? lane = null,
        string? notes = null) =>
        new(
            itemId,
            title,
            new DateOnly(2026, 4, startDay),
            new DateOnly(2026, 4, endDay),
            priority,
            repositories ?? [],
            lane,
            null,
            notes);

    private static RoadmapItemEditor Editor(IRenderedComponent<RoadmapBand> band) =>
        band.FindComponent<RoadmapItemEditor>().Instance;

    private static async Task Activate(IRenderedComponent<RoadmapBand> band, Guid itemId)
    {
        var timeline = band.FindComponent<RoadmapTimeline>();
        await band.InvokeAsync(() => timeline.Instance.OnBarSelected.InvokeAsync(itemId.ToString()));
    }

    [Fact]
    public void PlanWorkOpensAnEmptyEditor()
    {
        using var context = Context();
        var band = context.Render<RoadmapBand>();

        Open(band, "roadmap-band-add", "roadmap-editor");

        Assert.NotNull(band.Find("[data-testid=\"roadmap-editor\"]"));
        Assert.Equal(string.Empty, band.Find("[data-testid=\"roadmap-editor-title\"] input").GetAttribute("value"));
        Assert.Null(Editor(band).Item);
    }

    [Fact]
    public void ThereIsNoDialogUntilSomethingAsksForOne()
    {
        using var context = Context();

        var band = context.Render<RoadmapBand>();

        Assert.Empty(band.FindAll("[data-testid=\"roadmap-editor\"]"));
    }

    [Fact]
    public async Task AddingFromTheEditorStoresIt()
    {
        Configure("JSdotNet/Backlog");

        using var context = Context();
        var band = context.Render<RoadmapBand>();
        Open(band, "roadmap-band-add", "roadmap-editor");

        await band.InvokeAsync(() => Editor(band).OnSave.InvokeAsync(Submission(
            title: "Typed into the dialog",
            priority: PlanningPriority.High,
            repositories: ["backlog"],
            lane: "platform",
            notes: "A note")));

        var item = Assert.Single((await Planning.GetPlanAsync()).Items);

        Assert.Equal("Typed into the dialog", item.Title);
        Assert.Equal(new DateOnly(2026, 4, 6), item.Start);
        Assert.Equal(new DateOnly(2026, 4, 17), item.End);
        Assert.Equal(PlanningPriority.High, item.Priority);
        Assert.Equal(["backlog"], item.RepositoryAliases);
        Assert.Equal("platform", item.Lane);
        Assert.Equal("A note", item.Notes);

        // Accepted, so the dialog is gone and the new work is on the chart.
        Assert.Empty(band.FindAll("[data-testid=\"roadmap-editor\"]"));
        Assert.Contains("Typed into the dialog", band.Markup);
    }

    [Fact]
    public async Task OpeningABarEditsThatItem()
    {
        var added = await Planning.AddItemAsync("Already planned", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));

        using var context = Context();
        var band = Drawn(context);
        await Activate(band, added.Value.Id);

        Assert.Equal(added.Value.Id, Editor(band).Item?.Id);
        Assert.Equal(
            "Already planned",
            band.WaitForElement("[data-testid=\"roadmap-editor-title\"] input").GetAttribute("value"));
    }

    [Fact]
    public async Task EditingFromTheEditorStoresIt_AndKeepsTheSameItem()
    {
        var added = await Planning.AddItemAsync("Before", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));

        using var context = Context();
        var band = Drawn(context);
        await Activate(band, added.Value.Id);

        await band.InvokeAsync(() => Editor(band).OnSave.InvokeAsync(Submission(
            itemId: added.Value.Id,
            title: "After",
            priority: PlanningPriority.Critical)));

        var item = Assert.Single((await Planning.GetPlanAsync()).Items);

        Assert.Equal(added.Value.Id, item.Id);
        Assert.Equal("After", item.Title);
        Assert.Equal(new DateOnly(2026, 4, 6), item.Start);
        Assert.Equal(PlanningPriority.Critical, item.Priority);
    }

    [Fact]
    public async Task ARefusedSaveKeepsTheEditorOpenAndSaysWhy()
    {
        using var context = Context();
        var band = context.Render<RoadmapBand>();
        Open(band, "roadmap-band-add", "roadmap-editor");

        // A window that ends before it starts. The dialog's own check catches this
        // first in normal use; going straight to the callback proves the band does not
        // rely on that, and shows the module's refusal reaching the dialog.
        await band.InvokeAsync(() => Editor(band).OnSave.InvokeAsync(
            Submission(title: "Backwards", startDay: 17, endDay: 6)));

        Assert.NotNull(band.Find("[data-testid=\"roadmap-editor\"]"));
        Assert.NotNull(band.Find("[data-testid=\"roadmap-editor-error\"]"));
        Assert.Empty((await Planning.GetPlanAsync()).Items);
    }

    [Fact]
    public async Task DeletingFromTheEditorRemovesIt()
    {
        var added = await Planning.AddItemAsync("Doomed", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));

        using var context = Context();
        var band = Drawn(context);
        await Activate(band, added.Value.Id);

        await band.InvokeAsync(() => Editor(band).OnDelete.InvokeAsync(added.Value.Id));

        Assert.Empty((await Planning.GetPlanAsync()).Items);
        Assert.Empty(band.FindAll("[data-testid=\"roadmap-editor\"]"));
        Assert.NotNull(band.Find("[data-testid=\"roadmap-band-empty-state\"]"));
    }

    [Fact]
    public async Task ADependencyAddedInTheEditorIsStored()
    {
        var design = await Planning.AddItemAsync("Design", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));
        var build = await Planning.AddItemAsync("Build", new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 16));

        using var context = Context();
        var band = Drawn(context);
        await Activate(band, build.Value.Id);

        await band.InvokeAsync(() => Editor(band).OnDependencyChanged.InvokeAsync(
            new RoadmapDependencyChange(build.Value.Id, design.Value.Id, Added: true)));

        var plan = await Planning.GetPlanAsync();

        Assert.Equal([design.Value.Id], plan.Items.Single(item => item.Id == build.Value.Id).DependsOn);
    }

    [Fact]
    public async Task ADependencyTakenAwayInTheEditorIsRemoved()
    {
        var design = await Planning.AddItemAsync("Design", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));
        var build = await Planning.AddItemAsync("Build", new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 16));
        Assert.True((await Planning.AddDependencyAsync(build.Value.Id, design.Value.Id)).IsSuccess);

        using var context = Context();
        var band = Drawn(context);
        await Activate(band, build.Value.Id);

        await band.InvokeAsync(() => Editor(band).OnDependencyChanged.InvokeAsync(
            new RoadmapDependencyChange(build.Value.Id, design.Value.Id, Added: false)));

        var plan = await Planning.GetPlanAsync();

        Assert.Empty(plan.Items.Single(item => item.Id == build.Value.Id).DependsOn);
    }

    [Fact]
    public async Task ADependencyThatWouldCloseACycleIsRefusedAndExplained()
    {
        var design = await Planning.AddItemAsync("Design", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));
        var build = await Planning.AddItemAsync("Build", new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 16));
        Assert.True((await Planning.AddDependencyAsync(build.Value.Id, design.Value.Id)).IsSuccess);

        using var context = Context();
        var band = Drawn(context);
        await Activate(band, design.Value.Id);

        await band.InvokeAsync(() => Editor(band).OnDependencyChanged.InvokeAsync(
            new RoadmapDependencyChange(design.Value.Id, build.Value.Id, Added: true)));

        // Reported in the dialog, and the stored plan is untouched.
        Assert.NotNull(band.Find("[data-testid=\"roadmap-editor-error\"]"));

        var plan = await Planning.GetPlanAsync();
        Assert.Empty(plan.Items.Single(item => item.Id == design.Value.Id).DependsOn);
    }

    [Fact]
    public async Task AnItemIsNeverOfferedAsItsOwnDependency()
    {
        var first = await Planning.AddItemAsync("First", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));
        await Planning.AddItemAsync("Second", new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 16));

        using var context = Context();
        var band = Drawn(context);
        await Activate(band, first.Value.Id);

        var options = Editor(band).DependencyOptions;

        Assert.DoesNotContain(first.Value.Id.ToString(), options.Select(option => option.Value));
        Assert.Contains("Second", options.Select(option => option.Label));
    }

    [Fact]
    public async Task AMilestoneCanBeWaitedFor_SoItIsOfferedToo()
    {
        var work = await Planning.AddItemAsync("Work", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));
        await Planning.AddMilestoneAsync("Code freeze", new DateOnly(2026, 2, 2), MilestoneKind.Freeze);

        using var context = Context();
        var band = Drawn(context);
        await Activate(band, work.Value.Id);

        Assert.Contains("Code freeze", Editor(band).DependencyOptions.Select(option => option.Label));
    }

    [Fact]
    public async Task OpeningAMilestoneSelectsItWithoutOpeningAnItemEditor()
    {
        await Planning.AddItemAsync("Work", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));
        var release = await Planning.AddMilestoneAsync("1.0", new DateOnly(2026, 2, 2));

        using var context = Context();
        var band = Drawn(context);

        // A milestone has no editor of its own yet. Showing the item dialog for one
        // would offer a start date, an end date and a lane for something that has none.
        await Activate(band, release.Value.Id);

        Assert.Empty(band.FindAll("[data-testid=\"roadmap-editor\"]"));
    }

    [Fact]
    public async Task TheEditorOffersTheConfiguredRepositories()
    {
        Configure("JSdotNet/Backlog", "fincent = JSdotNet/Fincent");
        var added = await Planning.AddItemAsync("Work", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));

        using var context = Context();
        var band = Drawn(context);
        await Activate(band, added.Value.Id);

        Assert.Equal(
            ["backlog", "fincent"],
            Editor(band).Repositories.Select(repository => repository.Alias));
    }
}
