using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>
/// Whatever writes an import plan about an inbox item — today an adapter over
/// Azure Foundry, in the harness a canned fixture.
/// <para>
/// Availability is a property rather than a failure code, because the pane
/// reads it on every render: "Create plan" stays on screen, disabled, with
/// <see cref="UnavailableReason"/> as its title, and a port that only said no
/// when asked would leave the button looking clickable.
/// </para>
/// <para>
/// A host that has no drafter registers nothing. The handlers take this port as
/// optional and answer <c>inbox.plan.not_configured</c> in its absence, so a
/// head composes without it and the feature reads as unavailable rather than
/// missing.
/// </para>
/// </summary>
public interface IInboxPlanDrafter
{
    bool IsAvailable { get; }

    /// <summary>Why <see cref="IsAvailable"/> is false, in words a person can
    /// act on; null when it is true.</summary>
    string? UnavailableReason { get; }

    /// <summary>Drafts the plan. Transport and model failures come back as
    /// <c>inbox.plan.failed</c> rather than thrown.</summary>
    Task<Result<InboxPlanDraftDto>> DraftAsync(InboxPlanDraftRequestDto request, CancellationToken cancellationToken = default);
}
