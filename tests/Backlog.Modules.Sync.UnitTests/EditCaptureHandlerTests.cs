using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.EditCapture;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The phone rewording one of its own captures before the desktop decides it:
/// the title and body change, nothing else the capture said does, and the
/// service's clock — never the phone's — orders the new version.
/// </summary>
public sealed class EditCaptureHandlerTests
{
    private static readonly OwnerScope Phone = new(OwnerId.New(), DeviceId.New());
    private static readonly DateTimeOffset CapturedAt = new(2026, 11, 18, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Stored = new(2026, 11, 18, 8, 31, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryTaskReplica _replica = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_edit_replaces_the_title_and_body_and_keeps_everything_else_the_capture_said()
    {
        var id = await Captured("Call the dentist", "before noon");

        var result = await Edit(id, "Call the dentist about the crown", "Ask for Thursday.");

        Assert.True(result.IsSuccess);
        Assert.Equal("Call the dentist about the crown", result.Value.Title);
        Assert.Equal("Ask for Thursday.", result.Value.BodyMd);

        var document = (await _replica.Find(Phone.OwnerId, id, Cancellation))!.Change;
        Assert.Equal("Call the dentist about the crown", document.Task.Title);
        Assert.Equal("Ask for Thursday.", document.Task.ContentMd);
        Assert.Equal("capture", document.Task.Type);
        Assert.Equal(CapturedAt, document.Task.CreatedAt);
        Assert.Equal("phone", document.Task.SourceInboxId);
        Assert.Equal(["health", "@sam"], document.Task.Tags);
        Assert.Equal(Attachment.Id, Assert.Single(document.Task.Attachments!).Id);
        Assert.Null(document.DeletedAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero), document.UpdatedAt);
    }

    /// <summary>A capture stamped ahead of the service's clock — a retry after
    /// the clock was moved, or a skewed earlier write — still gets a later
    /// version, or the replica would refuse the edit as stale.</summary>
    [Fact]
    public async Task An_edit_is_stamped_after_the_held_version_when_the_service_clock_is_not_later()
    {
        var id = await Captured("Call the dentist", null, updatedAt: _clock.GetUtcNow().AddMinutes(5));

        var result = await Edit(id, "Call the dentist today", null);

        Assert.True(result.IsSuccess);
        var document = (await _replica.Find(Phone.OwnerId, id, Cancellation))!.Change;
        Assert.Equal(_clock.GetUtcNow().AddMinutes(5).AddTicks(1), document.UpdatedAt);
        Assert.Equal("Call the dentist today", document.Task.Title);
        Assert.Equal(string.Empty, document.Task.ContentMd);
    }

    public static TheoryData<string> NotWaiting => ["missing", "acknowledged", "a task", "another owner's"];

    /// <summary>Everything that is not a live capture of this owner reads the
    /// same: the desktop already decided it, or it was never this caller's.</summary>
    [Theory]
    [MemberData(nameof(NotWaiting))]
    public async Task An_edit_of_anything_but_a_waiting_capture_is_not_found_and_writes_nothing(string what)
    {
        var id = Guid.CreateVersion7();
        var scope = Phone;

        switch (what)
        {
            case "acknowledged":
                await _replica.Upsert(Phone, [Capture(id, "Dealt with", null, Stored) with { DeletedAt = Stored }], Cancellation);
                break;
            case "a task":
                await _replica.Upsert(Phone, [Capture(id, "Ship it", null, Stored) with { Task = Capture(id, "Ship it", null, Stored).Task with { Type = "task" } }], Cancellation);
                break;
            case "another owner's":
                await _replica.Upsert(Phone, [Capture(id, "Mine", null, Stored)], Cancellation);
                scope = new OwnerScope(OwnerId.New(), DeviceId.New());
                break;
        }

        var before = await _replica.Find(Phone.OwnerId, id, Cancellation);

        var result = await new EditCaptureCommandHandler(_replica, _clock)
            .Handle(new EditCaptureCommand(scope, id, "Reworded", null), Cancellation);

        Assert.True(result.IsFailure, what);
        Assert.Equal(SyncErrorCodes.InboxItemNotFound, result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(before, await _replica.Find(Phone.OwnerId, id, Cancellation));
    }

    /// <summary>The desktop's tombstone lands between the read and the write.
    /// The replica refuses the edit, and the phone hears that the capture was
    /// already dealt with rather than that its edit was taken.</summary>
    [Fact]
    public async Task An_edit_the_replica_refuses_because_the_capture_was_decided_meanwhile_is_not_found()
    {
        var id = await Captured("Call the dentist", null);
        var racing = new TombstoneBeforeUpsert(_replica, Stored.AddSeconds(1));

        var result = await new EditCaptureCommandHandler(racing, _clock)
            .Handle(new EditCaptureCommand(Phone, id, "Reworded", null), Cancellation);

        Assert.Equal(SyncErrorCodes.InboxItemNotFound, result.Error.Code);
        var document = (await _replica.Find(Phone.OwnerId, id, Cancellation))!.Change;
        Assert.NotNull(document.DeletedAt);
        Assert.Equal("Call the dentist", document.Task.Title);
    }

    private static readonly AttachmentMetadata Attachment =
        new(Guid.CreateVersion7(), "crown.jpg", "image/jpeg", 10, new string('a', 64));

    private async Task<Guid> Captured(string title, string? body, DateTimeOffset? updatedAt = null)
    {
        var id = Guid.CreateVersion7();
        await _replica.Upsert(Phone, [Capture(id, title, body, updatedAt ?? Stored)], Cancellation);
        return id;
    }

    private Task<Result<InboxItem>> Edit(Guid id, string title, string? body) =>
        new EditCaptureCommandHandler(_replica, _clock).Handle(new EditCaptureCommand(Phone, id, title, body), Cancellation);

    private static TaskChange Capture(Guid id, string title, string? body, DateTimeOffset updatedAt) => new(
        id,
        updatedAt,
        DeletedAt: null,
        new TaskPayload(
            title,
            ContentMd: body ?? string.Empty,
            "capture",
            Status: "draft",
            Priority: "medium",
            Order: 0,
            Area: null,
            CapturedAt,
            SourceInboxId: "phone",
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
            Tags: ["health", "@sam"],
            RepoIds: [],
            DependsOn: [],
            SubItems: [],
            UsageEvents: [],
            ProjectionRefs: [],
            Attachments: [Attachment]));

    /// <summary>The replica as a desktop's push meets it mid-edit: its
    /// tombstone is written just before the edit's upsert arrives.</summary>
    private sealed class TombstoneBeforeUpsert(InMemoryTaskReplica inner, DateTimeOffset decidedAt) : ITaskReplica
    {
        private static readonly OwnerScope Desktop = new(Phone.OwnerId, DeviceId.New());

        public async Task<int> Upsert(OwnerScope scope, IReadOnlyList<TaskChange> changes, CancellationToken cancellationToken = default)
        {
            foreach (var change in changes)
            {
                var held = (await inner.Find(scope.OwnerId, change.Id, cancellationToken))!.Change;
                await inner.Upsert(Desktop, [held with { UpdatedAt = decidedAt, DeletedAt = decidedAt }], cancellationToken);
            }

            return await inner.Upsert(scope, changes, cancellationToken);
        }

        public Task<TaskReplicaPage> ReadChanges(OwnerId owner, TaskReplicaCursor? cursor, int maxItems, CancellationToken cancellationToken = default) =>
            inner.ReadChanges(owner, cursor, maxItems, cancellationToken);

        public Task<IReadOnlyList<TaskChangeRecord>> ListCaptures(OwnerId owner, CancellationToken cancellationToken = default) =>
            inner.ListCaptures(owner, cancellationToken);

        public Task<TaskChangeRecord?> Find(OwnerId owner, Guid id, CancellationToken cancellationToken = default) =>
            inner.Find(owner, id, cancellationToken);
    }
}
