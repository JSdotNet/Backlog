using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.AdviseTriage;

/// <summary>The AI cards for one item opened in triage. <paramref name="Repositories"/>
/// are the ones configured in Settings, <c>owner/name</c> — the module does not
/// read Settings, so the pane hands them over, as it does for a batch route.</summary>
public sealed record AdviseTriageQuery(Guid Id, IReadOnlyList<string>? Repositories = null);

/// <summary>
/// Asks the triage advisor about one item, with what it is compared against —
/// the open backlog tasks, the other unprocessed items, the repositories and
/// the lists (local ADR 0023 §1) — and holds the answer to that context, so a
/// card never names a task or an item the reader cannot find.
/// <para>
/// One call per ask: whether to ask again is the pane's decision, and it keeps
/// the answer for the app session (ADR 0023 §2). The advisor is optional, as
/// the plan drafter is for Create plan; its absence, or an advisor that says it
/// cannot run, answers <c>inbox.triage.not_configured</c> without reading
/// anything. Only an unprocessed item is advised on: a decided one has no
/// triage left to advise.
/// </para>
/// </summary>
public sealed class AdviseTriageQueryHandler(
    IInboxItemRepository items,
    IInboxOrganizerRepository organizer,
    IInboxTaskReferences? taskReferences = null,
    IInboxTriageAdvisor? advisor = null)
    : IQueryHandler<AdviseTriageQuery, Result<InboxTriageAdviceDto>>
{
    public async Task<Result<InboxTriageAdviceDto>> Handle(AdviseTriageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (advisor is not { IsAvailable: true }) return InboxErrors.TriageNotConfigured;

        var item = await items.GetAsync(query.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return InboxErrors.ItemNotFound;
        if (!InboxTriageContext.IsUnprocessed(item)) return InboxErrors.ItemAlreadyDecided;

        var all = await items.ListAsync(cancellationToken).ConfigureAwait(false);
        var context = await InboxTriageContext.ReadAsync(all, organizer, taskReferences, [item.Id], query.Repositories, cancellationToken).ConfigureAwait(false);
        var subject = InboxTriageItemDto.From(item.ToDto());

        var advice = await advisor.AdviseAsync(subject, context, cancellationToken).ConfigureAwait(false);
        if (advice.IsFailure) return advice.Error;

        return InboxTriageContext.Hold(advice.Value, subject, context);
    }
}
