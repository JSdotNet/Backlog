namespace Backlog.Modules.Backlog.Abstractions.Services;

/// <summary>
/// Where the backlog lives on disk, and where a repository's authored entries
/// live when one is scoped.
/// <para>
/// The setting itself is deliberately <em>not</em> stored in the backlog folder —
/// it is kept in a fixed per-user location, because a pointer that moves with
/// the thing it points at is no pointer at all.
/// </para>
/// <para>
/// This is narrower than the store behind it, and the narrowing is forced rather
/// than preferred. The adapter also remembers which GitHub repository backs the
/// storage folder and which knowledge folders are configured for it; both are
/// expressed in <c>Backlog.Infrastructure.GitHub</c> and
/// <c>Backlog.Modules.Knowledge.Abstractions</c> types, and an abstractions
/// project may reference neither an infrastructure adapter
/// (<c>ModuleBoundaryTests.A_module_never_references_infrastructure</c>) nor
/// another module. Those members have one consumer besides tests — the desktop
/// settings screen — so they stayed on the adapter, where that screen takes them
/// directly. Do not add them back here.
/// </para>
/// </summary>
public interface IBacklogStore
{
    /// <summary>Raised after the store moves, so open views can reload.</summary>
    event Action? RootChanged;

    /// <summary>Where the backlog lives when nothing has been configured.</summary>
    string DefaultRootDirectory { get; }

    /// <summary>Where the backlog lives right now. Read per call rather than
    /// handed over once, so pointing the app at a different folder takes effect
    /// without restarting it.</summary>
    string RootDirectory { get; }

    bool IsDefaultRoot { get; }

    /// <summary>Where the entry markdown files themselves are written — shown on
    /// the settings page so the folder can be found in a file manager.</summary>
    string EntriesDirectory { get; }

    string InboxDirectory { get; }

    /// <summary>Points the app at a different folder. Returns an error message
    /// when the folder cannot be used, rather than throwing — a bad path typed
    /// into a settings field is an ordinary thing to do, not an exception.</summary>
    string? TryUseRoot(string? path);

    /// <summary>Returns the app to its default per-user folder.</summary>
    string? ResetToDefault();

    /// <summary>
    /// Where the repository-authored <c>.backlog</c> entries for a scope are.
    /// <para>
    /// This is the load-bearing member. <c>.backlog</c> is one of the configured
    /// knowledge folders, so resolving it is the same lookup Second Brain does
    /// for <c>.domain</c> or <c>.arc42</c> — and asking Second Brain for it would
    /// make Backlog Management depend on a context the map says it only partners
    /// with. Asking its own store instead pushes the join down into the adapter,
    /// which is allowed to see both.
    /// </para>
    /// </summary>
    BacklogFolderLocation ResolveBacklogFolder(string? repositoryAlias);
}

/// <summary>
/// Where a scope's repository-authored backlog folder is, or that there is not
/// one. <paramref name="RepositoryRootPath"/> is the repository's local clone
/// directory when the scope is a repository and null otherwise, because a
/// relative path shown to a person should be relative to the clone rather than
/// to the folder inside it.
/// </summary>
public sealed record BacklogFolderLocation(
    bool Available,
    string? FullPath,
    string? RepositoryRootPath,
    string? RepositoryFullName,
    string? RepositoryAlias)
{
    public static BacklogFolderLocation None { get; } = new(false, null, null, null, null);
}
