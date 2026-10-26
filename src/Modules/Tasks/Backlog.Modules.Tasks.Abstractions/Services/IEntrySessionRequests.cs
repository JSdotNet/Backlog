namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// Where a session request for an entry stands while it is still waiting to be
/// answered, in the words the Sessions context uses for it
/// (<c>.devbook/domain/sessions/domain.md#session-request-queue</c>, local ADR 0038 §3).
/// <para>
/// Tasks' own copy of the three open states rather than a reference to Sessions, for
/// the reason <see cref="LinkedSessionState"/> is one: a screen asks its own module,
/// and the adapter that sees both contexts translates. Linked and Cancelled have no
/// member — a request in either is no longer waiting, so the port answers nothing
/// for it and the entry shows what it showed before it was asked.
/// </para>
/// </summary>
public enum EntrySessionRequestState
{
    /// <summary>Waiting for a dispatcher; the person may still cancel it. A claim
    /// whose lease ran out reads as this again.</summary>
    Requested,

    /// <summary>A dispatcher holds it and is starting the session.</summary>
    Claimed,

    /// <summary>The dispatcher reported the session it started; that session has not
    /// picked the entry up yet.</summary>
    Started
}

/// <summary>An open session request, as the Tasks screens draw it.</summary>
/// <param name="Id">The request's own id, which Cancel names.</param>
/// <param name="State">Where it stands now.</param>
/// <param name="Repository">The repository it asks for, as <c>owner/name</c>.</param>
public sealed record EntrySessionRequest(Guid Id, EntrySessionRequestState State, string Repository);

/// <summary>
/// The Start session action's port (local ADR 0038 §1): queue a session request for
/// one prompt entry, read which entries have one waiting, and cancel one.
/// <para>
/// Answered by an adapter over the Sessions context's request queue, so this module
/// never references Sessions. The request names the entry, the repository and its
/// clone folder and nothing the entry says: the brief is fixed by the dispatcher.
/// </para>
/// <para>
/// A host that composes no queue registers nothing, and the pane offers no action.
/// </para>
/// </summary>
public interface IEntrySessionRequests
{
    /// <summary>Whether the action is offered at all: the <c>session-requests</c>
    /// switch, read live, so turning it on or off shows or hides it on the next
    /// render.</summary>
    bool IsOffered { get; }

    /// <summary>The open request of each entry that has one — Requested, Claimed or
    /// Started — keyed by entry id. Never throws for an unreadable queue: what could
    /// not be read is not in the answer.</summary>
    Task<IReadOnlyDictionary<Guid, EntrySessionRequest>> OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Queues a request for <paramref name="entryId"/> in
    /// <paramref name="repository"/>, whose clone folder on this machine is
    /// <paramref name="cloneFolder"/>; or answers the request already open for the
    /// entry and queues nothing.</summary>
    Task<EntrySessionRequest> RequestAsync(
        Guid entryId,
        string repository,
        string cloneFolder,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels the request with <paramref name="requestId"/>. Answers
    /// whether it was cancelled — false once a dispatcher has started its session,
    /// or when no such request is held.</summary>
    Task<bool> CancelAsync(Guid requestId, CancellationToken cancellationToken = default);
}
