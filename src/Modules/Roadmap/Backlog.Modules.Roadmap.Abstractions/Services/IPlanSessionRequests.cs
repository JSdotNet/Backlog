namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>
/// Where a session request for a plan stands while it is still waiting to be
/// answered (local ADR 0038 §3). Roadmap's own copy of the three open states, for the
/// reason the Tasks port keeps its own: a screen asks its own module, and the adapter
/// that sees the Sessions context translates. Linked and Cancelled have no member.
/// </summary>
public enum PlanSessionRequestState
{
    /// <summary>Waiting for a dispatcher; the person may still cancel it.</summary>
    Requested,

    /// <summary>A dispatcher holds it and is starting the session.</summary>
    Claimed,

    /// <summary>The dispatcher reported the session it started.</summary>
    Started
}

/// <summary>An open plan session request, as the roadmap draws it.</summary>
/// <param name="Id">The request's own id, which Cancel names.</param>
/// <param name="State">Where it stands now.</param>
/// <param name="Repository">The repository it asks for, as <c>owner/name</c>.</param>
public sealed record PlanSessionRequest(Guid Id, PlanSessionRequestState State, string Repository);

/// <summary>
/// The plan queue icon's port (local ADR 0038 §2): queue a plan session — a request
/// for a session that runs a whole plan, by its tag — read which plans have one waiting,
/// and cancel one. Answered by an adapter over the Sessions context's queue; a host with
/// none registers nothing and the roadmap offers no queue icon.
/// </summary>
public interface IPlanSessionRequests
{
    /// <summary>Whether the action is offered at all: the <c>session-requests</c>
    /// switch, read live.</summary>
    bool IsOffered { get; }

    /// <summary>The open request of each plan that has one, keyed by tag without its
    /// sigil and compared without regard to case. Never throws for an unreadable
    /// queue.</summary>
    Task<IReadOnlyDictionary<string, PlanSessionRequest>> OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Queues a request for the plan tagged <paramref name="planTag"/> in
    /// <paramref name="repository"/>, whose clone folder is
    /// <paramref name="cloneFolder"/>; or answers the one already open for that plan.</summary>
    Task<PlanSessionRequest> RequestAsync(
        string planTag,
        string repository,
        string cloneFolder,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels the request with <paramref name="requestId"/>; answers whether
    /// it was cancelled.</summary>
    Task<bool> CancelAsync(Guid requestId, CancellationToken cancellationToken = default);
}
