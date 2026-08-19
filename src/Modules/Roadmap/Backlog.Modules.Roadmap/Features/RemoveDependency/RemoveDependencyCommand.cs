using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.RemoveDependency;

/// <summary>Takes a dependency back out of the plan.</summary>
public sealed record RemoveDependencyCommand(Guid NodeId, Guid DependsOnId);

public sealed class RemoveDependencyCommandHandler(IRoadmapPlanRepository plans)
    : ICommandHandler<RemoveDependencyCommand, Result>
{
    public async Task<Result> Handle(RemoveDependencyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var plan = await plans.LoadAsync(cancellationToken);
        var removed = plan.RemoveDependency(command.NodeId, command.DependsOnId);

        if (removed.IsFailure) return removed;

        await plans.SaveAsync(plan, cancellationToken);
        return Result.Success();
    }
}
