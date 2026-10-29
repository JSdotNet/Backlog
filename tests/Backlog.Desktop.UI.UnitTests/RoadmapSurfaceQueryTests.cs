using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Roadmap;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Roadmap surface follows <c>plan=&lt;tag&gt;</c> (local ADR 0042 §6): the chart
/// opens with the bar of the item carrying that tag selected, without opening its
/// editor; and its "Open in Tasks" is a jump to the Tasks surface through the window's
/// navigator, with the plan and the item's linked task as the query.
/// </summary>
public sealed class RoadmapSurfaceQueryTests : RoadmapBandHarness
{
    [Theory]
    [InlineData("plan-b")]
    [InlineData("+plan-b")]
    [InlineData("+Plan-B")]
    public async Task Plan_opens_the_roadmap_with_that_plans_bar_selected(string plan)
    {
        Configure("JSdotNet/Backlog");
        await Planning.AddItemAsync("Plan A", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), repositoryAliases: ["backlog"], cancellationToken: TestContext.Current.CancellationToken);
        var added = await Planning.AddItemAsync("Plan B", new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 16), repositoryAliases: ["backlog"], cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(added.IsSuccess);

        using var context = SurfaceContext();
        var surface = context.Render<RoadmapSurface>(parameters => parameters
            .Add(component => component.Query, new Dictionary<string, string> { ["plan"] = plan }));

        surface.WaitForAssertion(() =>
        {
            var selected = surface.FindComponent<RoadmapTimeline>().Instance.SelectedId;
            Assert.Equal(added.Value.Id, RoadmapPlanView.NodeIdOf(selected));
        });
        Assert.Empty(surface.FindAll("[data-testid='roadmap-editor']"));
        Assert.NotEmpty(surface.FindAll("[data-testid='roadmap-view'] [data-testid='roadmap-band']"));
    }

    [Fact]
    public async Task A_plan_no_item_carries_selects_nothing()
    {
        Configure("JSdotNet/Backlog");
        await Planning.AddItemAsync("Plan A", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), repositoryAliases: ["backlog"], cancellationToken: TestContext.Current.CancellationToken);

        using var context = SurfaceContext();
        var surface = context.Render<RoadmapSurface>(parameters => parameters
            .Add(component => component.Query, new Dictionary<string, string> { ["plan"] = "nothing-here" }));

        surface.WaitForElement("[data-testid='roadmap-timeline']");
        Assert.Null(surface.FindComponent<RoadmapTimeline>().Instance.SelectedId);
    }

    [Fact]
    public async Task Open_in_tasks_jumps_to_the_tasks_surface_with_the_plan()
    {
        Configure("JSdotNet/Backlog");
        var added = await Planning.AddItemAsync("Plan A", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), repositoryAliases: ["backlog"], cancellationToken: TestContext.Current.CancellationToken);
        await CarriedByATaskAsync(added.Value.Tag);

        using var context = SurfaceContext();
        var requests = new List<SurfaceRequest>();
        context.Services.GetRequiredService<ISurfaceNavigator>().ShowRequested += requests.Add;
        var surface = context.Render<RoadmapSurface>();
        surface.WaitForElement("[data-testid='roadmap-timeline']");

        var timeline = surface.FindComponent<RoadmapTimeline>();
        await surface.InvokeAsync(() => timeline.Instance.OnBarSelected.InvokeAsync(added.Value.Id.ToString()));
        surface.WaitForElement("[data-testid='roadmap-editor-open-tasks']").Click();

        surface.WaitForAssertion(() =>
        {
            var request = Assert.Single(requests);
            Assert.Equal("tasks", request.SurfaceId);
            Assert.Equal("+plan-a", request.Value("plan"));
        });
    }

    private BunitContext SurfaceContext()
    {
        var context = Context();
        context.Services.AddSingleton<ISurfaceNavigator, SurfaceNavigator>();
        return context;
    }
}
