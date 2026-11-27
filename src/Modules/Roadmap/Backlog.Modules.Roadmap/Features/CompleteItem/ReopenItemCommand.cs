using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.CompleteItem;

/// <summary>Opens an item a person completed by hand again ("Reopen").</summary>
public sealed record ReopenItemCommand(Guid ItemId);

public sealed class ReopenItemCommandHandler(IRoadmapPlanRepository plans, RoadmapPlanGate gate)
    : ICommandHandler<ReopenItemCommand, Result<RoadmapItemDto>>
{
    public async Task<Result<RoadmapItemDto>> Handle(
        ReopenItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var held = await gate.EnterAsync(cancellationToken);
        var plan = await plans.LoadAsync(cancellationToken);
        var reopened = plan.ReopenItem(command.ItemId);
        return reopened switch
        {
            Ok<RoadmapItem>(var item) => await SavedAsync(item),
            Error error => error,
        };

        async Task<Result<RoadmapItemDto>> SavedAsync(RoadmapItem item)
        {
            await plans.SaveAsync(plan, cancellationToken);
            return Result.Success(item.ToDto());
        }
    }
}
