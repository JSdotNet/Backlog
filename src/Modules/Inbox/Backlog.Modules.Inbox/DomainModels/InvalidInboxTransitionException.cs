using Backlog.Modules.Inbox.Abstractions;

namespace Backlog.Modules.Inbox.DomainModels;

/// <summary>
/// Thrown by <see cref="InboxItem"/> when a lifecycle step is not one
/// <c>.domain/inbox/flow.md</c> allows — archiving a routed item, routing one
/// twice. Handlers translate it to <c>inbox.item.invalid_transition</c>
/// (guideline ADR 0004 rule 3); nothing outside the module sees it.
/// </summary>
public sealed class InvalidInboxTransitionException : Exception
{
    public InvalidInboxTransitionException(InboxStatus from, string attempted)
        : base($"An item that is {from} cannot be {attempted}.")
    {
        From = from;
        Attempted = attempted;
    }

    public InboxStatus From { get; }

    /// <summary>What was asked for, in the past participle the message uses —
    /// "archived", "routed".</summary>
    public string Attempted { get; }
}
