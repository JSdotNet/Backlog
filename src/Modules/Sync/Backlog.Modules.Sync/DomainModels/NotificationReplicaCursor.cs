using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// A verified position in one owner's notification feed — <see cref="AnnotationReplicaCursor"/>
/// for the fourth replica container, its own type so a continuation for one feed cannot be
/// handed to another feed's replica.
/// </summary>
public readonly record struct NotificationReplicaCursor(OwnerId Owner, string Continuation)
{
    /// <summary>The only way to get one from something a client sent: through a verified
    /// <see cref="SyncCursor"/>.</summary>
    public NotificationReplicaCursor(SyncCursor verified)
        : this(verified.Owner, verified.Continuation)
    {
    }
}

/// <summary>One page of an owner's notification feed, under <see cref="TaskReplicaPage"/>'s
/// contract: the cursor is always the position after this page, and only
/// <see cref="HasMore"/> false says the feed is drained.</summary>
public sealed record NotificationReplicaPage(IReadOnlyList<NotificationChangeRecord> Changes, NotificationReplicaCursor Cursor, bool HasMore);
