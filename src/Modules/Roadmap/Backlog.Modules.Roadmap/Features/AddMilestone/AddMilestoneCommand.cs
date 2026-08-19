using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.AddMilestone;

/// <summary>Puts a fixed point on the plan — a release, a freeze, a review, a
/// commitment. One day, no duration.</summary>
public sealed record AddMilestoneCommand(
    string Title,
    DateOnly On,
    MilestoneKind Kind = MilestoneKind.Release,
    IReadOnlyList<string>? RepositoryAliases = null,
    string? Lane = null,
    bool IsPlanWide = false);

public sealed class AddMilestoneCommandHandler(IRoadmapPlanRepository plans)
    : ICommandHandler<AddMilestoneCommand, Result<RoadmapMilestoneDto>>
{
    public async Task<Result<RoadmapMilestoneDto>> Handle(
        AddMilestoneCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var plan = await plans.LoadAsync(cancellationToken);
        var added = plan.AddMilestone(
            command.Title,
            command.On,
            command.Kind,
            RepositoryScope.Of(command.RepositoryAliases),
            PlanningLane.Of(command.Lane),
            command.IsPlanWide);

        if (added.IsFailure) return Result.Failure<RoadmapMilestoneDto>(added.Error);

        await plans.SaveAsync(plan, cancellationToken);
        return Result.Success(added.Value.ToDto());
    }
}
