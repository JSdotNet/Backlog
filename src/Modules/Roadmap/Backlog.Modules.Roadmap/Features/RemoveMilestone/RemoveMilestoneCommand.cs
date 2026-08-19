using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.RemoveMilestone;

/// <summary>Takes a date off the plan, along with every dependency that waited on
/// it.</summary>
public sealed record RemoveMilestoneCommand(Guid MilestoneId);

public sealed class RemoveMilestoneCommandHandler(IRoadmapPlanRepository plans)
    : ICommandHandler<RemoveMilestoneCommand, Result>
{
    public async Task<Result> Handle(RemoveMilestoneCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var plan = await plans.LoadAsync(cancellationToken);
        var removed = plan.RemoveMilestone(command.MilestoneId);

        if (removed.IsFailure) return removed;

        await plans.SaveAsync(plan, cancellationToken);
        return Result.Success();
    }
}
