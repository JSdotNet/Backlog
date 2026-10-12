using System.Globalization;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Features.SetPlannedHours;

/// <summary>
/// Sets the hours the person has set aside for a task on given days, replacing the
/// whole list — an empty one clears it.
/// <para>
/// A use case of its own rather than a token in the text, for the reason the Devbook
/// references have one: the blocks are the task's own field, which the text save
/// neither writes nor clears. They are the person's plan for the Calendar, and no
/// roadmap rule reads them.
/// </para>
/// </summary>
public sealed record SetPlannedHoursCommand(Guid Id, IReadOnlyList<PlannedHoursDto> Blocks);

public sealed class SetPlannedHoursCommandHandler(ITaskRepository entries)
    : ICommandHandler<SetPlannedHoursCommand, Result<TaskItemDto>>
{
    /// <summary>The code a refused figure fails with. Displayed rather than branched
    /// on, so it is not one of <c>TaskEntryErrorCodes</c>.</summary>
    public const string InvalidHoursCode = "entry.invalid_planned_hours";

    public static readonly Error NotFound = Error.NotFound(
        "entry.not_found",
        "That entry no longer exists.");

    public async Task<Result<TaskItemDto>> Handle(
        SetPlannedHoursCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Read before the entry is loaded, so a figure the aggregate would refuse
        // fails as a value naming what was wrong rather than as its exception.
        var blocks = new List<PlannedHoursBlock>();
        foreach (var block in command.Blocks ?? [])
        {
            if (PlannedHoursBlock.TryCreate(block.On, block.Hours) is not { } made) return InvalidHours(block);
            blocks.Add(made);
        }

        var entry = await entries.GetAsync(command.Id, cancellationToken);
        if (entry is null) return NotFound;

        entry.SetPlannedHours(blocks);

        await entries.SaveAsync(entry, cancellationToken);
        return entry.ToDto();
    }

    private static Error InvalidHours(PlannedHoursDto block) => Error.Validation(
        InvalidHoursCode,
        string.Create(
            CultureInfo.CurrentCulture,
            $"{block.Hours}h on {block.On:d} is not a number of hours to plan — give more than 0 and at most {PlannedHoursBlock.MaxHours}."));
}
