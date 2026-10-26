namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// The plan filter's queue icon port (local ADR 0037 §2): queue a session that runs a
/// whole plan, named by the tag the filter bar has pressed, read which plans have one
/// waiting, cancel one — and learn which repositories the plan is filed under, which
/// only the roadmap knows.
/// <para>
/// A port on Tasks' own surface rather than a reference to Roadmap Planning, the shape
/// <see cref="IRoadmapTagSource"/> and <see cref="IEntrySessionRequests"/> take: a screen
/// asks its own module, and an infrastructure adapter that may see both contexts answers
/// it from the roadmap's plan and its plan session queue. The requests come back as
/// <see cref="EntrySessionRequest"/>, Tasks' own copy of an open request, because the
/// queue's three open states are the same whatever the request names.
/// </para>
/// <para>
/// A host that composed no queue answers that the action is not offered, and the filter
/// bar draws its plan chip with its copy alone.
/// </para>
/// </summary>
public interface IPlanTagSessionRequests
{
    /// <summary>Raised after the roadmap's plan changed, so the repositories a plan is
    /// filed under may have too. It may arrive on any thread.</summary>
    event Action? PlanChanged;

    /// <summary>Whether the action is offered at all: a queue is composed and the
    /// <c>session-requests</c> switch is on, read live.</summary>
    bool IsOffered { get; }

    /// <summary>The repository aliases each tagged roadmap item is filed under, in the
    /// order the item names them, keyed by its tag without the plan sigil and compared
    /// without regard to case. Where two items carry one tag, the first in the plan's
    /// order answers. A plan no roadmap item carries is not in the answer.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> RepositoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>The open request of each plan that has one, keyed by tag without its
    /// sigil and compared without regard to case. Never throws for an unreadable
    /// queue.</summary>
    Task<IReadOnlyDictionary<string, EntrySessionRequest>> OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Queues a request for the plan tagged <paramref name="planTag"/> (no
    /// sigil) in <paramref name="repository"/>, whose clone folder is
    /// <paramref name="cloneFolder"/>; or answers the one already open for that plan.</summary>
    Task<EntrySessionRequest> RequestAsync(
        string planTag,
        string repository,
        string cloneFolder,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels the request with <paramref name="requestId"/>; answers whether
    /// it was cancelled — false once a dispatcher has started its session.</summary>
    Task<bool> CancelAsync(Guid requestId, CancellationToken cancellationToken = default);
}
