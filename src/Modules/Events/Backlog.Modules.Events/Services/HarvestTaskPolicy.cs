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
/// ends, and completes it when the event is marked Harvested. On that move it also files each
/// linked Talk note into the Brain's inbox and links it there (local ADR 0073 §8). Its rules are
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
/// <b>A Talk note moves to the Brain on harvest.</b> Each Talk note the event links is written as
/// a new note in the Brain's inbox through <see cref="IEventTalkNotes"/>, its link is replaced
/// in place by a Brain link to that path, and only then is the Inbox note archived, so a fault
/// part-way leaves the words in the Brain or the Inbox and never in neither. With no Brain set the
/// note and its link stay as they are.
/// </para>
/// <para>
/// A service and not a rule of the event, because it spans the event, a task in Tasks and the
/// notes. It never changes an event's state, and it raises no notification: the task is the
/// reminder. Scoped, for the task target it holds.
/// </para>
/// </summary>
internal sealed partial class HarvestTaskPolicy(
    IKnowledgeEventRepository events,
    IEventTaskTarget tasks,
    ILogger<HarvestTaskPolicy>? log = null,
    KnowledgeEventSaved? saved = null,
    IEventTalkNotes? talkNotes = null)
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
            if (due.HoldsHarvestTask) continue;

            try
            {
                if (await FileOneAsync(due, today, cancellationToken).ConfigureAwait(false)) filed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // One event's fault is no reason for the next to wait a day.
                LogFilingThrew(_log, ex, due.Id);
            }
        }

        return filed;
    }

    /// <summary>
    /// Completes the task the Harvest link of <paramref name="harvested"/> names, and no other: a
    /// prep or linked task is never touched. Whether that task is still open is the task
    /// target's to decide — one already Done or Archived is left as it is. Then files each Talk
    /// note the event links into the Brain (<see cref="FileTalkNotesAsync"/>). Best effort: the
    /// move into Harvested is saved before this runs, so a completion Tasks refuses, or one that
    /// throws, leaves the event Harvested; the answer says whether the completion held.
    /// </summary>
    public async Task<Result> OnHarvestedAsync(KnowledgeEvent harvested, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(harvested);

        var completed = await CompleteHarvestTaskAsync(harvested, cancellationToken).ConfigureAwait(false);
        await FileTalkNotesAsync(harvested, cancellationToken).ConfigureAwait(false);
        return completed;
    }

    private async Task<Result> CompleteHarvestTaskAsync(KnowledgeEvent harvested, CancellationToken cancellationToken)
    {
        if (harvested.Tasks.FirstOrDefault(task => task.Purpose == TaskPurpose.Harvest) is not { } link) return Result.Success();

        try
        {
            return await tasks.CompleteAsync(link.TaskId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogCompletionThrew(_log, ex, link.TaskId, harvested.Id);
            return Result.Failure(CompletionThrew);
        }
    }

    /// <summary>
    /// Writes each Talk note <paramref name="harvested"/> links as a new note in the Brain's
    /// inbox, words unchanged, replaces its link in place with a Brain link to that path, saves
    /// the event once, and then archives each Inbox note that moved (local ADR 0073 §8). A note
    /// the Brain does not take — no Brain set, a refusal, a note the Inbox no longer holds —
    /// keeps its Talk link and stays in the Inbox. Every fault is logged and none is the
    /// move's: the event is already Harvested.
    /// </summary>
    private async Task FileTalkNotesAsync(KnowledgeEvent harvested, CancellationToken cancellationToken)
    {
        if (talkNotes is null) return;

        var moved = new List<(Guid NoteId, NoteLink Talk, NoteLink Brain)>();
        foreach (var talk in harvested.Notes.Where(note => note.Kind == NoteLinkKind.Talk).ToList())
        {
            if (!Guid.TryParse(talk.Reference, out var noteId)) continue;

            // Once a note is in the Brain the rest of the pass runs to its end: a cancel then
            // would leave that note in both homes, with nothing to retry a final state.
            var token = moved.Count == 0 ? cancellationToken : CancellationToken.None;
            if (token.IsCancellationRequested) break;

            try
            {
                if (await talkNotes.FileIntoBrainAsync(noteId, token).ConfigureAwait(false) is not { } filed) continue;

                var brain = NoteLink.Create(NoteLinkKind.Brain, filed.Path, filed.Title);
                if (harvested.ReplaceNoteLink(talk, brain) is Error) continue;
                moved.Add((noteId, talk, brain));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // One note's fault is no reason for the next to stay in the Inbox.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogTalkNoteNotFiled(_log, ex, noteId, harvested.Id);
            }
        }

        if (moved.Count == 0) return;

        try
        {
            await events.SaveAsync(harvested, CancellationToken.None).ConfigureAwait(false);
            saved?.Raise();
        }
#pragma warning disable CA1031 // Best effort: the event is already Harvested, and a fault here is logged.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // The notes are in the Brain and still in the Inbox: nothing is lost. The event, in
            // the store and as the pane reads it now, keeps its Talk links, and the person can
            // link the Brain copies by hand.
            foreach (var (_, talk, brain) in moved) _ = harvested.ReplaceNoteLink(brain, talk);
            LogTalkLinksNotSaved(_log, ex, harvested.Id);
            return;
        }

        foreach (var (noteId, _, _) in moved)
        {
            try
            {
                if (await talkNotes.ArchiveAsync(noteId, CancellationToken.None).ConfigureAwait(false) is Error)
                    LogTalkNoteNotArchived(_log, null, noteId, harvested.Id);
            }
#pragma warning disable CA1031 // Best effort: the note is in the Brain and linked there; a fault leaves it in the Inbox too.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogTalkNoteNotArchived(_log, ex, noteId, harvested.Id);
            }
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
        if (filed is not Ok<bool>(var filedNow)) return false;

        if (await events.GetAsync(due.Id, cancellationToken).ConfigureAwait(false) is not { } current
            || !IsDue(current, today)
            || current.LinkHarvestTask(taskId) is Error)
        {
            LogNotLinked(_log, taskId, due.Id);
            return false;
        }

        await events.SaveAsync(current, cancellationToken).ConfigureAwait(false);
        saved?.Raise();
        return filedNow;
    }

    /// <summary>Not removed, Going, and its last day — the end, else the start — is before
    /// <paramref name="today"/>. An event with no dates is never due. The store already leaves
    /// a removed event out; the policy holds its own rule rather than lean on that.</summary>
    private static bool IsDue(KnowledgeEvent e, DateOnly today) =>
        !e.IsRemoved && e.State == KnowledgeEventState.Going && (e.End ?? e.Start) is { } last && last.Date < today;

    [LoggerMessage(Level = LogLevel.Error, Message = "Filing the harvest task of event {EventId} threw; the next check tries again.")]
    private static partial void LogFilingThrew(ILogger logger, Exception exception, Guid eventId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Completing harvest task {TaskId} of event {EventId} threw; the event stays Harvested.")]
    private static partial void LogCompletionThrew(ILogger logger, Exception exception, string taskId, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Talk note {NoteId} of event {EventId} was not filed into the Brain; it stays in the Inbox.")]
    private static partial void LogTalkNoteNotFiled(ILogger logger, Exception exception, Guid noteId, Guid eventId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Brain links of harvested event {EventId} were not saved; its Talk notes stay in the Inbox.")]
    private static partial void LogTalkLinksNotSaved(ILogger logger, Exception exception, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Talk note {NoteId} of event {EventId} is in the Brain but was not archived in the Inbox.")]
    private static partial void LogTalkNoteNotArchived(ILogger logger, Exception? exception, Guid noteId, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Harvest task {TaskId} is held but event {EventId} changed meanwhile, so it was not linked.")]
    private static partial void LogNotLinked(ILogger logger, string taskId, Guid eventId);
}
