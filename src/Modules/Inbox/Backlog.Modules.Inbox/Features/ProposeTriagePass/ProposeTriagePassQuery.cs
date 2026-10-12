using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.ProposeTriagePass;

/// <summary>"Let AI propose the rest": a proposal for every unprocessed item
/// of <paramref name="Ids"/>, the slice being triaged. <paramref name="Repositories"/>
/// are the ones configured in Settings, as for <c>AdviseTriageQuery</c>.</summary>
public sealed record ProposeTriagePassQuery(IReadOnlyList<Guid> Ids, IReadOnlyList<string>? Repositories = null);

/// <summary>
/// Asks the triage advisor for the AI triage pass over a slice, and holds its
/// answer to the slice and the context (local ADR 0023): each item placed at
/// most once, every item the kept proposals do not place listed as unplaced.
/// Nothing is applied here — the pass is a proposal until the reader presses
/// Apply (ADR 0023 §5) — and nothing is written.
/// <para>
/// Ids that are unknown, or name an item already decided, are left out of the
/// pass rather than refusing it: the slice may have moved on while the reader
/// pressed. A slice with nothing left to propose for is answered with an empty
/// pass and no call. The advisor is optional, as for the cards.
/// </para>
/// </summary>
public sealed class ProposeTriagePassQueryHandler(
    IInboxItemRepository items,
    IInboxOrganizerRepository organizer,
    IInboxTaskReferences? taskReferences = null,
    IInboxTriageAdvisor? advisor = null)
    : IQueryHandler<ProposeTriagePassQuery, Result<InboxTriagePassDto>>
{
    public async Task<Result<InboxTriagePassDto>> Handle(ProposeTriagePassQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Ids);

        if (advisor is not { IsAvailable: true }) return InboxErrors.TriageNotConfigured;

        var all = await items.ListAsync(cancellationToken).ConfigureAwait(false);
        var byId = all.ToDictionary(item => item.Id);
        var batch = query.Ids
            .Distinct()
            .Select(id => byId.GetValueOrDefault(id))
            .Where(item => item is not null && InboxTriageContext.IsUnprocessed(item))
            .Select(item => InboxTriageItemDto.From(item!.ToDto()))
            .ToList();

        if (batch.Count == 0) return InboxTriagePassDto.Nothing;

        var context = await InboxTriageContext.ReadAsync(
            all, organizer, taskReferences, [.. batch.Select(item => item.Id)], query.Repositories, cancellationToken).ConfigureAwait(false);

        var pass = await advisor.ProposePassAsync(batch, context, cancellationToken).ConfigureAwait(false);
        if (pass.IsFailure) return pass.Error;

        return InboxTriageContext.Hold(pass.Value, batch, context);
    }
}
