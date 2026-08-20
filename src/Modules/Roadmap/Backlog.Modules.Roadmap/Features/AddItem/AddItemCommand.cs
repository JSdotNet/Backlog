using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.AddItem;

/// <summary>
/// Puts a piece of planned work on the plan.
/// <para>
/// A backlog entry is not required, and that is the point of this context existing:
/// most planning happens before anything has been refined into an entry.
/// </para>
/// </summary>
/// <param name="Start">First day, inclusive.</param>
/// <param name="End">Last day, inclusive.</param>
public sealed record AddItemCommand(
    string Title,
    DateOnly Start,
    DateOnly End,
    PlanningPriority Priority = PlanningPriority.Medium,
    IReadOnlyList<string>? RepositoryAliases = null,
    string? Lane = null,
    Guid? BacklogEntryId = null,
    string? Notes = null,
    string? Tag = null,
    IReadOnlyList<string>? KnowledgeRefs = null);

public sealed class AddItemCommandHandler(IRoadmapPlanRepository plans)
    : ICommandHandler<AddItemCommand, Result<RoadmapItemDto>>
{
    public async Task<Result<RoadmapItemDto>> Handle(
        AddItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var window = PlannedWindow.Create(command.Start, command.End);
        if (window.IsFailure) return Result.Failure<RoadmapItemDto>(window.Error);

        var plan = await plans.LoadAsync(cancellationToken);
        var added = plan.AddItem(
            command.Title,
            window.Value,
            command.Priority,
            RepositoryScope.Of(command.RepositoryAliases),
            PlanningLane.Of(command.Lane),
            command.BacklogEntryId,
            command.Notes,
            // No tag means "derive one from the title"; the plan does that for itself.
            string.IsNullOrWhiteSpace(command.Tag) ? null : PlanningTag.Of(command.Tag),
            KnowledgeReferences.Of(command.KnowledgeRefs));

        if (added.IsFailure) return Result.Failure<RoadmapItemDto>(added.Error);

        await plans.SaveAsync(plan, cancellationToken);
        return Result.Success(added.Value.ToDto());
    }
}
