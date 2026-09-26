using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// A verified position in one owner's annotation feed.
/// <para>
/// The owner is part of the value for the reason <see cref="TaskReplicaCursor"/>
/// documents: a store continuation embeds the feed range it was minted for, so
/// an unverified one handed to the replica would read whichever partition it
/// came from and the store would not object (.devbook/arc42/adr/0005 §Identity). Its
/// own type rather than a reuse of the task cursor so a continuation for one
/// feed cannot be handed to the other's replica by a caller who reached for the
/// wrong local variable — see <see cref="SyncCursor"/>.
/// </para>
/// </summary>
public readonly record struct AnnotationReplicaCursor(OwnerId Owner, string Continuation)
{
    /// <summary>The only way to get one from something a client sent: through a
    /// <see cref="SyncCursor"/>, which the codec produces and which cannot be
    /// built without the signature having verified against the caller.</summary>
    public AnnotationReplicaCursor(SyncCursor verified)
        : this(verified.Owner, verified.Continuation)
    {
    }
}

/// <summary>
/// One page of an owner's annotation feed, under <see cref="TaskReplicaPage"/>'s
/// contract: <see cref="Cursor"/> is always the position after this page, even
/// when <see cref="Changes"/> is empty, and <see cref="HasMore"/> is false only
/// when the store has said the feed is drained — never inferred from a count.
/// </summary>
public sealed record AnnotationReplicaPage(IReadOnlyList<AnnotationChangeRecord> Changes, AnnotationReplicaCursor Cursor, bool HasMore);
