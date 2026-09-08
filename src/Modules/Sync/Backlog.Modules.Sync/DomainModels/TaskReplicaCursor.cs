using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// A verified position in one owner's change feed.
/// <para>
/// The owner is part of the value, and that is the whole reason this type
/// exists rather than a string parameter. A cursor is minted by the service and
/// handed to a client, which sends it back; a Cosmos continuation embeds the
/// feed range it was minted for, so replaying somebody else's continuation
/// reads somebody else's partition and the store will not object
/// (.arc42/adr/0005 §Identity). Carrying the owner inside the value means an
/// unverified cursor cannot be handed to <c>ITaskReplica</c> at all — there is
/// no way to construct one without having said whose it is.
/// </para>
/// <para>
/// So there is deliberately no string overload on the port. Adding one would
/// give the check a bypass, and the bypass would be the shorter call.
/// </para>
/// </summary>
public readonly record struct TaskReplicaCursor(OwnerId Owner, string Continuation)
{
    /// <summary>The only way to get one from something a client sent: through a
    /// <see cref="SyncCursor"/>, which the codec produces and which cannot be
    /// built without the signature having verified against the caller. The codec
    /// answers with the neutral pair rather than with this type because there
    /// are two feeds and one check to make on both of them.</summary>
    public TaskReplicaCursor(SyncCursor verified)
        : this(verified.Owner, verified.Continuation)
    {
    }
}

/// <summary>
/// One page of an owner's change feed.
/// <para>
/// <see cref="Cursor"/> is always the position after this page, even when
/// <see cref="Changes"/> is empty: a drained feed still moves, and a client that
/// kept its previous cursor would ask the same question forever.
/// <see cref="HasMore"/> is false only when the store has said the feed is
/// drained. It is deliberately not derived from the page size: a page limit is a
/// hint to Cosmos, which returns fewer items as a response nears its own size
/// ceiling, so a short page and a caught-up feed cannot be told apart by
/// counting — and a device bootstrapping a large backlog would stop at the first
/// short page believing it had finished. An adapter that does know what is left
/// may answer exactly; what none of them may do is guess "no more" from a count.
/// </para>
/// </summary>
public sealed record TaskReplicaPage(IReadOnlyList<TaskChangeRecord> Changes, TaskReplicaCursor Cursor, bool HasMore);
