using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The one place a saved goal is announced: every command that saves or deletes a goal raises it once the gate is let go,
/// and a pulled copy raises it too. <see cref="GoalReplication"/> forwards a person's
/// change to the sync loop, and <see cref="Abstractions.Services.IGoals.Changed"/>, the
/// published notice every goal screen re-reads on, is held on it. A singleton, because the commands run in scopes of their own.
/// </summary>
public sealed class GoalSaved
{
    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}

/// <summary>From the aggregate to what a caller receives, and back for an item.</summary>
public static class GoalMapper
{
    public static GoalDto ToDto(this Goal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);

        return new GoalDto(
            goal.Id,
            goal.Title,
            goal.Outcome,
            goal.DueDate,
            [.. goal.Attachments.Select(attachment => new GoalAttachmentDto(attachment.ToItem(), attachment.AttachedOn))]);
    }

    public static GoalItemDto ToItem(this GoalAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return new GoalItemDto(attachment.Kind, attachment.Plan, attachment.TaskId, attachment.Repository, attachment.Number);
    }

    public static GoalProgressDto ToDto(this GoalProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return new GoalProgressDto(progress.Done, progress.Total, progress.Doing, progress.Blocked, progress.ToDo, progress.DoneInWindow);
    }
}
