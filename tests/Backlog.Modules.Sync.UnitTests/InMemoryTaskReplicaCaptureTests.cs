using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.CaptureInboxItem;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// What the task replica calls a capture, away from HTTP: the kind token and
/// nothing else. Worth having separately from the endpoint tests for the reason
/// <see cref="InMemorySessionReplicaTests"/> gives — this adapter is the store
/// every endpoint test runs against, and the Cosmos adapter's query has to say
/// the same thing in SQL.
/// </summary>
public class InMemoryTaskReplicaCaptureTests
{
    private static readonly OwnerId Mine = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly OwnerScope Phone = new(Mine, new DeviceId(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")));
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_capture_is_listed_by_its_kind_token_and_a_routed_task_is_not()
    {
        var replica = new InMemoryTaskReplica();
        var capture = Guid.CreateVersion7();
        var routed = Guid.CreateVersion7();

        await replica.Upsert(Phone,
        [
            new TaskChange(capture, Noon, DeletedAt: null, Payload("Still waiting", CaptureInboxItemCommandHandler.CaptureType, sourceInboxId: "phone")),
            // A task the desktop made from an inbox item: the source is set, and
            // that must not make it a capture.
            new TaskChange(routed, Noon, DeletedAt: null, Payload("Routed from the inbox", "task", sourceInboxId: Guid.CreateVersion7().ToString("D"))),
        ], Cancellation);

        var captures = await replica.ListCaptures(Mine, Cancellation);

        Assert.Equal(capture, Assert.Single(captures).Change.Id);
    }

    [Fact]
    public async Task A_tombstoned_capture_is_not_listed()
    {
        var replica = new InMemoryTaskReplica();
        var capture = Guid.CreateVersion7();

        await replica.Upsert(Phone,
        [
            new TaskChange(capture, Noon, DeletedAt: null, Payload("Dealt with", CaptureInboxItemCommandHandler.CaptureType, sourceInboxId: "phone")),
        ], Cancellation);
        await replica.Upsert(Phone,
        [
            new TaskChange(capture, Noon.AddMinutes(1), DeletedAt: Noon.AddMinutes(1), Payload("Dealt with", CaptureInboxItemCommandHandler.CaptureType, sourceInboxId: "phone")),
        ], Cancellation);

        Assert.Empty(await replica.ListCaptures(Mine, Cancellation));
    }

    /// <summary>The least a document can be, with the two fields these tests are
    /// about set explicitly.</summary>
    private static TaskPayload Payload(string title, string type, string? sourceInboxId) => new(
        title,
        ContentMd: string.Empty,
        type,
        Status: "draft",
        Priority: "medium",
        Order: 0,
        Area: null,
        Noon,
        sourceInboxId,
        RecurrenceSourceId: null,
        DueOn: null,
        RemindAt: null,
        Recurrence: null,
        InMyDayOn: null,
        View: null,
        Effort: null,
        ImportPlanId: null,
        ImportItemId: null,
        AttachmentPath: null,
        Tags: [],
        RepoIds: [],
        DependsOn: [],
        SubItems: [],
        UsageEvents: [],
        ProjectionRefs: []);
}
