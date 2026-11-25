namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// One question a session is waiting on the person for, as the docked session queue's
/// <b>Needs you</b> group lists it (local ADR 0062 §6, local ADR 0041). Tasks' own copy of
/// the Sessions context's waiting question, for the reason
/// <see cref="EntrySessionRequestState"/> is one: a screen asks its own module.
/// </summary>
/// <param name="Id">The waiting question's id.</param>
/// <param name="SessionId">The session that asked, which Answer opens.</param>
/// <param name="Text">The first question it asked, in its own words.</param>
/// <param name="AskedAt">When it asked.</param>
public sealed record SessionQueueQuestion(Guid Id, string SessionId, string Text, DateTimeOffset AskedAt);

/// <summary>How many sessions the Manager has working against the most it starts at once,
/// from its latest check-in (local ADR 0051 §4).</summary>
/// <param name="Working">Sessions it started that are still working.</param>
/// <param name="Limit">The most it starts at once.</param>
public sealed record SessionQueueSlots(int Working, int Limit);

/// <summary>What the docked session queue shows beside the requests: the questions waiting
/// on the person and the Manager's slots.</summary>
/// <param name="Questions">Every open waiting question, oldest first.</param>
/// <param name="Slots">The Manager's slots, or null when no Manager has checked in.</param>
public sealed record SessionQueueAttention(IReadOnlyList<SessionQueueQuestion> Questions, SessionQueueSlots? Slots)
{
    /// <summary>Nothing waits on the person, and no Manager has checked in.</summary>
    public static SessionQueueAttention None { get; } = new([], null);
}

/// <summary>
/// The docked session queue's read of what needs the person and how full the Manager is
/// (local ADR 0062 §6). Answered by an adapter over the Sessions context's waiting
/// questions and loop register, so this module never references Sessions; a host that
/// composes neither registers nothing, and the queue shows no questions and no slots.
/// </summary>
public interface ISessionQueueAttention
{
    /// <summary>The open questions and the Manager's slots. Never throws for an
    /// unreadable store: what could not be read is not in the answer.</summary>
    Task<SessionQueueAttention> ReadAsync(CancellationToken cancellationToken = default);
}
