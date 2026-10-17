using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.DomainModels;
using Backlog.SharedKernel;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The <c>.devbook/domain/progress/domain.md#blocked-task-policy</c>: a task marked blocked,
/// by its <c>blocked:</c> token or a <c>set_blocked</c> call, raises a New impediment titled
/// with the task's title that holds the task back — unless one already tracks that block.
/// <para>
/// <b>Stateless, decided from what is stored.</b> Tasks' change signal names no task, so
/// the policy reads every blocked task on each run and asks of each whether its block is
/// tracked: an impediment holds it back that is open, or that was raised on or after the
/// day the task was marked blocked. The second half is what keeps a person's Resolve from
/// being undone by the next task write while the task is still marked, and what makes a
/// task marked blocked again later raise a new one
/// (<c>.devbook/domain/progress/requirements.md#requirement-a-blocked-task-raises-one-new-impediment</c>).
/// Clearing the mark is never read, so it resolves nothing.
/// </para>
/// <para>
/// Under one hold of the <see cref="ProgressGate"/> from read to save
/// (<see cref="ImpedimentChanges.RaiseAllAsync"/>), so two runs that overlap raise one.
/// </para>
/// </summary>
public sealed class BlockedTaskPolicy(
    IBlockedTaskSource tasks,
    ImpedimentChanges changes,
    TimeProvider clock,
    IAppFeatureSettings? features = null)
    : IBlockedTaskPolicy
{
    public async Task<IReadOnlyList<ImpedimentDto>> ReactAsync(CancellationToken cancellationToken = default)
    {
        if (!(features?.IsEnabled(ProgressFeatures.Goals) ?? false)) return [];

        var blocked = await tasks.ReadBlockedAsync(cancellationToken).ConfigureAwait(false);
        if (blocked.Count == 0) return [];

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var now = clock.GetUtcNow();

        return await changes
            .RaiseAllAsync(all => Decide(all, blocked, today, now), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The new impediments the blocked tasks need, one per task whose block no
    /// impediment in <paramref name="all"/> tracks.</summary>
    internal static IReadOnlyList<Impediment> Decide(
        IReadOnlyList<Impediment> all,
        IReadOnlyList<BlockedTaskDto> blocked,
        DateOnly today,
        DateTimeOffset now)
    {
        var raised = new List<Impediment>();

        foreach (var task in blocked.DistinctBy(task => task.Id))
        {
            if (task.Id == Guid.Empty || Tracks(all, task)) continue;

            var impediment = Impediment.Raise(Guid.NewGuid(), Title(task), today, now).Value;
            impediment.HoldBack(task.Id);
            raised.Add(impediment);
        }

        return raised;
    }

    /// <summary>Whether an impediment tracks <paramref name="task"/>'s block: it holds the
    /// task back and is open, or was raised on or after the day the task was marked.</summary>
    internal static bool Tracks(IReadOnlyList<Impediment> all, BlockedTaskDto task) =>
        all.Any(impediment => impediment.Holds(task.Id) && (impediment.IsOpen || impediment.RaisedOn >= task.BlockedSince));

    private static string Title(BlockedTaskDto task) =>
        string.IsNullOrWhiteSpace(task.Title) ? "Blocked task" : task.Title;
}
