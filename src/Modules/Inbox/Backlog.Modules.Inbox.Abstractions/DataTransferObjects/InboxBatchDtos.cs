using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>
/// What one act run across several items did: the items it changed, and the
/// ones it could not, each with the reason.
/// <para>
/// Two lists rather than a <see cref="Result"/>, because a batch has no single
/// answer. Each item is its own command and its own save, so a refusal on the
/// third item neither stops the fourth nor undoes the first two — there is no
/// transaction over N saves, and pretending otherwise would be worse than
/// reporting the truth (guideline 0004).
/// </para>
/// </summary>
/// <param name="Changed">The items the command accepted, in the order asked.</param>
/// <param name="Failed">The items it refused, in the order asked.</param>
public sealed record InboxBatchResultDto(
    IReadOnlyList<Guid> Changed,
    IReadOnlyList<InboxBatchFailureDto> Failed)
{
    /// <summary>Asked about nothing, so nothing happened.</summary>
    public static readonly InboxBatchResultDto Nothing = new([], []);
}

/// <summary>One item a batch could not change, and the error its own command
/// answered with.</summary>
public sealed record InboxBatchFailureDto(Guid Id, Error Error);

/// <summary>
/// Several items routed together as one plan import — a <em>batch</em>, the
/// Inbox's word for a set routed at once. The same facts as routing each on its
/// own, plus the one tag every entry of the batch shares.
/// </summary>
/// <param name="Items">The items to route, each with its own repositories, in
/// the order asked.</param>
/// <param name="PlanTag">The plan tag the batch's entries share, sigil included
/// (<c>+reading-1a2b3c4d</c>): Tasks' <c>import_plan_id</c> for the batch, and
/// new for every batch so a later one never clears an earlier one's entries.</param>
public sealed record InboxBatchRouteRequestDto(
    IReadOnlyList<InboxRouteRequestDto> Items,
    string PlanTag);

/// <summary>
/// What Tasks' side made of a batch, item by item. The items the adapter could
/// not put into the batch's document — notes that would split it, a repository
/// the workspace does not know — are left out and named in
/// <paramref name="Refused"/> with the reason; the rest were imported together.
/// </summary>
/// <param name="Routed">The items imported, in the order of the request, each
/// with only its own task ids in the order of its repositories.</param>
/// <param name="Refused">The items left out of the document, or whose entries
/// did not all come back, each with its reason, in the order of the request.</param>
/// <param name="DocumentRefused">Tasks' own refusal of the document, when it
/// gave one. Tasks takes a document whole or not at all, so
/// <paramref name="Routed"/> is then empty and nothing sent was written. Null
/// when the import went through, or when no item was left to send.</param>
public sealed record InboxBatchTargetResultDto(
    IReadOnlyList<InboxRoutedDto> Routed,
    IReadOnlyList<InboxBatchFailureDto> Refused,
    Error? DocumentRefused = null);

/// <summary>
/// What routing a batch did. Unlike the other acts across a selection it is one
/// import rather than one command per item, so it has a whole-batch outcome:
/// every routable item went, each with only its own entries, or none did and
/// each is named in <paramref name="Failed"/> with the reason the batch was
/// refused. Items refused before the import — gone, already routed, archived —
/// are named there either way.
/// </summary>
/// <param name="PlanTag">The tag the batch's entries share, sigil included.</param>
/// <param name="Routed">The items routed, in the order asked, each with its own
/// task ids.</param>
/// <param name="Failed">The items not routed, in the order asked.</param>
public sealed record InboxBatchRoutedDto(
    string PlanTag,
    IReadOnlyList<InboxRoutedDto> Routed,
    IReadOnlyList<InboxBatchFailureDto> Failed);
