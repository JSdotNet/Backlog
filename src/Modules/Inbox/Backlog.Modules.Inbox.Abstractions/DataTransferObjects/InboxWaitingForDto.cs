namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>
/// What the Waiting for decision (<c>w</c>, local ADR 0054 §4) gives every task
/// it makes: who the work waits on, free text, and an optional day to follow up.
/// The Tasks side writes them as the entry's <c>waiting:</c> and
/// <c>followup:</c> tokens (<c>.devbook/domain/tasks/domain.md#waiting-for</c>).
/// </summary>
/// <param name="Who">Who the tasks wait on. Required: a blank one, or one holding
/// a backtick — which would end the token it is written in — is refused before
/// anything is routed.</param>
/// <param name="FollowUpOn">The day to chase them, or null for no date.</param>
public sealed record InboxWaitingForDto(string Who, DateOnly? FollowUpOn = null)
{
    /// <summary>Whether <see cref="Who"/> can be written: not blank, and with no
    /// backtick in it.</summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(Who) && !Who.Contains('`', StringComparison.Ordinal);
}
