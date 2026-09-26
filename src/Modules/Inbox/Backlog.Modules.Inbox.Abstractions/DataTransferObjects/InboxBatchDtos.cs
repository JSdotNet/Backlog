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
