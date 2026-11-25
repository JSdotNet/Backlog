using System.Globalization;

namespace Backlog.SharedKernel.Changes;

/// <summary>
/// The journal port a store with no table in <c>backlog.db</c> writes its revisions
/// through (local ADR 0061 §2) — the devbook subject states and the remarks, which are
/// JSON files a trigger cannot reach.
/// <para>
/// The store calls <see cref="Record"/> with the document as it stands, before it writes
/// the file, under the change set <see cref="ChangeContext.Current"/> names, or an
/// unknown writer's when none is current, as a trigger would. When the file write then
/// fails it calls <see cref="Withdraw"/>, so the journal never claims a change the file
/// does not hold. Synchronous, because the stores write under a lock.
/// </para>
/// </summary>
public interface IDocumentRevisionJournal
{
    /// <summary>Records that <paramref name="documentId"/> of
    /// <paramref name="documentKind"/> is about to be written: <paramref name="before"/>
    /// is the document as it stands, null when the write creates it, and
    /// <paramref name="afterUpdatedAt"/> the stamp the write leaves, null when it removes
    /// the document. A second write in the same change set only moves that stamp on.
    /// Null when nothing was recorded — inside <see cref="ChangeContext.Unjournalled"/>,
    /// or when the journal could not be written, which never stops the write.</summary>
    JournalRevision? Record(string documentKind, string documentId, string? before, DateTimeOffset? afterUpdatedAt);

    /// <summary>Takes back what <paramref name="revision"/> recorded, for a write that did
    /// not land.</summary>
    void Withdraw(JournalRevision revision);
}

/// <summary>What one <see cref="IDocumentRevisionJournal.Record"/> wrote, so
/// <see cref="IDocumentRevisionJournal.Withdraw"/> can take exactly that back.</summary>
/// <param name="ChangeSetId">The change set it was recorded under.</param>
/// <param name="Sequence">The revision's row.</param>
/// <param name="Inserted">True when the call made the revision; false when it moved an
/// earlier one's stamp.</param>
/// <param name="PreviousAfterUpdatedAt">The stamp an earlier revision held before the
/// call moved it.</param>
/// <param name="ChangeSetCreated">True when the call also recorded the change set.</param>
public sealed record JournalRevision(
    Guid ChangeSetId,
    long Sequence,
    bool Inserted,
    string? PreviousAfterUpdatedAt,
    bool ChangeSetCreated);

/// <summary>The one text form every journalled stamp is stored and compared in: the
/// round-trip <c>"O"</c> form in UTC, as the tables store theirs.</summary>
public static class JournalStamp
{
    public static string Format(DateTimeOffset stamp) =>
        stamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static string? Format(DateTimeOffset? stamp) => stamp is { } value ? Format(value) : null;
}

/// <summary>
/// A store outside <c>backlog.db</c> whose documents the journal keeps, and which writes
/// them back when a person restores a change set (local ADR 0061 §4) — through its own
/// write path, so its file, its lock and its change notice stay its own.
/// </summary>
public interface IJournalledDocumentStore
{
    /// <summary>The <c>revisions.document_kind</c> values this store answers for.</summary>
    IReadOnlyCollection<string> DocumentKinds { get; }

    /// <summary>The document's stamp now, in <see cref="JournalStamp"/>'s form, or null
    /// when the store holds no such document.</summary>
    string? HeldStamp(DocumentKey key);

    /// <summary>A title for the document: as it stands, else as
    /// <paramref name="before"/> held it; null when neither has one.</summary>
    string? TitleOf(DocumentKey key, string? before);

    /// <summary>What a restore would do with a revision whose prior document is
    /// <paramref name="before"/>.</summary>
    RestoreAction ActionFor(DocumentKey key, string? before);

    /// <summary>Writes the document back to <paramref name="before"/> — or takes it away
    /// when that is null, since the change set created it — stamped later than the
    /// version held, and only while the store still holds the stamp
    /// <paramref name="held"/>. True when it wrote.</summary>
    bool Restore(DocumentKey key, string? before, string? held);
}
