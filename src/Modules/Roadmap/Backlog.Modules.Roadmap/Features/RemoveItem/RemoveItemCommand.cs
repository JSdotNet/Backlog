using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.RemoveItem;

/// <summary>Takes planned work off the plan, along with every dependency that
/// pointed at it.</summary>
public sealed record RemoveItemCommand(Guid ItemId);

public sealed class RemoveItemCommandHandler(IRoadmapPlanRepository plans)
    : ICommandHandler<RemoveItemCommand, Result>
{
    public async Task<Result> Handle(RemoveItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var plan = await plans.LoadAsync(cancellationToken);
        var removed = plan.RemoveItem(command.ItemId);

        if (removed.IsFailure) return removed;

        await plans.SaveAsync(plan, cancellationToken);
        return Result.Success();
    }
}
