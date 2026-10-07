using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Roadmap.Features.KeepUpWithWork;

/// <summary>
/// Lays every item the import sized by its effort out again from its unfinished work, and
/// stores each window that moved (ADR 0013, ruling 5 as amended on 2026-09-27 and
/// 2026-10-07).
/// <para>
/// Asked for where the roadmap reads the plan — when it opens and when a task changes —
/// never on a pace change or a plan pulled from another PC: those redraw through the same
/// rule (<see cref="RoadmapProjection"/>) and store nothing, so a device does not save the
/// other PC's plan back at it (local ADR 0018).
/// </para>
/// </summary>
/// <param name="Today">The day open work is placed from — the caller's, so what is stored
/// and what the caller draws are counted from the same day.</param>
public sealed record KeepUpWithWorkCommand(DateOnly Today);

/// <summary>
/// Projects the plan and writes only the windows that moved, through the same path as any
/// re-placement by the import (<see cref="RoadmapPlan.PlaceByImport"/>), so each stays
/// effort-placed.
/// <para>
/// Idempotent within a day: a run that moves nothing saves nothing, because a save that
/// changes nothing is still a write the other devices would sync (local ADR 0018, "The
/// daily re-projection counts as a save"). What it did is answered in the
/// <c>RoadmapItemScheduled</c> shape, each carrying the window it replaced.
/// </para>
/// </summary>
/// <param name="catchUp">Where the host replicates, the pull the read waits for, as
/// <see cref="Features.GetPlan.GetPlanQueryHandler"/> waits for it: a window stored over
/// a plan this device had not pulled yet would overwrite the other PC's edit. Awaited
/// before <paramref name="gate"/> is entered, so a slow pull holds up no other writer.</param>
/// <param name="gate">Held from the load to the save: the task write that starts this run
/// is often an import's or an agent's, and their plan write follows it while this run is
/// still gathering.</param>
public sealed class KeepUpWithWorkCommandHandler(
    IRoadmapPlanRepository plans,
    IPlanningVelocity velocity,
    IRoadmapItemRollup rollups,
    RoadmapPlanGate gate,
    IRoadmapCatchUp? catchUp = null) : ICommandHandler<KeepUpWithWorkCommand, IReadOnlyList<RoadmapItemScheduledDto>>
{
    public async Task<IReadOnlyList<RoadmapItemScheduledDto>> Handle(
        KeepUpWithWorkCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (catchUp is not null) await catchUp.CatchUpAsync(cancellationToken);

        using var held = await gate.EnterAsync(cancellationToken);
        var plan = await plans.LoadAsync(cancellationToken);
        var all = plan.ToDto();

        // Only an item the projection may move needs its work read: every other item
        // floors its successors by its stored end, which needs no gathering. A plan with
        // none reads neither the backlog nor the paces.
        List<RoadmapItemDto> keepingUp = [.. all.Items.Where(RoadmapProjection.KeepsUp)];
        if (keepingUp.Count == 0) return [];

        var gathered = await rollups.GatherPlanAsync(all with { Items = keepingUp }, cancellationToken);
        var paces = await velocity.ReadPacesInUseAsync(cancellationToken);

        var projected = RoadmapProjection.Project(all.Items, gathered, paces, command.Today, all.Milestones);

        var scheduled = new List<RoadmapItemScheduledDto>();
        foreach (var (stored, read) in all.Items.Zip(projected))
        {
            if (read.Start == stored.Start && read.End == stored.End) continue;

            var item = plan.Items.First(candidate => candidate.Id == stored.Id);
            var previous = item.Window;

            // Refused only for a window a person placed, which KeepsUp already left out.
            var placed = plan.PlaceByImport(item.Id, PlannedWindow.Of(read.Start, read.End), ImportPlacement.Effort);
            if (placed.IsFailure) continue;

            scheduled.Add(placed.Value.Scheduled(previous));
        }

        if (scheduled.Count > 0) await plans.SaveAsync(plan, cancellationToken);
        return scheduled;
    }
}
