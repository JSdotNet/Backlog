namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Which surface the shell is showing below its chrome.
/// <para>
/// One field with three states, rather than a flag per takeover, and that is the
/// whole point: a takeover cannot coexist with the workspace, and the two
/// takeovers cannot coexist with each other. Tools is the Dev PC Management
/// context and Dashboard is the Dashboard context — opening either means the
/// reader has stopped looking at the backlog, so there is no arrangement in which
/// one shares the screen with the panes or with the other. Two booleans would have
/// to be kept out of the fourth state by hand; this cannot reach it.
/// </para>
/// <para>
/// That second attribution used to read "Dashboard is Monitoring's", which is what
/// it was while the dashboard was a derived view inside Monitoring with no module
/// behind it. It is its own context now, with its own module, and the only thing
/// that changed here is which context the takeover belongs to.
/// </para>
/// <para>
/// Deliberately not a fourth <see cref="GlobalPane"/>. The panes have a capacity
/// rule the viewport sets and an invariant that one of them is always on screen;
/// a takeover has neither, and folding it in would let the window width close a
/// takeover or a takeover evict the reader's pane selection. Because this is a
/// separate field, closing a surface simply reveals the selection that was there
/// all along — no save, no restore, nothing to get wrong.
/// </para>
/// </summary>
internal enum WorkspaceSurface
{
    /// <summary>The three side-by-side panes, with the roadmap band above them.</summary>
    Workspace,

    /// <summary>Dev PC Management, taking the whole screen.</summary>
    Tools,

    /// <summary>The Dashboard context, taking the whole screen.</summary>
    Dashboard
}
