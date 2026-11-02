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
/// written into a governed devbook folder, and so free to exist on a chapter
/// read from a branch snapshot nobody can write to. The two are not the same
/// store; the one bridge is the one-time import of a repository's open fences
/// through <see cref="IDevbookAnnotationStore.ImportIfAbsent"/>.
/// </para>
/// <para>
/// Addressed by repository alias and repository-relative chapter path rather
/// than by anything on disk, so the same remark names the same chapter on every
/// device the person owns — the alias is the cross-device name a repository
/// already has (.devbook/arc42/adr/0005 §Session records). Anchored by block index for
/// the reason <c>MarkdownComment</c> gives, and by
/// <see cref="BlockHash"/> beside it, which is what lets the index be wrong:
/// the index says where to look, the hash says whether the block found there is
/// still the one the remark was about. A chapter edited above a remark is
/// therefore re-anchored rather than silently pointing at its neighbour, and
/// only a remark whose block has genuinely gone shows at the end of the chapter.
/// </para>
/// <para>
/// <see cref="UpdatedAt"/> and <see cref="DeletedAt"/> are what let it
/// replicate on a task's terms: whole-document last-write-wins, and a deletion
/// that is a tombstone rather than an absence. A remark with an empty
/// <see cref="Body"/> is a draft the read view has open in its own textarea; it
/// is kept so that closing the pane does not lose it, and never replicated.
/// </para>
/// <para>
/// <see cref="Kind"/> and <see cref="Branch"/> came with devbook's branch-true
/// decision, which makes the store rather than the fence where an open note lives.
/// The kind is the fence's closed set, <see cref="DevbookAnnotationKind.Comment"/>
/// by default. The branch is the one the chapter was read from: null for the
/// default branch — a clone, a default-branch snapshot, and every remark stored
/// before the field existed — and the name for a named-branch snapshot, so a
/// remark left on a branch shows on that branch and nowhere else.
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
    DateTimeOffset? DeletedAt = null,
    string? BlockHash = null,
    DevbookAnnotationKind Kind = DevbookAnnotationKind.Comment,
    string? Branch = null)
{
    /// <summary>One spelling for "the default branch": a blank name is none, and a
    /// named one is trimmed. Every store keys a remark's branch through this, so a
    /// caller holding <c>""</c> and one holding null ask the same question.</summary>
    public static string? BranchKey(string? branch) =>
        string.IsNullOrWhiteSpace(branch) ? null : branch.Trim();

    /// <summary>Whether this remark can be re-anchored at all.
    /// <para>
    /// A remark made before anchoring carried a hash — and one on a block with
    /// no text to digest — has none, and can only be believed rather than
    /// checked. That is exactly the behaviour it already had, so nothing on
    /// disk or on another device needs migrating for the hash to be safe to
    /// add.
    /// </para>
    /// </summary>
    public bool IsAnchored => !string.IsNullOrEmpty(BlockHash);

    /// <summary>A remark nobody has typed into yet.</summary>
    public bool IsDraft => DeletedAt is null && string.IsNullOrWhiteSpace(Body);

    /// <summary>Not deleted. Drafts count as live: they are on screen.</summary>
    public bool IsLive => DeletedAt is null;
}
