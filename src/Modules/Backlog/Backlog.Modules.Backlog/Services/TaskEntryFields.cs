using Backlog.Modules.Backlog.Abstractions;
using Backlog.Modules.Backlog.DomainModels;

namespace Backlog.Modules.Backlog.Services;

/// <summary>
/// Builds and applies the fields a <see cref="EntryTextParser.ParsedEntry"/>
/// carries on to a <see cref="TaskItem"/>.
/// <para>
/// Shared behaviour behind <c>SaveTaskFromText</c> and Import: both take a block
/// of entry text and either construct a new aggregate from it or bring an
/// existing one up to date, and per this module's own
/// <c>Features/README.md</c> rule a slice does not call another slice — so the
/// logic neither owns lives here rather than in either one.
/// </para>
/// </summary>
internal static class TaskEntryFields
{
    /// <summary>Constructs a new entry from a parsed segment, at the given manual
    /// rank. Mirrors what a fresh <c>SaveTaskFromText</c> create used to do
    /// inline: born at <see cref="EntryStatus.Draft"/> unless the text itself
    /// says otherwise, then every other field applied on top.</summary>
    public static TaskItem CreateFrom(EntryTextParser.ParsedEntry parsed, int order)
    {
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

        return entry;
    }

    /// <summary>Brings an existing entry's fields up to date from a parsed
    /// segment. Deliberately does not touch status or record the completion of a
    /// repeat — those stay the caller's own decision, the way
    /// <c>SaveTaskFromTextCommand.UpdateAsync</c> keeps them, because "was this
    /// save the one that finished a repeating entry" is a question only that
    /// caller can answer.</summary>
    public static void ApplyToExisting(TaskItem entry, EntryTextParser.ParsedEntry parsed)
    {
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

        TaskTextSync.SyncSubItems(entry, parsed.SubItems);
    }

    /// <summary>
    /// Writes the scheduling and dependency fields on unconditionally, so a token
    /// that is no longer on the metadata line clears the field it named. This
    /// follows <c>SetTags</c> and <c>SetArea</c> rather than the
    /// <c>?? entry.Type</c> fallback that Type and Priority use, and the
    /// difference is deliberate: type and priority are values an entry always has,
    /// while these fields are absent by default — and "delete the token to clear
    /// the due date" is only true if an absent token means absent.
    /// </summary>
    private static void ApplyScheduling(TaskItem entry, EntryTextParser.ParsedEntry parsed)
    {
        entry.SetDueOn(parsed.DueOn);
        entry.SetReminder(parsed.RemindAt);
        entry.SetRecurrence(parsed.Recurrence);
        entry.SetInMyDayOn(parsed.InMyDayOn);
        entry.SetDependsOn(parsed.DependsOn ?? []);

        // Not scheduling, and here anyway. What is attached is a fact about the
        // work like the fields above it — set once, cleared by deleting the
        // token — and the alternative was a method of one line whose only claim
        // was that a folder is not a date. Unconditional for the same reason
        // they are: deleting `files:` is how a reader detaches, which is only
        // true while an absent token means absent.
        entry.SetAttachment(parsed.Attachment);

        // A size is a fact about the work too, and clearable the same way: deleting
        // the `effort:` token is how an estimate is retracted, so it is written
        // unconditionally like the fields above rather than merged, which would let
        // a token that is gone leave a stale estimate behind. The aggregate refuses
        // a negative, but the parser never hands one up — an unreadable value
        // arrives here as null.
        entry.SetEffort(parsed.Effort);

        // `repo:` and `id:` round-trip the same way every other named token
        // does — general grammar, not Import-specific, per ADR 0004: an entry
        // saved through the ordinary text-save path carries them exactly as
        // one saved through Import does.
        entry.SetRepoIds(parsed.RepoIds ?? []);
        entry.SetImportItemId(parsed.ImportItemId);
    }

    /// <summary>
    /// Writes the one token on the metadata line that is about the reader rather
    /// than about the work: which reading of the body they last asked for.
    /// <para>
    /// Its own method rather than a line in <see cref="ApplyScheduling"/>,
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
