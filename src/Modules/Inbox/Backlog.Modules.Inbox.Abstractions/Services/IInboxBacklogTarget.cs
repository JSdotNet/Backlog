using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>
/// The Tasks context, as the Inbox is allowed to see it: a place that turns a
/// routed item into entries and says which entries it made.
/// <para>
/// A port declared here and answered in <c>src/Infrastructure</c>, never a
/// reference to Tasks. The Inbox is upstream of Tasks on the context map and
/// the boundary tests forbid one module's UI consuming another module's
/// published surface — so the join lives in an adapter that may see both, the
/// same arrangement the roadmap's cross-context joins already use. The adapter
/// is also the only thing that knows the entry text grammar; this port is
/// phrased in facts.
/// </para>
/// </summary>
public interface IInboxBacklogTarget
{
    /// <summary>One entry per repository in the request — or exactly one
    /// untargeted entry when it names none — each stamped with the item's id as
    /// its source. The ids come back in the same order as the repositories.
    /// A failure leaves the item unrouted; the entries created before the
    /// failure are named in the error so nothing is silently orphaned.</summary>
    Task<Result<IReadOnlyList<Guid>>> CreateTasksAsync(InboxRouteRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Hands a drafted plan to Tasks' import, stamping every entry it
    /// creates with <paramref name="sourceInboxId"/>. A plan that names a
    /// repository outside <paramref name="allowedRepoIds"/> — the item's own,
    /// compared without regard to case — is refused whole with
    /// <c>inbox.plan.unknown_repository</c> before Tasks sees it, because Tasks'
    /// import registers a repository it does not know and a drafter's guess is
    /// not a person's decision. Tasks' own import errors
    /// (<c>import.empty_plan</c>, <c>import.duplicate_item_id</c>) come back
    /// unchanged.</summary>
    Task<Result<IReadOnlyList<Guid>>> ImportPlanAsync(
        string planMarkdown,
        Guid sourceInboxId,
        IReadOnlyList<string> allowedRepoIds,
        CancellationToken cancellationToken = default);
}
