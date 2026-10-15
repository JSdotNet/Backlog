using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Roadmap;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// "Open in Tasks" from a roadmap item: which plan tag and which repositories the band
/// asks the shell to show. The band raises the request and the shell answers it, so the
/// roadmap never reaches into the task list (roadmap/features.md#reading-and-rescheduling-on-a-timeline).
/// Over the real stored plan, with <c>backlog</c> and <c>fincent</c> configured.
/// </summary>
public sealed class RoadmapBandOpenTasksTests : RoadmapBandHarness
{
    private readonly List<RoadmapTasksRequest> _requests = [];

    private IRenderedComponent<RoadmapBand> Band(BunitContext context)
    {
        var band = context.Render<RoadmapBand>(parameters => parameters
            .Add(component => component.OnOpenTasks, (RoadmapTasksRequest request) => _requests.Add(request)));
        band.WaitForElement("[data-testid='roadmap-timeline']");
        return band;
    }

    private async Task<Guid> PlannedAsync(string title, params string[] repositories)
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Fincent");

        var added = await Planning.AddItemAsync(
            title,
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 9),
            repositoryAliases: repositories,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(added.IsSuccess);

        // Open in Tasks is offered for work tasks carry.
        await CarriedByATaskAsync(added.Value.Tag);

        return added.Value.Id;
    }

    /// <summary>Opens the editor from <paramref name="barId"/> and presses Open in Tasks.</summary>
    private static async Task OpenInTasksFrom(IRenderedComponent<RoadmapBand> band, string barId)
    {
        var timeline = band.FindComponent<RoadmapTimeline>();
        await band.InvokeAsync(() => timeline.Instance.OnBarSelected.InvokeAsync(barId));

        band.WaitForElement("[data-testid='roadmap-editor-open-tasks']").Click();
    }

    [Fact]
    public async Task A_single_bar_asks_for_its_plan_in_the_repository_it_is_filed_in()
    {
        var id = await PlannedAsync("Plan A", "backlog");

        using var context = Context();
        var band = Band(context);
        await OpenInTasksFrom(band, id.ToString());

        band.WaitForAssertion(() =>
        {
            var request = Assert.Single(_requests);
            Assert.Equal("+plan-a", request.PlanTag);
            Assert.Equal(["backlog"], request.RepositoryAliases);
        });
    }

    /// <summary>The press closes the editor: the reader is on their way to the list.</summary>
    [Fact]
    public async Task Opening_the_plan_in_tasks_closes_the_editor()
    {
        var id = await PlannedAsync("Plan A", "backlog");

        using var context = Context();
        var band = Band(context);
        await OpenInTasksFrom(band, id.ToString());

        band.WaitForAssertion(() => Assert.Empty(band.FindAll("[data-testid='roadmap-editor']")));
    }

    [Fact]
    public async Task A_full_name_on_the_item_scopes_to_its_configured_alias()
    {
        var id = await PlannedAsync("Plan A", "JSdotNet/Backlog");

        using var context = Context();
        var band = Band(context);
        await OpenInTasksFrom(band, id.ToString());

        band.WaitForAssertion(() => Assert.Equal(["backlog"], Assert.Single(_requests).RepositoryAliases));
    }

    /// <summary>One band's part of an item filed in two repositories is that repository's
    /// share of the work, so the list is scoped to it alone.</summary>
    [Theory]
    [InlineData("backlog")]
    [InlineData("fincent")]
    public async Task One_repositorys_part_scopes_to_that_repository_alone(string alias)
    {
        var id = await PlannedAsync("Plan A", "backlog", "fincent");

        using var context = Context();
        var band = Band(context);
        await OpenInTasksFrom(band, $"{id}{RoadmapPlanView.PartSeparator}{alias}");

        band.WaitForAssertion(() =>
        {
            var request = Assert.Single(_requests);
            Assert.Equal("+plan-a", request.PlanTag);
            Assert.Equal([alias], request.RepositoryAliases);
        });
    }

    [Fact]
    public async Task One_phase_of_a_hand_over_scopes_to_its_band()
    {
        var id = await PlannedAsync("Plan A", "backlog", "fincent");

        using var context = Context();
        var band = Band(context);
        await OpenInTasksFrom(band, $"{id}{RoadmapPlanView.PartSeparator}fincent{RoadmapPlanView.SegmentSeparator}2");

        band.WaitForAssertion(() => Assert.Equal(["fincent"], Assert.Single(_requests).RepositoryAliases));
    }

    // --- From the bar itself ---------------------------------------------------------

    private const string BarAction = "[data-roadmap-action]";

    /// <summary>AC3: the action at the end of the bar opens the plan in Tasks without the
    /// editor ever opening — scoped to the whole item, not the part it sits on.</summary>
    [Fact]
    public async Task The_bars_own_action_opens_the_plan_in_tasks_without_opening_the_editor()
    {
        await PlannedAsync("Plan A", "backlog", "fincent");

        using var context = Context();
        var band = Band(context);

        var action = band.WaitForElement(BarAction);
        Assert.Equal("open", action.GetAttribute("data-roadmap-action"));
        Assert.Equal("Open Plan A in Tasks", action.GetAttribute("aria-label"));
        Assert.Single(band.FindAll(BarAction));

        action.Click();

        band.WaitForAssertion(() =>
        {
            var request = Assert.Single(_requests);
            Assert.Equal("+plan-a", request.PlanTag);
            Assert.Equal(["backlog", "fincent"], request.RepositoryAliases);
        });
        Assert.Empty(band.FindAll("[data-testid='roadmap-editor']"));
    }

    /// <summary>AC2: work no task carries offers Complete on its bar, never Open in Tasks;
    /// pressing it stores today without opening the editor, and the bar then offers
    /// nothing.</summary>
    [Fact]
    public async Task A_bar_no_task_carries_offers_complete_and_completing_it_draws_it_done()
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Fincent");
        await Planning.AddItemAsync(
            "Launch post",
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 5),
            repositoryAliases: ["backlog"],
            cancellationToken: TestContext.Current.CancellationToken);

        using var context = Context();
        var band = context.Render<RoadmapBand>(parameters => parameters
            .Add(component => component.Today, PaceToday)
            .Add(component => component.OnOpenTasks, (RoadmapTasksRequest request) => _requests.Add(request)));

        var action = band.WaitForElement(BarAction);
        Assert.Equal("complete", action.GetAttribute("data-roadmap-action"));
        Assert.Equal("Complete Launch post", action.GetAttribute("aria-label"));

        action.Click();

        band.WaitForAssertion(() =>
        {
            Assert.Empty(band.FindAll(BarAction));
            Assert.True(Assert.Single(band.FindComponent<RoadmapTimeline>().Instance.Bars).Done);
        });
        Assert.Empty(_requests);
        Assert.Empty(band.FindAll("[data-testid='roadmap-editor']"));
        Assert.Equal(PaceToday, Assert.Single((await Planning.GetPlanAsync(TestContext.Current.CancellationToken)).Items).CompletedOn);
    }

    /// <summary>Work filed under no configured repository may have its tasks anywhere,
    /// so the list is scoped to every repository.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("retired")]
    public async Task Work_under_no_configured_repository_scopes_to_every_repository(string? filedAs)
    {
        var id = await PlannedAsync("Plan A", filedAs is null ? [] : [filedAs]);

        using var context = Context();
        var band = Band(context);
        await OpenInTasksFrom(band, id.ToString());

        band.WaitForAssertion(() => Assert.Empty(Assert.Single(_requests).RepositoryAliases));
    }
}
