using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.UpdateItem;

/// <summary>
/// Writes an edited item back: everything a form holds, in one go.
/// <para>
/// Every field is sent every time, including the ones the person did not touch. An
/// editor that omitted unchanged fields would make "left alone" and "cleared"
/// indistinguishable on the wire, and the field that would lose is whichever one
/// somebody had just emptied on purpose.
/// </para>
/// </summary>
public sealed record UpdateItemCommand(
    Guid ItemId,
    string Title,
    DateOnly Start,
    DateOnly End,
    PlanningPriority Priority,
    IReadOnlyList<string>? RepositoryAliases = null,
    string? Lane = null,
    Guid? BacklogEntryId = null,
    string? Notes = null,
    string? Tag = null,
    IReadOnlyList<string>? KnowledgeRefs = null);

public sealed class UpdateItemCommandHandler(IRoadmapPlanRepository plans)
    : ICommandHandler<UpdateItemCommand, Result<RoadmapItemDto>>
{
    public async Task<Result<RoadmapItemDto>> Handle(
        UpdateItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var window = PlannedWindow.Create(command.Start, command.End);
        if (window.IsFailure) return Result.Failure<RoadmapItemDto>(window.Error);

        var plan = await plans.LoadAsync(cancellationToken);
        var updated = plan.UpdateItem(
            command.ItemId,
            command.Title,
            window.Value,
            command.Priority,
            RepositoryScope.Of(command.RepositoryAliases),
            PlanningLane.Of(command.Lane),
            command.BacklogEntryId,
            command.Notes,
            // A cleared tag means "derive one from the title"; the plan does that.
            string.IsNullOrWhiteSpace(command.Tag) ? null : PlanningTag.Of(command.Tag),
            KnowledgeReferences.Of(command.KnowledgeRefs));

        if (updated.IsFailure) return Result.Failure<RoadmapItemDto>(updated.Error);

        await plans.SaveAsync(plan, cancellationToken);
        return Result.Success(updated.Value.ToDto());
    }
}
