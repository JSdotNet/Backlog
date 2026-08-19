namespace Backlog.Modules.Backlog.Abstractions.DataTransferObjects;

/// <summary>
/// A backlog entry as anything outside the module sees it: what it says, not
/// what it can do.
/// <para>
/// Callers get this instead of the aggregate so that changing an entry has to go
/// through a use case. Sub-items are not listed separately because they are
/// written as <c>##</c> headings inside <paramref name="Body"/> — the text is the
/// source of truth, and <see cref="EntryTextParser"/> reads them back out of it.
/// The counts are here because a list wants to show progress without parsing.
/// </para>
/// <para>
/// The scheduling and dependency members are what the canonical metadata line is
/// rebuilt from (<see cref="EntryTextParser.ToRawText"/>), which makes this record
/// load-bearing for round-trip fidelity rather than merely descriptive: a field
/// the aggregate holds and this record does not is destroyed on the next save.
/// They carry defaults so that a caller constructing a DTO for a test or a
/// projection does not have to state five fields it has no opinion about.
/// </para>
/// <para>
/// <paramref name="View"/> rides on that same rebuild without being one of them:
/// it is a display preference the entry carries rather than a fact about the work
/// (see <see cref="EntryView"/>), and it is published here for exactly one reason
/// — the metadata line is composed from this record, so a preference the record
/// does not carry is destroyed by the next save.
/// </para>
/// </summary>
public sealed record BacklogEntryDto(
    Guid Id,
    string Title,
    string Body,
    EntryType Type,
    Priority Priority,
    EntryStatus Status,
    string? Area,
    IReadOnlyList<string> Tags,
    int Order,
    int TotalSubItems,
    int CompletedSubItems,
    IReadOnlyList<EntryProjectionDto> Projections,
    DateOnly? DueOn = null,
    DateTime? RemindAt = null,
    Recurrence? Recurrence = null,
    DateOnly? InMyDayOn = null,
    IReadOnlyList<string>? DependsOn = null,
    EntryView? View = null);

/// <summary>Where an entry has been projected to outside this system — today a
/// GitHub issue. Kept as data rather than a typed link so the module does not
/// have to know what GitHub is.</summary>
public sealed record EntryProjectionDto(string RepoId, string ExternalId, string TargetType)
{
    /// <summary>The <see cref="TargetType"/> an entry carries once it has been
    /// pushed to a GitHub issue. The value is this context's vocabulary rather
    /// than the adapter's: what an entry was projected onto is a fact about the
    /// entry, and a caller comparing against it should not have to reference the
    /// adapter that happened to create it.</summary>
    public const string IssueTargetType = "issue";
}
