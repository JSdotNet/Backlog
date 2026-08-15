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
    IReadOnlyList<EntryProjectionDto> Projections);

/// <summary>Where an entry has been projected to outside this system — today a
/// GitHub issue. Kept as data rather than a typed link so the module does not
/// have to know what GitHub is.</summary>
public sealed record EntryProjectionDto(string RepoId, string ExternalId, string TargetType);
