namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// Where the task store lives on disk.
/// <para>
/// Not to be confused with <c>ITaskRepository</c>, which reads and writes the tasks
/// themselves. This one only answers <em>where</em> they are kept: the root
/// directory, the database path beneath it, and the folders that sit alongside.
/// </para>
/// <para>
/// What it points at is wider than its name: the root it hands out also holds
/// <see cref="InboxDirectory"/>, which the Inbox context owns. That is a placement
/// problem rather than a naming one — a workspace-wide port declared inside one
/// context's abstractions. Moving it out as a shared <c>IWorkspaceStore</c> is a
/// relocation rather than a rename, so it belongs to its own change.
/// </para>
/// <para>
/// The setting itself is deliberately <em>not</em> stored in the workspace folder —
/// it is kept in a fixed per-user location, because a pointer that moves with
/// the thing it points at is no pointer at all.
/// </para>
/// <para>
/// This is narrower than the store behind it, and the narrowing is forced rather
/// than preferred. The adapter also remembers which GitHub repository backs the
/// storage folder; that is expressed in a
/// <c>Backlog.Infrastructure.GitHub</c> type, and an abstractions project may not
/// reference an infrastructure adapter
/// (<c>ModuleBoundaryTests.A_module_never_references_infrastructure</c>). Those
/// members have one consumer besides tests — the desktop settings screen — so
/// they stayed on the adapter, where that screen takes them directly. Do not add
/// them back here.
/// </para>
/// </summary>
public interface ITaskStore
{
    /// <summary>Raised after the store moves, so open views can reload.</summary>
    event Action? RootChanged;

    /// <summary>Where tasks live when nothing has been configured.</summary>
    string DefaultRootDirectory { get; }

    /// <summary>Where tasks live right now. Read per call rather than
    /// handed over once, so pointing the app at a different folder takes effect
    /// without restarting it.</summary>
    string RootDirectory { get; }

    bool IsDefaultRoot { get; }

    /// <summary>The database file the tasks are kept in — shown on the settings
    /// page so it can be found in a file manager, and so it is obvious what to
    /// copy for a backup.</summary>
    string DatabasePath { get; }

    string InboxDirectory { get; }

    /// <summary>Points the app at a different folder. Returns an error message
    /// when the folder cannot be used, rather than throwing — a bad path typed
    /// into a settings field is an ordinary thing to do, not an exception.</summary>
    string? TryUseRoot(string? path);

    /// <summary>Returns the app to its default per-user folder.</summary>
    string? ResetToDefault();
}
