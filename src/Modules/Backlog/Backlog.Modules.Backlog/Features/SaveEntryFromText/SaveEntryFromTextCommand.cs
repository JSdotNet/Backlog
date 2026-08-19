using Backlog.Modules.Backlog.Abstractions;
using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.Modules.Backlog.DomainModels;
using Backlog.Modules.Backlog.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Backlog.Features.SaveEntryFromText;

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
public sealed record SaveEntryFromTextCommand(Guid? Id, string RawText, int Order);

public sealed class SaveEntryFromTextCommandHandler(IBacklogRepository entries)
    : ICommandHandler<SaveEntryFromTextCommand, Result<BacklogEntryDto>>
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

    public async Task<Result<BacklogEntryDto>> Handle(
        SaveEntryFromTextCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var parsed = EntryTextParser.Parse(command.RawText);

        return command.Id is { } id
            ? await UpdateAsync(id, parsed, cancellationToken)
            : await CreateAsync(parsed, command.Order, cancellationToken);
    }

    private async Task<Result<BacklogEntryDto>> CreateAsync(
        EntryTextParser.ParsedEntry parsed,
        int order,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parsed.Title)) return NeedsTitle;

        var entry = new BacklogEntry(
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
        EntryTextSync.SyncSubItems(entry, parsed.SubItems);

        await entries.SaveAsync(entry, cancellationToken);

        // Deliberately no successor here, even for an entry typed straight in as
        // `!done` with a repeat on it. Spawning is what happens when a save
        // *completes* an occurrence, and a create has no previous state for the
        // save to have moved it from — an entry arriving already finished is a
        // record of something done, not an occurrence just now finishing.
        return entry.ToDto();
    }

    private async Task<Result<BacklogEntryDto>> UpdateAsync(
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

        if (parsed.Status is { } targetStatus) entry.SetStatus(targetStatus);

        EntryTextSync.SyncSubItems(entry, parsed.SubItems);

        await entries.SaveAsync(entry, cancellationToken);

        if (!wasDone && entry.Status is EntryStatus.Done && entry.Recurrence is not null)
        {
            // Saved after the completed occurrence, so a failure here cannot lose
            // the completion that has already been recorded.
            await entries.SaveAsync(RecurrencePolicy.NextOccurrence(entry), cancellationToken);
        }

        return entry.ToDto();
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
    private static void ApplyScheduling(BacklogEntry entry, EntryTextParser.ParsedEntry parsed)
    {
        entry.SetDueOn(parsed.DueOn);
        entry.SetReminder(parsed.RemindAt);
        entry.SetRecurrence(parsed.Recurrence);
        entry.SetInMyDayOn(parsed.InMyDayOn);
        entry.SetDependsOn(parsed.DependsOn ?? []);
    }
}
