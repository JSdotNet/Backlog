using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// A verified position in one owner's knowledge event feed. Its own type, as
/// <see cref="DevbookSubjectReplicaCursor"/> is, so a continuation for one feed cannot be
/// handed to another feed's replica by a caller who reached for the wrong local variable;
/// the owner is part of the value for the reason <see cref="TaskReplicaCursor"/> documents.
/// </summary>
public readonly record struct KnowledgeEventReplicaCursor(OwnerId Owner, string Continuation)
{
    /// <summary>The only way to get one from something a client sent: through a
    /// <see cref="SyncCursor"/>, which cannot be built without the signature having verified
    /// against the caller.</summary>
    public KnowledgeEventReplicaCursor(SyncCursor verified)
        : this(verified.Owner, verified.Continuation)
    {
    }
}

/// <summary>
/// One page of an owner's knowledge event feed, under <see cref="TaskReplicaPage"/>'s
/// contract: <see cref="Cursor"/> is always the position after this page, and
/// <see cref="HasMore"/> is false only when the store has said the feed is drained.
/// </summary>
public sealed record KnowledgeEventReplicaPage(IReadOnlyList<KnowledgeEventChangeRecord> Changes, KnowledgeEventReplicaCursor Cursor, bool HasMore);
