namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What "Open in Tasks" hands the shell for a roadmap item: its plan tag, and the task
/// it names outright. The roadmap gathers an item's work by both
/// (.devbook/domain/roadmap/domain.md#roadmap-item-gathering), so a request carrying the
/// tag alone would leave the linked task out of the list the roadmap counted it in.
/// </summary>
public sealed class RoadmapTasksRequestTests
{
    private static readonly IReadOnlyList<PlannedRepository> Configured =
        [new("backlog", "JSdotNet/Backlog"), new("docs", "JSdotNet/Docs")];

    private static RoadmapItemDto Item(Guid? taskId) =>
        new(
            Guid.NewGuid(), "Ship it", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9),
            PlanningPriority.Medium, ["backlog"], Lane: null, TaskId: taskId, DependsOn: [], Tag: "release-q4");

    [Fact]
    public void A_request_names_the_task_the_item_links()
    {
        var linked = Guid.NewGuid();
        var item = Item(linked);

        var request = RoadmapTasksRequest.For(item, item.Id.ToString(), Configured);

        Assert.Equal(linked, request.LinkedTaskId);
        Assert.Equal("+release-q4", request.PlanTag);
    }

    [Fact]
    public void A_request_for_an_item_with_no_linked_task_names_none()
    {
        var item = Item(taskId: null);

        var request = RoadmapTasksRequest.For(item, item.Id.ToString(), Configured);

        Assert.Null(request.LinkedTaskId);
    }
}
