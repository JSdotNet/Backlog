namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>
/// What a single Move to backlog may also set on every task it makes (local
/// ADR 0054): a <see cref="DueOn"/> date, and whether the tasks go into My Day.
/// My Day is a choice about today, so it is asked for as a yes and the module
/// dates it with its own clock rather than taking a date from the screen. A
/// batch route takes neither.
/// </summary>
/// <param name="DueOn">The due date to give every task, or null for none.</param>
/// <param name="AddToMyDay">Whether every task is picked for today's My Day.</param>
public sealed record InboxRouteScheduleDto(DateOnly? DueOn = null, bool AddToMyDay = false)
{
    /// <summary>Whether the schedule sets nothing, so the route is the plain one.</summary>
    public bool IsEmpty => DueOn is null && !AddToMyDay;
}
