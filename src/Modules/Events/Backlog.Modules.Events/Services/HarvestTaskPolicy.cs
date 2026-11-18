using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Modules.Events.Services;

/// <summary>
/// The Harvest Task Policy (<c>.devbook/domain/events/domain.md#harvest-task-policy</c>): files
/// one task <c>Harvest &lt;event title&gt;</c> under the Brain scope the day after a Going event
/// ends, and completes it when the event is marked Harvested. Its rules are
/// <c>domain.invariants.md#harvest-task-policy</c>, enforced at <see cref="FileDueAsync"/> and
/// <see cref="OnHarvestedAsync"/>.
/// <para>
/// A service and not a rule of the event, because it spans the event and a task in Tasks. It
/// never changes an event's state, and it raises no notification: the task is the reminder.
/// Scoped, for the task target it holds.
/// </para>
/// </summary>
internal sealed class HarvestTaskPolicy(
    IKnowledgeEventRepository events,
    IEventTaskTarget tasks,
    ILogger<HarvestTaskPolicy>? log = null)
{
    private const string TitlePrefix = "Harvest ";

    private readonly ILogger _log = log ?? NullLogger<HarvestTaskPolicy>.Instance;

    /// <summary>
    /// Files the harvest task of every event due on <paramref name="today"/>, the desktop's local
    /// date, and links it to the event as its harvest task; answers how many it filed. An event
    /// already holding a harvest task gets none, so a second check the same day, or the next,
    /// files nothing. A filing that is refused, or throws, links nothing, and the other events
    /// still file.
    /// </summary>
    public async Task<int> FileDueAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        var filed = 0;
        foreach (var due in (await events.ListAsync(cancellationToken).ConfigureAwait(false)).Where(e => IsDue(e, today)))
        {
            if (due.Tasks.Any(task => task.Purpose == TaskPurpose.Harvest)) continue;

            try
            {
                if (await FileOneAsync(due, today, cancellationToken).ConfigureAwait(false)) filed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // One event's fault is no reason for the next to wait a day.
                _log.LogError(ex, "Filing the harvest task of event {EventId} threw; the next check tries again.", due.Id);
            }
        }

        return filed;
    }

    /// <summary>
    /// Completes the task the Harvest link of <paramref name="harvested"/> names, and no other: a
    /// prep or linked task is never touched. Whether that task is still open is the task
    /// target's to decide — one already Done or Archived is left as it is. Best effort: the move
    /// into Harvested is saved before this runs, so a completion Tasks refuses, or one that
    /// throws, leaves the event Harvested; the answer says whether it held.
    /// </summary>
    public async Task<Result> OnHarvestedAsync(KnowledgeEvent harvested, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(harvested);

        if (harvested.Tasks.FirstOrDefault(task => task.Purpose == TaskPurpose.Harvest) is not { } link) return Result.Success();

        try
        {
            return await tasks.CompleteAsync(link.TaskId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _log.LogError(ex, "Completing harvest task {TaskId} of event {EventId} threw; the event stays Harvested.", link.TaskId, harvested.Id);
            return Result.Failure(CompletionThrew);
        }
    }

    private static readonly Error CompletionThrew =
        Error.Unexpected("harvest_task.completion_failed", "The harvest task could not be completed.");

    /// <summary>
    /// Files the harvest task of <paramref name="due"/>, then links it on the event as it stands
    /// now rather than the copy the pass listed, so a change the person made in the pane while
    /// the task filed is kept. File, then link: when the event is gone, already holds a harvest
    /// task, or is no longer due by then, the filed task is left unlinked, as it is when the save
    /// after the filing fails — accepted in the first version.
    /// </summary>
    private async Task<bool> FileOneAsync(KnowledgeEvent due, DateOnly today, CancellationToken cancellationToken)
    {
        var task = await tasks.FileAsync(TitlePrefix + due.Title, cancellationToken).ConfigureAwait(false);
        if (task.IsFailure) return false;

        if (await events.GetAsync(due.Id, cancellationToken).ConfigureAwait(false) is not { } current
            || !IsDue(current, today)
            || current.LinkHarvestTask(task.Value).IsFailure)
        {
            _log.LogWarning("Harvest task {TaskId} was filed but event {EventId} changed meanwhile, so it was not linked.", task.Value, due.Id);
            return false;
        }

        await events.SaveAsync(current, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Going, and its last day — the end, else the start — is before
    /// <paramref name="today"/>. An event with no dates is never due.</summary>
    private static bool IsDue(KnowledgeEvent e, DateOnly today) =>
        e.State == KnowledgeEventState.Going && (e.End ?? e.Start) is { } last && last.Date < today;
}
