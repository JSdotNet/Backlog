namespace Backlog.Modules.Knowledge.Abstractions;

/// <summary>
/// Answers "where does this knowledge area live right now?" for a repository
/// scope, and "which folders are configured for that scope?".
/// <para>
/// The answer is stitched together from two settings files — a repository's
/// configured folders when a scope is named, the local storage folder's when one
/// is not — and stitching them is exactly what Second Brain must not have to
/// know. Reaching for the backlog's root store to find out would make this
/// context depend on Tasks, which <c>.domain/context-map.md</c>
/// calls a Partnership that coordinates by id rather than by reaching across.
/// The adapter that implements this port sees both; the panels see only this.
/// </para>
/// </summary>
public interface IKnowledgeFolderSource
{
    /// <summary>Raised when a folder, a repository or the storage root moves, or
    /// when a folder's content was replaced under it, so open panels can
    /// reload.</summary>
    event Action? Changed;

    /// <summary>
    /// Announce that a folder's content was replaced wholesale — a clone pulled to
    /// its latest version, say — so open panels reload the way they already do
    /// when a folder moves.
    /// <para>
    /// This sits on the same port as <see cref="Changed"/> rather than on one of
    /// its own, because the two are one mechanism: a port that publishes an event
    /// and hides who may raise it leaves the raiser reaching for the adapter, and
    /// reaching for the adapter is the thing this port exists to stop. Moving and
    /// being overwritten are the same news to every subscriber — the folder you
    /// read is not the folder you have — so they arrive as the same event.
    /// </para>
    /// </summary>
    void NotifyContentChanged();

    /// <summary>Where the folders resolve against when no repository is scoped.
    /// A panel needs it to present the storage folder as a source alongside the
    /// configured repositories; it is a path and nothing more.</summary>
    string StorageDirectory { get; }

    /// <summary>The configured folders for a scope: the repository's when one is
    /// named, the storage folder's otherwise.</summary>
    IReadOnlyList<KnowledgeFolderSetting> Folders(string? repositoryAlias);

    /// <summary>Where one area's folder is, or why it is not available.</summary>
    KnowledgeFolderLocation Resolve(string key, string? repositoryAlias = null);
}

/// <summary>
/// Where a knowledge folder resolved to, or why it did not.
/// <para>
/// The repository is named rather than handed over: this record is part of a
/// module's published surface and <c>GitHubRepositoryRef</c> belongs to an
/// infrastructure adapter, which a module may not reference. A caller that wants
/// to show which repository a folder came from wants the name; a caller that
/// wants the repository itself has the alias to look it up with.
/// </para>
/// </summary>
public sealed record KnowledgeFolderLocation(
    string Key,
    bool Available,
    string? Message,
    string? RepositoryFullName,
    KnowledgeFolderSetting? Folder,
    string? FullPath,
    string? RootPath = null,
    string? ScopeLabel = null,
    string? RepositoryAlias = null,
    KnowledgeSourceKind Source = KnowledgeSourceKind.LocalFolder)
{
    /// <summary>
    /// Whether a caller may write to what it just resolved.
    /// <para>
    /// A branch snapshot is a copy of somebody else's commit, and the only thing
    /// editing it could achieve is losing the edit at the next fetch. So the
    /// panels do not offer the edit at all rather than offering one that is
    /// quietly discarded — the same reasoning
    /// <c>KnowledgeChapterEditor</c> already applies to a chapter it could not
    /// place on disk.
    /// </para>
    /// </summary>
    public bool CanEdit => Source is KnowledgeSourceKind.LocalFolder;

    public static KnowledgeFolderLocation Unavailable(
        string key,
        string message,
        string? repositoryFullName = null,
        KnowledgeFolderSetting? folder = null,
        string? fullPath = null,
        string? rootPath = null,
        string? scopeLabel = null,
        string? repositoryAlias = null,
        KnowledgeSourceKind source = KnowledgeSourceKind.LocalFolder) =>
        new(key, false, message, repositoryFullName, folder, fullPath, rootPath, scopeLabel, repositoryAlias, source);
}

/// <summary>
/// Which of the two places a resolved knowledge folder came from.
/// <para>
/// It rides on the location rather than being asked of the settings again,
/// because the question every caller actually has is not "how is this repository
/// configured?" but "may I write to the folder I was just handed?" — and only
/// the resolution knows that. A repository configured to read a branch but
/// holding a clone still resolves to the branch; a repository configured for its
/// local folder with no clone on this machine still resolves to the branch.
/// </para>
/// <para>
/// <see cref="LocalFolder"/> is the default on <see cref="KnowledgeFolderLocation"/>
/// so that every construction predating branch loading — the storage-folder
/// scope, and the test fakes — keeps meaning what it meant: a real folder
/// somebody may edit.
/// </para>
/// </summary>
public enum KnowledgeSourceKind
{
    /// <summary>A working folder on this machine: a clone, or the storage folder.
    /// Readable and writable.</summary>
    LocalFolder,

    /// <summary>A snapshot of a repository branch, fetched and cached. Readable
    /// only.</summary>
    Branch
}
