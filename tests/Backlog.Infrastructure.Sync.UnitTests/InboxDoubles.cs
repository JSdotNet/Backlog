using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>The Inbox's intake, recording what it was handed and answering
/// whatever the test set. The sync client never sees past this port, so what
/// the Inbox does with a capture is not this project's business — only that it
/// was asked, and how the answer is counted.</summary>
internal sealed class RecordingInboxIntake : IInboxIntake
{
    public List<InboxCaptureDto> Received { get; } = [];

    public InboxIntakeOutcome Answer { get; set; } = InboxIntakeOutcome.Received;

    /// <summary>Thrown instead of answering, for captures whose title matches.
    /// Stands in for the aggregate refusing a document — the one way the
    /// intake can fail that the merge has to survive.</summary>
    public Func<InboxCaptureDto, Exception?>? Refuse { get; set; }

    public Task<InboxIntakeOutcome> ReceiveAsync(InboxCaptureDto capture, CancellationToken cancellationToken = default)
    {
        Received.Add(capture);

        if (Refuse?.Invoke(capture) is { } failure) throw failure;

        return Task.FromResult(Answer);
    }
}

/// <summary>The Inbox's outbox: a list of acknowledgements a test seeds, and a
/// record of which ids were marked sent.</summary>
internal sealed class RecordingInboxOutbox : IInboxCaptureOutbox
{
    public List<InboxCaptureAckDto> Pending { get; } = [];

    public List<Guid> MarkedSent { get; } = [];

    public Task<IReadOnlyList<InboxCaptureAckDto>> ListPendingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InboxCaptureAckDto>>([.. Pending]);

    public Task MarkSentAsync(IReadOnlyList<Guid> captureIds, CancellationToken cancellationToken = default)
    {
        MarkedSent.AddRange(captureIds);
        Pending.RemoveAll(ack => captureIds.Contains(ack.CaptureId));
        return Task.CompletedTask;
    }
}

/// <summary>Capture documents the way the service writes them: task-shaped,
/// with the <c>capture</c> kind token and the client's name in the source.</summary>
internal static class Captures
{
    public static TaskChange Change(
        string title,
        DateTimeOffset createdAt,
        Guid? id = null,
        DateTimeOffset? deletedAt = null,
        string status = "draft") =>
        new(id ?? Guid.CreateVersion7(), createdAt, deletedAt, Payload(title, createdAt, status, "capture", "phone"));

    /// <summary>A capture the way the mobile head wrote one before the
    /// <c>capture</c> kind existed: an ordinary <c>task</c> document with the
    /// phone's name in <c>sourceInboxId</c>. Nothing about it says capture
    /// any more, and the merge reads it as the task it claims to be.</summary>
    public static TaskChange LegacyChange(string title, DateTimeOffset createdAt, Guid? id = null) =>
        new(id ?? Guid.CreateVersion7(), createdAt, null, Payload(title, createdAt, "draft", "task", "mobile"));

    public static TaskChangeRecord Record(TaskChange change, Guid deviceId, long serverTimestamp) =>
        new(change, deviceId, serverTimestamp);

    private static TaskPayload Payload(string title, DateTimeOffset createdAt, string status, string type, string source) => new(
        title,
        ContentMd: string.Empty,
        type,
        status,
        Priority: "medium",
        Order: 0,
        Area: null,
        createdAt,
        SourceInboxId: source,
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
