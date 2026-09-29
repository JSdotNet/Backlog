using System.Net.Http.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Endpoints;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The roadmap plan and the planning pace ride the task feed as ordinary task
/// documents with a type the service does not interpret (local ADR 0018, Decision §4
/// and Verification 9). Nothing in the service changed for them; these pin that it
/// did not need to — the payload comes back whole, the replica's precedence applies to
/// it like any other document, and the inbox listing never offers one.
/// </summary>
public sealed class RoadmapDocumentEndpointTests : IDisposable
{
    private const string PlanJson =
        """{"version":1,"items":[{"id":"0b6e3f0e-1d7c-4f55-9a55-7c5f7a2f4a10","title":"Ship sync","start":"2026-10-01","end":"2026-10-09"}],"milestones":[],"bands":{"backlog":4}}""";

    private static readonly Guid PlanId = Guid.Parse("7c1a0b52-3d4e-4f6a-9b8c-0d1e2f3a4b01");
    private static readonly DateTimeOffset Morning = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_plan_document_comes_back_from_the_feed_whole()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        await device.PushChanges([Plan(PlanJson, Morning)]);

        var arrived = Assert.Single((await device.PullTasks()).Tasks);
        Assert.Equal(PlanId, arrived.Change.Id);
        Assert.Equal("roadmap-plan", arrived.Change.Task.Type);
        Assert.Equal(PlanJson, arrived.Change.Task.ContentMd);
        Assert.Equal(Morning, arrived.Change.UpdatedAt);
    }

    /// <summary>ADR 0018 Verification 2, at the service: an older copy of the plan is
    /// refused like any stale task, and the later one stays.</summary>
    [Fact]
    public async Task An_older_plan_is_refused_and_the_later_one_stays()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        await device.PushChanges([Plan("""{"version":1,"items":[]}""", Morning.AddMinutes(5))]);
        var stale = await device.PushChanges([Plan(PlanJson, Morning)]);

        Assert.Equal(0, (await stale.Content.ReadFromJsonAsync<PushTasksResponse>(Cancellation))!.Accepted);
        Assert.Equal(Morning.AddMinutes(5), Assert.Single((await device.PullTasks()).Tasks).Change.UpdatedAt);
    }

    /// <summary>ADR 0018 Verification 9: the inbox listing the phone and the IDE read
    /// filters on <c>capture</c>, so neither roadmap document is ever offered as
    /// one.</summary>
    [Fact]
    public async Task The_inbox_lists_no_roadmap_document()
    {
        var device = await _service.CreateClient().RegisteredDevice("Phone");
        var inbox = SyncRoutes.Absolute(SyncRoutes.Inbox);

        await device.PushChanges(
        [
            Plan(PlanJson, Morning),
            Plan("""{"storyPointsPerWeek":12}""", Morning) with
            {
                Id = Guid.Parse("7c1a0b52-3d4e-4f6a-9b8c-0d1e2f3a4b02"),
                Task = Plan("{}", Morning).Task with { Type = "planning-pace", Title = "Planning pace" },
            },
        ]);
        await device.PostAsJsonAsync(inbox, new CaptureRequest("Call the dentist", "phone"), Cancellation);

        var items = (await device.GetFromJsonAsync<List<InboxItem>>(inbox, Cancellation))!;

        Assert.Equal("Call the dentist", Assert.Single(items).Title);
    }

    private static TaskChange Plan(string content, DateTimeOffset updatedAt) =>
        new(PlanId, updatedAt, DeletedAt: null, TaskSyncClientExtensions.Payload("Roadmap plan") with
        {
            ContentMd = content,
            Type = "roadmap-plan",
        });
}
