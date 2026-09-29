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

    /// <summary>A batch of items as one plan import: the entries
    /// <see cref="CreateTasksAsync"/> would make for each item, all under the
    /// batch's shared plan tag and each stamped with its own item as its source.
    /// <para>
    /// Decided per item first, by the one side that knows the grammar: an item
    /// whose notes would split or merge the document, or that names a repository
    /// the workspace does not know, is left out and comes back refused with its
    /// reason. The rest go as one document, and that part is all or nothing —
    /// Tasks takes it whole or refuses it, and its refusal comes back as
    /// <see cref="InboxBatchTargetResultDto.DocumentRefused"/> with no entry
    /// made. With every item refused, Tasks is not asked at all.
    /// </para>
    /// <para>
    /// Imported items come back in the order of the request, each carrying only
    /// its own task ids in the order of its repositories. No repository is ever
    /// registered by a batch.
    /// </para></summary>
    Task<InboxBatchTargetResultDto> CreateBatchTasksAsync(
        InboxBatchRouteRequestDto request,
        CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Reads the order a drafter proposed for a batch: every <c>after:</c> in
    /// <paramref name="planMarkdown"/> as an edge between two of
    /// <paramref name="itemIds"/>, the entry's own <c>id:</c> being the item
    /// that waits. An id may carry a <c>/repo</c> suffix, the shape a batch
    /// writes for an item with several repositories.
    /// <para>
    /// Read, never imported: the edges go back to the person as proposals and
    /// the batch is routed as it always is. Refused whole — no edge at all —
    /// when the answer names a repository outside
    /// <paramref name="allowedRepoIds"/> (<c>inbox.order.unknown_repository</c>,
    /// the rule <see cref="ImportPlanAsync"/> applies to a drafted plan) or an
    /// entry that is not one of the items (<c>inbox.order.unknown_item</c>):
    /// an answer that made one thing up is not trusted for the rest.
    /// </para>
    /// </summary>
    Result<IReadOnlyList<InboxBatchEdge>> ReadDraftedOrder(
        string planMarkdown,
        IReadOnlyCollection<Guid> itemIds,
        IReadOnlyList<string> allowedRepoIds);
}
