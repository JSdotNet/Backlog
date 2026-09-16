namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// Where a person's remarks on Devbook chapters are kept — the home the panels
/// deliberately did not choose while a remark lived in a dictionary on the
/// component and went with it when the Router remounted the pane.
/// <para>
/// A port, so that the panels see one thing and the two adapters that write to
/// it — the file store the desktop composes, and the replication that applies
/// what other devices changed — see the same one. Everything is synchronous and
/// in memory once loaded: a repository's remarks are a small file, and a panel
/// asks for them on every render.
/// </para>
/// <para>
/// Two kinds of write, and they stamp differently. The four verbs a panel uses
/// (<see cref="Add"/>, <see cref="Edit"/>, <see cref="SetResolved"/>,
/// <see cref="Delete"/>) stamp <c>UpdatedAt</c> from the store's own clock,
/// which is what puts the change in front of the push watermark.
/// <see cref="Apply"/> writes a document exactly as it arrived from another
/// device, stamps included — it is replication's, and the merge has already
/// decided the arriving version wins.
/// </para>
/// </summary>
public interface IDevbookAnnotationStore
{
    /// <summary>Raised after any write, on whichever thread wrote. A panel
    /// subscribing to it has to marshal; replication is the one writer that is
    /// never on the renderer's thread.</summary>
    event Action? Changed;

    /// <summary>The live remarks on one chapter, drafts included, oldest first.
    /// An unscoped chapter — no repository alias — is answered under the empty
    /// alias, so a storybook or harness with no registry still keeps its
    /// remarks somewhere.</summary>
    IReadOnlyList<DevbookAnnotation> List(string? repositoryAlias, string chapterPath);

    /// <summary>One remark by id, tombstone or not, or null. Replication asks
    /// this to decide whether an arriving version is newer.</summary>
    DevbookAnnotation? Find(Guid id);

    /// <summary>A fresh, empty remark against one block — a draft the read view
    /// opens straight into its textarea.</summary>
    DevbookAnnotation Add(string? repositoryAlias, string chapterPath, int blockIndex, string author);

    /// <summary>Replaces the body. A no-op for an id the store does not hold.</summary>
    void Edit(Guid id, string body);

    /// <summary>Marks a remark dealt with, or not. A resolved remark stays
    /// visible and quiet rather than disappearing.</summary>
    void SetResolved(Guid id, bool resolved);

    /// <summary>
    /// Takes a remark away. A draft is removed outright — it never left this
    /// machine, so there is nothing anywhere else to tell — and anything else
    /// becomes a tombstone, because a deletion has to replicate and a row that is
    /// simply gone cannot.
    /// </summary>
    void Delete(Guid id);

    /// <summary>Writes a document as replication hands it over, tombstone or
    /// live, stamps and all. Nothing here decides whether it should win; the
    /// merge did.</summary>
    void Apply(DevbookAnnotation replicated);

    /// <summary>
    /// Everything this machine has changed strictly after <paramref name="watermark"/>,
    /// tombstones included and drafts excluded, ordered by <c>UpdatedAt</c> —
    /// what a push selects. Across every repository, because the watermark is
    /// the device's rather than a repository's.
    /// </summary>
    IReadOnlyList<DevbookAnnotation> ListChangedSince(DateTimeOffset watermark);
}
