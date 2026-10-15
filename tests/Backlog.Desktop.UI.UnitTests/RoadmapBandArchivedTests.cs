using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Roadmap;
using Backlog.UI.Components.Selects;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The band over a registry with an archived repository (local ADR 0029,
/// <c>repository-management/requirements.md#the-roadmap-hides-an-archived-repositorys-open-work</c>):
/// its open work drawn in no band and never under No repository while show archived is
/// off; with show archived on — read from this device's port, as the header's switch
/// sets it — its whole band, greyed and tagged Archived. Which finished work an archived
/// band keeps in the drawn window is <see cref="RoadmapPlanViewArchivedTests"/>.
/// </summary>
public class RoadmapBandArchivedTests : RoadmapBandHarness
{
    private ShowArchivedRepositories ShowArchived { get; } = new(store: null);

    private async Task PlanAsync()
    {
        Configure("JSdotNet/Backlog", "innovadis-dev/Budgetbeheer");
        Assert.Null(RepositorySettings.SetRepositoryArchived("budgetbeheer", archived: true));

        await Planning.AddItemAsync(
            "Ship", new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 30),
            repositoryAliases: ["backlog"], cancellationToken: TestContext.Current.CancellationToken);
        await Planning.AddItemAsync(
            "Migrate the ledger", new DateOnly(2026, 10, 19), new DateOnly(2026, 11, 13),
            repositoryAliases: ["budgetbeheer"], cancellationToken: TestContext.Current.CancellationToken);
    }

    private BunitContext ArchivedContext()
    {
        var context = Context();
        context.Services.AddSingleton<IShowArchivedRepositories>(ShowArchived);
        return context;
    }

    [Fact]
    public async Task Open_work_of_an_archived_repository_is_in_no_band_and_not_under_no_repository()
    {
        await PlanAsync();

        using var context = ArchivedContext();
        var band = Drawn(context);
        var timeline = band.FindComponent<RoadmapTimeline>().Instance;

        Assert.DoesNotContain(timeline.Groups, group => group.Id == "budgetbeheer");
        Assert.Equal(["Ship"], timeline.Bars.Select(bar => bar.Title));
        Assert.DoesNotContain("Migrate the ledger", band.Markup);
        Assert.Empty(band.FindAll(".roadmap-timeline__group-tag"));
    }

    [Fact]
    public async Task Show_archived_on_draws_the_whole_band_greyed_and_tagged_archived()
    {
        await PlanAsync();
        ShowArchived.SetShown(true);

        using var context = ArchivedContext();
        var band = Drawn(context);
        var timeline = band.FindComponent<RoadmapTimeline>().Instance;

        Assert.True(Assert.Single(timeline.Groups, group => group.Id == "budgetbeheer").Archived);
        Assert.StartsWith(
            "budgetbeheer::",
            timeline.Bars.Single(bar => bar.Title == "Migrate the ledger").RowId,
            StringComparison.Ordinal);

        var archived = band.Find("[data-testid=\"roadmap-timeline-group-budgetbeheer\"]");
        Assert.Contains("roadmap-timeline__group--archived", archived.ClassList);
        Assert.Equal("Archived", archived.QuerySelector(".roadmap-timeline__group-tag")?.TextContent.Trim());
    }

    [Fact]
    public async Task Turning_show_archived_on_and_off_redraws_the_open_band()
    {
        await PlanAsync();

        using var context = ArchivedContext();
        var band = Drawn(context);

        ShowArchived.SetShown(true);
        band.WaitForAssertion(() => Assert.Contains(
            band.FindComponent<RoadmapTimeline>().Instance.Bars,
            bar => bar.Title == "Migrate the ledger"));

        ShowArchived.SetShown(false);
        band.WaitForAssertion(() => Assert.DoesNotContain(
            band.FindComponent<RoadmapTimeline>().Instance.Groups,
            group => group.Id == "budgetbeheer"));
    }

    [Fact]
    public async Task Unarchiving_the_repository_brings_its_band_back_as_an_active_one()
    {
        await PlanAsync();

        using var context = ArchivedContext();
        var band = Drawn(context);

        Assert.Null(RepositorySettings.SetRepositoryArchived("budgetbeheer", archived: false));

        band.WaitForAssertion(() =>
        {
            var group = Assert.Single(band.FindComponent<RoadmapTimeline>().Instance.Groups, group => group.Id == "budgetbeheer");
            Assert.False(group.Archived);
        });
        Assert.Contains("Migrate the ledger", band.Markup);
    }

    private static IReadOnlyList<string> OptionLabels(IRenderedComponent<RoadmapBand> band, string testId) =>
        [.. band.FindComponents<TagMultiSelect>()
            .Single(select => select.Instance.TestId == testId)
            .Instance.Options.Select(option => option.Label)];

    [Fact]
    public async Task Plan_work_offers_an_archived_repository_only_while_show_archived_is_on_and_says_so()
    {
        await PlanAsync();

        using var context = ArchivedContext();
        var band = Drawn(context);
        Open(band, "roadmap-band-add", "roadmap-editor");

        Assert.Equal(["JSdotNet/Backlog"], OptionLabels(band, "roadmap-editor-repositories"));

        ShowArchived.SetShown(true);
        band.WaitForAssertion(() => Assert.Equal(
            ["JSdotNet/Backlog", "innovadis-dev/Budgetbeheer" + ArchivedRepositoryLabel.Suffix],
            OptionLabels(band, "roadmap-editor-repositories")));
    }

    [Fact]
    public async Task A_new_date_offers_an_archived_repository_only_while_show_archived_is_on_and_says_so()
    {
        await PlanAsync();

        using var context = ArchivedContext();
        var band = Drawn(context);
        Open(band, "roadmap-band-add-date", "roadmap-milestone-editor");

        Assert.Equal(["JSdotNet/Backlog"], OptionLabels(band, "roadmap-milestone-editor-repositories"));

        ShowArchived.SetShown(true);
        band.WaitForAssertion(() => Assert.Equal(
            ["JSdotNet/Backlog", "innovadis-dev/Budgetbeheer" + ArchivedRepositoryLabel.Suffix],
            OptionLabels(band, "roadmap-milestone-editor-repositories")));
    }

    /// <summary>Work already filed under the archived repository keeps it offered, so
    /// the picker never holds a value it does not show and a Save never drops it.</summary>
    [Fact]
    public void Work_filed_under_an_archived_repository_keeps_it_offered_marked_archived()
    {
        using var context = ArchivedContext();
        var item = new RoadmapItemDto(
            Guid.NewGuid(), "Migrate the ledger", new DateOnly(2026, 10, 19), new DateOnly(2026, 11, 13),
            PlanningPriority.Medium, ["budgetbeheer"], Lane: null, TaskId: null, DependsOn: [], Tag: "ledger");

        var editor = context.Render<RoadmapItemEditor>(parameters => parameters
            .Add(component => component.Open, true)
            .Add(component => component.Item, item)
            .Add(component => component.ShowArchived, false)
            .Add(component => component.Repositories, (IReadOnlyList<PlannedRepository>)
            [
                new("backlog", "JSdotNet/Backlog"),
                new("budgetbeheer", "innovadis-dev/Budgetbeheer", Archived: true),
                new("old", "innovadis-dev/Old", Archived: true),
            ]));

        Assert.Equal(
            ["JSdotNet/Backlog", "innovadis-dev/Budgetbeheer" + ArchivedRepositoryLabel.Suffix],
            [.. editor.FindComponent<TagMultiSelect>().Instance.Options.Select(option => option.Label)]);
    }
}
