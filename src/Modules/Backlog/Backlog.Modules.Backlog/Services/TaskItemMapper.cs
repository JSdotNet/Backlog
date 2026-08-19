using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.Modules.Backlog.DomainModels;

namespace Backlog.Modules.Backlog.Services;

/// <summary>
/// Turns an aggregate into the shape callers are allowed to hold. One place, so
/// a field added to the entry is a deliberate decision to publish it rather than
/// something that leaks out of whichever handler happened to be edited.
/// </summary>
internal static class TaskItemMapper
{
    public static TaskItemDto ToDto(this TaskItem entry) => new(
        entry.Id,
        entry.Title,
        entry.ContentMd,
        entry.Type,
        entry.Priority,
        entry.Status,
        entry.Area,
        [.. entry.Tags],
        entry.Order,
        entry.TotalSubItemCount,
        entry.CompletedSubItemCount,
        [.. entry.ProjectionRefs.Select(p => new EntryProjectionDto(p.RepoId, p.ExternalId, p.TargetType))],
        entry.DueOn,
        entry.RemindAt,
        entry.Recurrence,
        entry.InMyDayOn,
        [.. entry.DependsOn],
        entry.View);
}
