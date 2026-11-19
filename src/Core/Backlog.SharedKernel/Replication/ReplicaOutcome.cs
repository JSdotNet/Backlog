namespace Backlog.SharedKernel.Replication;

/// <summary>
/// What a whole-document replication port did with an inbound copy from another device
/// (local ADR 0018): the goals', the impediments', the iteration settings', the roadmap's,
/// the GitHub settings' and the My Day preferences'. Four answers rather than a bool,
/// because the sync client counts the first as work applied, the last as a document this
/// build could not read, and the two between as nothing to do.
/// </summary>
public enum ReplicaOutcome
{
    /// <summary>The copy was later than the local one, or there was none, and it
    /// replaced it whole.</summary>
    Taken,

    /// <summary>The copy carries the stamp the local one already has: this device's own
    /// push coming back, or a replayed page. Nothing was written.</summary>
    Echo,

    /// <summary>The copy is older than the local one and was refused, as the replica
    /// refuses a stale push — or, where a port says so, the local one cannot be weighed
    /// against it. Nothing was written.</summary>
    Refused,

    /// <summary>The copy does not read as the document it claims to be, or this head
    /// keeps no such document. The local one was left exactly as it was.</summary>
    Unreadable,
}

/// <summary>
/// The stamp comparison every whole-document replication decides by: the later stamp
/// wins, an equal one is an echo, and an older one is refused (local ADR 0018).
/// </summary>
public static class LastWriteWins
{
    /// <summary>
    /// Weighs an inbound copy's stamp against the one this device holds. Answers
    /// <see cref="ReplicaOutcome.Echo"/> for an equal stamp and
    /// <see cref="ReplicaOutcome.Refused"/> for an older one — both meaning nothing is
    /// written — and <c>null</c> where the copy is later or nothing is held, so the caller
    /// goes on to write it.
    /// </summary>
    public static ReplicaOutcome? Weigh(DateTimeOffset inbound, DateTimeOffset? held)
    {
        if (held is not { } local) return null;
        if (inbound == local) return ReplicaOutcome.Echo;
        return inbound < local ? ReplicaOutcome.Refused : null;
    }
}
