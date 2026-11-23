using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Features.SetTaskDue;

/// <summary>
/// Sets the day a task is due on, or clears it for null, and nothing else (local ADR 0056
/// §2).
/// <para>
/// The Due row writes the same field by rewriting the metadata line and saving the text.
/// A caller that holds no text — the Calendar, moving a dropped chip to its day — sets it
/// on the aggregate instead: the metadata line is rebuilt from the fields, so the next
/// text read carries the new day, and no other field passes through a parse on the way.
/// </para>
/// </summary>
public sealed record SetTaskDueCommand(Guid Id, DateOnly? DueOn);

public sealed class SetTaskDueCommandHandler(ITaskRepository entries)
    : ICommandHandler<SetTaskDueCommand, Result<TaskItemDto>>
{
    public static readonly Error NotFound = Error.NotFound(
        "entry.not_found",
        "That entry no longer exists.");

    public async Task<Result<TaskItemDto>> Handle(
        SetTaskDueCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var entry = await entries.GetAsync(command.Id, cancellationToken);
        if (entry is null) return NotFound;

        entry.SetDueOn(command.DueOn);

        await entries.SaveAsync(entry, cancellationToken);
        return entry.ToDto();
    }
}
