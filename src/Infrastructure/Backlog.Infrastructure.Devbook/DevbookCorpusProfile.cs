namespace Backlog.Infrastructure.Devbook;

/// <summary>
/// How a folder handed to <see cref="DevbookDatabaseBuilder"/> is read.
/// <see cref="Repository"/> is the devbook layout every repository uses; another
/// context supplies its own by deriving from this and registering its folder
/// (<see cref="DevbookDatabaseLocation.RegisterCorpus"/>) — local ADR 0040 §2,
/// the hook Devbook's builder offers, so that it never names what a profile
/// reads. Identity is by reference: one instance per profile.
/// </summary>
public abstract class DevbookCorpusProfile
{
    /// <summary>A repository: its devbook folders under <c>.devbook/</c>, or at the
    /// root in the older layout, with every folder convention.</summary>
    public static DevbookCorpusProfile Repository { get; } = new RepositoryProfile();

    /// <summary>The tag of the extra line the inputs fingerprint writes after its
    /// schema version — <c>&lt;tag&gt;\t&lt;folders&gt;</c> — so that a change to the
    /// folder set alone makes the database stale; <see langword="null"/> writes
    /// none.</summary>
    public virtual string? FingerprintTag => null;

    /// <summary>Whether a cloud-only placeholder is listed as a file — indexed by
    /// its name, never read — rather than skipped as a link.</summary>
    public virtual bool ReadsCloudPlaceholders => false;

    /// <summary>
    /// Whether the devbook folder conventions apply: reading-order slots, resting
    /// status, the <c>kind</c> fallback, debt records and the C4 workspace. Without
    /// them a path's folder is the first of the profile's folders it lies under,
    /// and no convention folder applies to it, even one that shares a devbook
    /// folder's name.
    /// </summary>
    public virtual bool HasFolderConventions => true;

    /// <summary>Whether <c>repo:</c> links are kept as edges: a prose link as a
    /// <c>repo-link</c> edge, and a <c>repo:</c> reference kept with no node behind
    /// it (local ADR 0027 §6).</summary>
    public virtual bool ReadsCrossCorpusLinks => false;

    /// <summary>
    /// The list fields a <c>meta</c> block may also write as a YAML block list — the
    /// field with no value, then one <c>- item</c> line per value — read as if
    /// written <c>[a, b]</c>. The devbook parse skips those lines, so none by
    /// default; the Brain's are the lists the app appends to in either style
    /// (local ADR 0052 §1).
    /// </summary>
    public virtual IReadOnlyCollection<string> BlockListFields => [];

    /// <summary>The folders to index, relative to <paramref name="root"/> and in
    /// order, or <see langword="null"/> for the repository devbook layout's own
    /// discovery. An empty list is an ordinary answer: nothing to build.</summary>
    public virtual IReadOnlyList<string>? Folders(string root) => null;

    /// <summary>Further root-relative files the inputs fingerprint records, after
    /// the sorted walk — a file that changes what is read without being read as a
    /// chapter itself.</summary>
    public virtual IEnumerable<string> ExtraInputs(string root) => [];

    /// <summary>
    /// Fields the profile adds to a node, after the devbook's own: called for
    /// every file node with its <paramref name="folder"/>, and for every heading
    /// that carries a block with <see langword="null"/>.
    /// </summary>
    public virtual void ApplyNodeFields(IDevbookNodeFields node, string? folder)
    {
    }

    private sealed class RepositoryProfile : DevbookCorpusProfile;
}
