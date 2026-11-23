namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// Folds entries waiting in the outbox into one it has set aside, so a change
/// queued behind an entry the service refused travels with it instead of reaching
/// the service on its own, about something the service never received.
/// <para>
/// The outbox asks the folds as it sets an entry aside and at the start of each
/// pass, while nothing is being sent and under the lock every change to an entry
/// takes, so none changes between the question and the answer. It applies the
/// first answer:
/// the set-aside entry's new payload, and the waiting entries that are now part of
/// it and leave unsent — or, for no payload, the set-aside entry leaving unsent with
/// them. A fold reads payloads and changes nothing itself — the outbox is built from
/// its kinds, so a kind cannot reach back into it — until the outbox tells it
/// (<see cref="Removed"/>) that the set-aside entry is gone.
/// </para>
/// </summary>
public interface IOutboxFold
{
    /// <summary>What <paramref name="setAside"/> becomes with the entries in
    /// <paramref name="waiting"/> that belong to it — oldest first, the set-aside
    /// entry among them — or null when none does.</summary>
    OutboxFold? Fold(OutboxEntry setAside, IReadOnlyList<OutboxEntry> waiting);

    /// <summary>The outbox took <paramref name="setAside"/> off the device on this
    /// fold's answer of no payload: what the entry held outside the outbox — a talk
    /// note's files — can go now, and not before.</summary>
    void Removed(OutboxEntry setAside)
    {
    }
}

/// <summary>A fold's answer: the set-aside entry's new payload, or null when the
/// set-aside entry itself leaves the outbox unsent, and the ids of the waiting
/// entries it took in, which leave the outbox unsent.</summary>
public sealed record OutboxFold(string? PayloadJson, IReadOnlyList<Guid> Absorbed);
