using System.Security.Cryptography;
using System.Text;

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
/// <b>Once per event is kept by the task's id.</b> The harvest task is filed under an id derived
/// from the event (<see cref="HarvestTaskId"/>) and only when Tasks holds no task with it, live
/// or deleted, so two desktops that both check the morning after file the same task, both their
/// copies of the event link it, and the replicas fold the two into one. The id is a one-way hash,
/// so nothing in Tasks names the event (local ADR 0050 §3).
/// </para>
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

    /// <summary>Prefixed onto the event's id before it is hashed, so the harvest task's id cannot
    /// collide with a hash any other rule takes of the same id.</summary>
    private const string Namespace = "backlog:harvest-task:";

    private readonly ILogger _log = log ?? NullLogger<HarvestTaskPolicy>.Instance;

    /// <summary>
    /// The id of the harvest task of <paramref name="eventId"/>. The same event gives the same id
    /// on every machine: the first sixteen bytes of a SHA-256 over the namespace and the id,
    /// marked as an RFC 9562 version 8 (custom) UUID — Occurrence Spawning's recipe, under a
    /// namespace of its own.
    /// </summary>
    public static Guid HarvestTaskId(Guid eventId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Namespace + eventId.ToString("D")));
        var bytes = hash.AsSpan(0, 16).ToArray();

        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>
    /// Files the harvest task of every event due on <paramref name="today"/>, the desktop's local
    /// date, under the id derived from the event, and links it to the event as its harvest task;
    /// answers how many it filed. An event already holding a harvest task gets none, so a second
    /// check the same day, or the next, files nothing. A task already held under the derived id,
    /// live or deleted, is linked and not filed again. A filing that is refused, or throws, links
    /// nothing, and the other events still file.
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
    /// Files the harvest task of <paramref name="due"/> under its derived id unless that task is
    /// already held, then links the id on the event as it stands now rather than the copy the
    /// pass listed, so a change the person made in the pane while the task filed is kept; answers
    /// whether it filed. File, then link: when the event is gone, already holds a harvest task, or
    /// is no longer due by then, the task is left unlinked. A link whose save failed heals on the
    /// next check, which finds the task held and links it.
    /// </summary>
    private async Task<bool> FileOneAsync(KnowledgeEvent due, DateOnly today, CancellationToken cancellationToken)
    {
        var taskId = HarvestTaskId(due.Id).ToString("D");

        var filed = await tasks.FileOnceAsync(taskId, TitlePrefix + due.Title, cancellationToken).ConfigureAwait(false);
        if (filed.IsFailure) return false;

        if (await events.GetAsync(due.Id, cancellationToken).ConfigureAwait(false) is not { } current
            || !IsDue(current, today)
            || current.LinkHarvestTask(taskId).IsFailure)
        {
            _log.LogWarning("Harvest task {TaskId} is held but event {EventId} changed meanwhile, so it was not linked.", taskId, due.Id);
            return false;
        }

        await events.SaveAsync(current, cancellationToken).ConfigureAwait(false);
        return filed.Value;
    }

    /// <summary>Going, and its last day — the end, else the start — is before
    /// <paramref name="today"/>. An event with no dates is never due.</summary>
    private static bool IsDue(KnowledgeEvent e, DateOnly today) =>
        e.State == KnowledgeEventState.Going && (e.End ?? e.Start) is { } last && last.Date < today;
}
