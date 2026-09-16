namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// A remark a person left against one block of a Devbook chapter, as the app
/// keeps it — the record behind the <c>MarkdownComment</c> a read view draws.
/// <para>
/// Backlog's own, not the repository's. The devbook convention has its own
/// note — the <c>annotation</c> fence written into the chapter beside the
/// passage it is about — and that is a shared review artefact that travels with
/// the repository through git. This is the other thing: a private reading note,
/// owned by the person and their devices rather than by the repository, never
/// written into a governed knowledge folder, and so free to exist on a chapter
/// read from a branch snapshot nobody can write to. The two are deliberately not
/// the same store, and nothing promotes one into the other yet.
/// </para>
/// <para>
/// Addressed by repository alias and repository-relative chapter path rather
/// than by anything on disk, so the same remark names the same chapter on every
/// device the person owns — the alias is the cross-device name a repository
/// already has (.arc42/adr/0005 §Session records). Anchored by block index for
/// the reason <c>MarkdownComment</c> gives; re-anchoring after the chapter
/// changes is the host's problem and not yet solved, so an index that has gone
/// out of range shows at the end of the chapter rather than vanishing.
/// </para>
/// <para>
/// <see cref="UpdatedAt"/> and <see cref="DeletedAt"/> are what let it
/// replicate on a task's terms: whole-document last-write-wins, and a deletion
/// that is a tombstone rather than an absence. A remark with an empty
/// <see cref="Body"/> is a draft the read view has open in its own textarea; it
/// is kept so that closing the pane does not lose it, and never replicated.
/// </para>
/// </summary>
public sealed record DevbookAnnotation(
    Guid Id,
    string RepositoryAlias,
    string ChapterPath,
    int BlockIndex,
    string Body,
    string Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool Resolved = false,
    DateTimeOffset? DeletedAt = null)
{
    /// <summary>A remark nobody has typed into yet.</summary>
    public bool IsDraft => DeletedAt is null && string.IsNullOrWhiteSpace(Body);

    /// <summary>Not deleted. Drafts count as live: they are on screen.</summary>
    public bool IsLive => DeletedAt is null;
}
