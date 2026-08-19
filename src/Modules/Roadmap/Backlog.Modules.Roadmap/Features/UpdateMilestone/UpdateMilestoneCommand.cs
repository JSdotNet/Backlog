using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.UpdateMilestone;

/// <summary>Writes an edited milestone back: every field, in one go, for the same
/// reason an item's edit does.</summary>
public sealed record UpdateMilestoneCommand(
    Guid MilestoneId,
    string Title,
    DateOnly On,
    MilestoneKind Kind,
    IReadOnlyList<string>? RepositoryAliases = null,
    string? Lane = null,
    bool IsPlanWide = false);

public sealed class UpdateMilestoneCommandHandler(IRoadmapPlanRepository plans)
    : ICommandHandler<UpdateMilestoneCommand, Result<RoadmapMilestoneDto>>
{
    public async Task<Result<RoadmapMilestoneDto>> Handle(
        UpdateMilestoneCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var plan = await plans.LoadAsync(cancellationToken);
        var updated = plan.UpdateMilestone(
            command.MilestoneId,
            command.Title,
            command.On,
            command.Kind,
            RepositoryScope.Of(command.RepositoryAliases),
            PlanningLane.Of(command.Lane),
            command.IsPlanWide);

        if (updated.IsFailure) return Result.Failure<RoadmapMilestoneDto>(updated.Error);

        await plans.SaveAsync(plan, cancellationToken);
        return Result.Success(updated.Value.ToDto());
    }
}
