using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Roadmap.Features.MoveRepositoryAliases;

/// <summary>Moves the plan's data under each relabelled repository's old alias to its
/// new one (local ADR 0060 §3), the whole mapping at once.</summary>
/// <param name="OldToNew">The mapping, applied at once.</param>
/// <param name="KeepStamp">Save at the stamp the stored plan already carries rather than
/// as a new version: a backstop move, which every device makes to its own copy.</param>
public sealed record MoveRepositoryAliasesCommand(IReadOnlyDictionary<string, string> OldToNew, bool KeepStamp = false);

/// <summary>Loads, moves and saves the plan, and answers whether anything moved. A plan
/// that holds none of the old aliases is not saved, so its stamp stays and nothing
/// travels.</summary>
public sealed class MoveRepositoryAliasesCommandHandler(IRoadmapPlanRepository plans, RoadmapPlanGate gate)
    : ICommandHandler<MoveRepositoryAliasesCommand, bool>
{
    public async Task<bool> Handle(MoveRepositoryAliasesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Held from load to save, so a pulled plan (which enters too) cannot land between
        // them, and a kept stamp is the stamp of the plan this move was made on.
        using var held = await gate.EnterAsync(cancellationToken);
        var plan = await plans.LoadAsync(cancellationToken);

        if (!plan.MoveRepositoryAliases(command.OldToNew)) return false;

        if (command.KeepStamp) await plans.SaveKeepingStampAsync(plan, cancellationToken);
        else await plans.SaveAsync(plan, cancellationToken);

        return true;
    }
}
