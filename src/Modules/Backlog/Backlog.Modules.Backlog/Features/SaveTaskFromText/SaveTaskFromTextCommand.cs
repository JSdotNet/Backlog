using Backlog.Modules.Backlog.Abstractions;
using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.Modules.Backlog.DomainModels;
using Backlog.Modules.Backlog.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Backlog.Features.SaveTaskFromText;

/// <summary>
/// Writes one block of entry markdown down as an entry — creating it when
/// <paramref name="Id"/> is null, updating it otherwise.
/// <para>
/// The whole editing model of this product is that an entry <em>is</em> its
/// text, so this is the use case behind nearly every keystroke. It is the module
/// that reads that text, not the editor: the format is the context's published
/// language and the rules for what a token means belong with the aggregate that
/// has to honour them.
/// </para>
/// </summary>
public sealed record SaveTaskFromTextCommand(Guid? Id, string RawText, int Order);

public sealed class SaveTaskFromTextCommandHandler(ITaskRepository entries)
    : ICommandHandler<SaveTaskFromTextCommand, Result<SavedTaskDto>>
{
    /// <summary>An entry needs a title before it can exist. Somebody halfway
    /// through typing one has not failed at anything, so this is an ordinary
    /// outcome the editor holds on to rather than an error it reports.</summary>
    public static readonly Error NeedsTitle = Error.Validation(
        "entry.needs_title",
        "An entry needs a title line before it can be saved.");

    public static readonly Error NotFound = Error.NotFound(
        "entry.not_found",
        "That entry no longer exists.");

    public async Task<Result<SavedTaskDto>> Handle(
        SaveTaskFromTextCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var parsed = EntryTextParser.Parse(command.RawText);

        return command.Id is { } id
            ? await UpdateAsync(id, parsed, cancellationToken)
            : await CreateAsync(parsed, command.Order, cancellationToken);
    }

    private async Task<Result<SavedTaskDto>> CreateAsync(
        EntryTextParser.ParsedEntry parsed,
        int order,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parsed.Title)) return NeedsTitle;

        var entry = new TaskItem(
            parsed.Title,
            parsed.Body,
            parsed.Type ?? EntryType.Task,
            parsed.Priority ?? Priority.Medium,
            tags: parsed.Tags);

        // New entries are born at Draft. A status typed into the meta line is
        // applied as the direct value rather than stepped through the lifecycle,
        // so writing `!done` on a fresh entry means what it says.
        if (parsed.Status is { } initialStatus) entry.SetStatus(initialStatus);

        entry.SetOrder(Math.Max(order, 0));
        entry.SetArea(parsed.Area);
        ApplyScheduling(entry, parsed);
        ApplyPresentation(entry, parsed);
        TaskTextSync.SyncSubItems(entry, parsed.SubItems);

        await entries.SaveAsync(entry, cancellationToken);

        // Deliberately no successor here, even for an entry typed straight in as
        // `!done` with a repeat on it. Spawning is what happens when a save
        // *completes* an occurrence, and a create has no previous state for the
        // save to have moved it from — an entry arriving already finished is a
        // record of something done, not an occurrence just now finishing.
        return new SavedTaskDto(entry.ToDto());
    }

    private async Task<Result<SavedTaskDto>> UpdateAsync(
        Guid id,
        EntryTextParser.ParsedEntry parsed,
        CancellationToken cancellationToken)
    {
        var entry = await entries.GetAsync(id, cancellationToken);
        if (entry is null) return NotFound;

        // Read before anything is applied, because "this save completed the
        // entry" is a statement about the step from one status to another. An
        // entry that was already Done stays Done and spawns nothing: without this
        // the next keystroke on a finished repeating entry would spawn a second
        // successor, and the one after that a third.
        var wasDone = entry.Status is EntryStatus.Done;

        // A title that has momentarily been deleted is not an instruction to
        // rename the entry to nothing — the aggregate would refuse anyway.
        if (!string.IsNullOrWhiteSpace(parsed.Title)) entry.Rename(parsed.Title);

        entry.UpdateContent(parsed.Body);
        entry.ChangeType(parsed.Type ?? entry.Type);
        entry.ChangePriority(parsed.Priority ?? entry.Priority);
        entry.SetTags(parsed.Tags);
        entry.SetArea(parsed.Area);
        ApplyScheduling(entry, parsed);
        ApplyPresentation(entry, parsed);

        if (parsed.Status is { } targetStatus) entry.SetStatus(targetStatus);

        TaskTextSync.SyncSubItems(entry, parsed.SubItems);

        await entries.SaveAsync(entry, cancellationToken);

        if (!wasDone && entry.Status is EntryStatus.Done && entry.Recurrence is not null)
        {
            // Saved after the completed occurrence, so a failure here cannot lose
            // the completion that has already been recorded.
            var successor = RecurrencePolicy.NextOccurrence(entry);
            await entries.SaveAsync(successor, cancellationToken);

            // Named in the result rather than left for the caller to notice. A
            // list that only ever refreshes the row it saved has no other way to
            // learn that a second entry now exists.
            return new SavedTaskDto(entry.ToDto(), successor.Id);
        }

        return new SavedTaskDto(entry.ToDto());
    }

    /// <summary>
    /// Writes the scheduling and dependency fields on unconditionally, so a token
    /// that is no longer on the metadata line clears the field it named. This
    /// follows <c>SetTags</c> and <c>SetArea</c> rather than the
    /// <c>?? entry.Type</c> fallback that Type and Priority use, and the
    /// difference is deliberate: type and priority are values an entry always has,
    /// while these five are absent by default — and "delete the token to clear the
    /// due date" is only true if an absent token means absent.
    /// </summary>
    private static void ApplyScheduling(TaskItem entry, EntryTextParser.ParsedEntry parsed)
    {
        entry.SetDueOn(parsed.DueOn);
        entry.SetReminder(parsed.RemindAt);
        entry.SetRecurrence(parsed.Recurrence);
        entry.SetInMyDayOn(parsed.InMyDayOn);
        entry.SetDependsOn(parsed.DependsOn ?? []);

        // Not scheduling, and here anyway. What is attached is a fact about the
        // work like the five above it — set once, cleared by deleting the token —
        // and the alternative was a method of one line whose only claim was that a
        // folder is not a date. Unconditional for the same reason they are:
        // deleting `files:` is how a reader detaches, which is only true while an
        // absent token means absent.
        entry.SetAttachment(parsed.Attachment);
    }

    /// <summary>
    /// Writes the one token on the metadata line that is about the reader rather
    /// than about the work: which reading of the body they last asked for.
    /// <para>
    /// Its own method rather than a sixth line in <see cref="ApplyScheduling"/>,
    /// because grouping it with the due date would quietly claim it is the same kind
    /// of fact. A due date is a promise about the work; this is a preference about
    /// looking at it, and the only reason the aggregate holds it at all is that the
    /// canonical rewrite composes the metadata line from the entry — see
    /// <see cref="EntryView"/>.
    /// </para>
    /// <para>
    /// Unconditional for the same reason the scheduling fields are: deleting the
    /// token is how a reader goes back to having expressed no preference, and that
    /// is only true if an absent token means absent.
    /// </para>
    /// </summary>
    private static void ApplyPresentation(TaskItem entry, EntryTextParser.ParsedEntry parsed) =>
        entry.SetView(parsed.View);
}
