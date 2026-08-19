using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.AddDependency;

/// <summary>
/// Records that one thing on the plan has to land before another can. Either end
/// may be planned work or a milestone.
/// <para>
/// The plan is saved only when the edge is accepted. A refused dependency — a
/// cycle, an unknown id, something waiting for itself — leaves the stored plan
/// byte-for-byte as it was, which is what "the plan has been left as it was" in the
/// error message has to mean.
/// </para>
/// </summary>
/// <param name="NodeId">The thing that waits.</param>
/// <param name="DependsOnId">The thing it waits for.</param>
public sealed record AddDependencyCommand(Guid NodeId, Guid DependsOnId);

public sealed class AddDependencyCommandHandler(IRoadmapPlanRepository plans)
    : ICommandHandler<AddDependencyCommand, Result>
{
    public async Task<Result> Handle(AddDependencyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var plan = await plans.LoadAsync(cancellationToken);
        var added = plan.AddDependency(command.NodeId, command.DependsOnId);

        if (added.IsFailure) return added;

        await plans.SaveAsync(plan, cancellationToken);
        return Result.Success();
    }
}
