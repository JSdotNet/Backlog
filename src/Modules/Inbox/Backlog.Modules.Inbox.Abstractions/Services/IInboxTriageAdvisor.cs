using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>
/// Whatever advises triage — the AI cards over an item opened in triage, and
/// the AI triage pass over a slice (local ADR 0023). Today an adapter over the
/// Azure Foundry deployment <see cref="IInboxPlanDrafter"/> already uses; in
/// the desktop harness a deterministic stand-in.
/// <para>
/// Shaped like <see cref="IInboxPlanDrafter"/> and for its reasons:
/// availability is a property, because every AI surface is hidden — not
/// disabled — while it is false (ADR 0023 §4); and a host with no advisor
/// registers nothing, so the handlers take this port as optional and answer
/// <c>inbox.triage.not_configured</c> in its absence.
/// </para>
/// <para>
/// What the advisor answers is a proposal, not a decision. The handlers hold
/// the answer to the context it was given — an id the context does not hold, a
/// repository Settings does not list, a list that does not exist is dropped —
/// so an adapter need only translate.
/// </para>
/// </summary>
public interface IInboxTriageAdvisor
{
    bool IsAvailable { get; }

    /// <summary>The two cards for one item: at most one duplicate, at most one
    /// plan grouping, and the repositories it would go to. Transport and model
    /// failures come back as <c>inbox.triage.failed</c> rather than thrown.</summary>
    Task<Result<InboxTriageAdviceDto>> AdviseAsync(
        InboxTriageItemDto item,
        InboxTriageContextDto context,
        CancellationToken cancellationToken = default);

    /// <summary>A proposed decision for every item of <paramref name="items"/>
    /// it can place — plans, duplicate pairs, single routes, list filings,
    /// archives — and the ids of the ones it cannot. Failures as for
    /// <see cref="AdviseAsync"/>.</summary>
    Task<Result<InboxTriagePassDto>> ProposePassAsync(
        IReadOnlyList<InboxTriageItemDto> items,
        InboxTriageContextDto context,
        CancellationToken cancellationToken = default);
}
